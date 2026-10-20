using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Rakion.Network.Udp;

/// <summary>
/// High-performance asynchronous UDP server socket for Rakion NAT hole punching and real-time packet relay.
/// </summary>
public sealed class UdpServer : IAsyncDisposable
{
    private const int BufferSize = 2048;

    private readonly IPEndPoint _bindEndPoint;
    private readonly IUdpPacketHandler _handler;
    private readonly ILogger _logger;
    private readonly Socket _socket;
    private readonly CancellationTokenSource _cts = new();

    private Task? _receiveLoopTask;
    private bool _isDisposed;

    public EndPoint? LocalEndPoint => _socket.LocalEndPoint;
    public int Port => ((IPEndPoint?)_socket.LocalEndPoint)?.Port ?? _bindEndPoint.Port;
    public byte PortFlag { get; }
    public bool IsRunning => _receiveLoopTask != null && !_cts.IsCancellationRequested;

    public UdpServer(
        IPEndPoint bindEndPoint,
        IUdpPacketHandler handler,
        ILogger logger,
        byte portFlag = 0)
    {
        _bindEndPoint = bindEndPoint;
        _handler = handler;
        _logger = logger;
        PortFlag = portFlag;

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // On Windows, ignore ICMP Port Unreachable (WSAECONNRESET = 10054) caused by previous UDP sends
        const int SIO_UDP_CONNRESET = -1744830452;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _socket.IOControl(SIO_UDP_CONNRESET, [0], null);
            }
        }
        catch
        {
            // Ignore if platform doesn't support IOControl
        }
    }

    public void Start()
    {
        if (_receiveLoopTask != null)
        {
            return;
        }

        _socket.Bind(_bindEndPoint);
        _logger.LogInformation("UDP Server listening on {EndPoint}.", _socket.LocalEndPoint);

        _receiveLoopTask = Task.Run(ReceiveLoopAsync);
    }

    public async ValueTask SendToAsync(
        ReadOnlyMemory<byte> datagram,
        EndPoint destination,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            await _socket.SendToAsync(datagram, SocketFlags.None, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to send UDP datagram to {Destination}.", destination);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await _socket.ReceiveFromAsync(
                        buffer.AsMemory(0, BufferSize),
                        SocketFlags.None,
                        remoteEp,
                        _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(ex, "UDP receive error on {EndPoint}.", _socket.LocalEndPoint);
                    continue;
                }

                if (result.ReceivedBytes <= 0 || result.RemoteEndPoint is not IPEndPoint ipRemoteEp)
                {
                    continue;
                }

                ReadOnlyMemory<byte> payload = buffer.AsMemory(0, result.ReceivedBytes);

                try
                {
                    await _handler.HandleDatagramAsync(this, payload, ipRemoteEp, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error handling UDP datagram from {RemoteEndPoint}.", ipRemoteEp);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore cancellation errors on shutdown
        }

        _socket.Close();
        _socket.Dispose();

        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore task cancellation
            }
        }

        _cts.Dispose();
    }
}

