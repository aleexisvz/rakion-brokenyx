using System.Net;

namespace Rakion.Network.Udp;

/// <summary>
/// Defines a handler for processing incoming UDP datagrams.
/// </summary>
public interface IUdpPacketHandler
{
    /// <summary>
    /// Invoked when a UDP datagram is received from a remote client.
    /// </summary>
    ValueTask HandleDatagramAsync(
        UdpServer server,
        ReadOnlyMemory<byte> datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken);
}

