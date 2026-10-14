using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Rakion.Network.Tcp;

/// <summary>
/// Asynchronous TCP server using System.IO.Pipelines framing to accept and manage client connections.
/// </summary>
public sealed class TcpServer : IAsyncDisposable
{
    private readonly IPEndPoint _listenEndPoint;
    private readonly ITcpPacketHandler _handler;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, TcpConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();
    private Socket? _listenerSocket;
    private Task? _acceptLoopTask;
    private bool _isDisposed;

    public EndPoint? LocalEndPoint => _listenerSocket?.LocalEndPoint;
    public int ActiveConnectionsCount => _connections.Count;

    public TcpServer(IPEndPoint listenEndPoint, ITcpPacketHandler handler, ILogger logger)
    {
        _listenEndPoint = listenEndPoint;
        _handler = handler;
        _logger = logger;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _listenerSocket = new Socket(_listenEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        _listenerSocket.Bind(_listenEndPoint);
        _listenerSocket.Listen(100);

        _logger.LogInformation("TCP Server listening on {LocalEndPoint}", _listenerSocket.LocalEndPoint);
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listenerSocket != null)
        {
            try
            {
                Socket clientSocket = await _listenerSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
                clientSocket.NoDelay = true;

                var connection = new TcpConnection(clientSocket, _handler, _logger);
                _connections.TryAdd(connection.ConnectionId, connection);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await connection.StartAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        _connections.TryRemove(connection.ConnectionId, out _);
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error accepting client connection.");
            }
        }
    }

    public async Task StopAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_listenerSocket != null)
        {
            _listenerSocket.Close();
            _listenerSocket.Dispose();
            _listenerSocket = null;
        }

        if (_acceptLoopTask != null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignored during shutdown
            }
        }

        // Close all active connections
        foreach (TcpConnection conn in _connections.Values)
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
        _connections.Clear();

        _logger.LogInformation("TCP Server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
