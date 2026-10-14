using Rakion.Core.Protocol;

namespace Rakion.Network.Tcp;

/// <summary>
/// Contract for handling client connection lifecycle and parsed TCP packets.
/// </summary>
public interface ITcpPacketHandler
{
    /// <summary>
    /// Invoked when a new client connection has been established and framed.
    /// </summary>
    ValueTask OnConnectedAsync(TcpConnection connection, CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when a complete, framed packet is received from the client.
    /// </summary>
    /// <param name="connection">The originating connection.</param>
    /// <param name="header">The 4-byte parsed header containing total length and opcode.</param>
    /// <param name="payload">The packet payload excluding the 4-byte header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask OnPacketReceivedAsync(
        TcpConnection connection,
        PacketHeader header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when a client disconnects or an unhandled socket exception terminates the connection.
    /// </summary>
    ValueTask OnDisconnectedAsync(TcpConnection connection, Exception? exception);
}
