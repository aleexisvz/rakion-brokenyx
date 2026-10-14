using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Rakion.Core.Protocol;

namespace Rakion.Network.Tcp;

/// <summary>
/// Encapsulates an active client TCP connection with pipelined framing and asynchronous packet dispatch.
/// </summary>
public sealed class TcpConnection : IAsyncDisposable
{
    private const int MaxAllowedPacketSize = 65535;
    private readonly Socket _socket;
    private readonly ITcpPacketHandler _handler;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _isClosed;

    public Guid ConnectionId { get; } = Guid.NewGuid();
    public EndPoint? RemoteEndPoint { get; }
    public bool IsConnected => Volatile.Read(ref _isClosed) == 0 && _socket.Connected;

    public TcpConnection(Socket socket, ITcpPacketHandler handler, ILogger logger)
    {
        _socket = socket;
        _handler = handler;
        _logger = logger;
        RemoteEndPoint = socket.RemoteEndPoint;
    }

    /// <summary>
    /// Starts the pipelined socket pump and packet framing loop.
    /// </summary>
    public async Task StartAsync()
    {
        var pipe = new Pipe();
        Task writingTask = FillPipeAsync(pipe.Writer, _cts.Token);
        Task readingTask = ReadPipeAsync(pipe.Reader, _cts.Token);

        try
        {
            await _handler.OnConnectedAsync(this, _cts.Token).ConfigureAwait(false);
            await Task.WhenAll(writingTask, readingTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
            // Normal client disconnect or shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Unhandled exception in connection loop.", ConnectionId);
            await CloseAsync(ex).ConfigureAwait(false);
            return;
        }

        await CloseAsync(null).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends raw framed packet bytes over the socket asynchronously.
    /// </summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        await _sendLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(packet, SocketFlags.None, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Reads incoming raw bytes from the socket into the pipe writer.
    /// </summary>
    private async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        const int minimumBufferSize = 1024;

        while (!cancellationToken.IsCancellationRequested)
        {
            Memory<byte> memory = writer.GetMemory(minimumBufferSize);
            int bytesRead;
            try
            {
                bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.Shutdown)
            {
                break;
            }

            if (bytesRead == 0)
            {
                // Socket gracefully closed by remote peer
                break;
            }

            writer.Advance(bytesRead);
            FlushResult result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCompleted || result.IsCanceled)
            {
                break;
            }
        }

        await writer.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads framed packets from the pipe reader and dispatches them to the packet handler.
    /// </summary>
    private async Task ReadPipeAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadPacket(ref buffer, out PacketHeader header, out ReadOnlySequence<byte> payloadSeq))
            {
                // Copy payload to continuous memory for the handler
                byte[] payloadArray = ArrayPool<byte>.Shared.Rent((int)payloadSeq.Length);
                try
                {
                    payloadSeq.CopyTo(payloadArray);
                    ReadOnlyMemory<byte> payload = payloadArray.AsMemory(0, (int)payloadSeq.Length);

                    await _handler.OnPacketReceivedAsync(this, header, payload, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payloadArray);
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted || result.IsCanceled)
            {
                break;
            }
        }

        await reader.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to extract a complete packet frame from the sequence buffer.
    /// </summary>
    private bool TryReadPacket(ref ReadOnlySequence<byte> buffer, out PacketHeader header, out ReadOnlySequence<byte> payload)
    {
        header = default;
        payload = default;

        if (buffer.Length < PacketHeader.HeaderSize)
        {
            return false;
        }

        // Parse header bytes
        Span<byte> headerBytes = stackalloc byte[PacketHeader.HeaderSize];
        buffer.Slice(0, PacketHeader.HeaderSize).CopyTo(headerBytes);

        if (!PacketHeader.TryParse(headerBytes, out header))
        {
            return false;
        }

        if (header.TotalLength < PacketHeader.HeaderSize || header.TotalLength > MaxAllowedPacketSize)
        {
            _logger.LogWarning("[{ConnectionId}] Invalid packet total length: {TotalLength}. Closing connection.", ConnectionId, header.TotalLength);
            throw new PacketParseException($"Invalid packet total length {header.TotalLength}.");
        }

        if (buffer.Length < header.TotalLength)
        {
            // Incomplete frame, wait for more data from socket
            return false;
        }

        // Slice payload sequence excluding the 4-byte header
        payload = buffer.Slice(PacketHeader.HeaderSize, header.PayloadLength);

        // Advance buffer past this entire packet
        buffer = buffer.Slice(header.TotalLength);
        return true;
    }

    public async Task CloseAsync(Exception? exception = null)
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
        {
            return;
        }

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch
        {
            // Suppress shutdown errors
        }
        finally
        {
            _socket.Close();
        }

        try
        {
            await _handler.OnDisconnectedAsync(this, exception).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Error during OnDisconnectedAsync callback.", ConnectionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _cts.Dispose();
        _socket.Dispose();
    }
}
