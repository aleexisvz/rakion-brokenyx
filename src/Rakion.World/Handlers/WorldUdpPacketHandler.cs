using System.Net;
using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Network.Udp;

namespace Rakion.World.Handlers;

/// <summary>
/// UDP packet handler processing NAT traversal, hole punching probes, and relay traffic on ports 40708 and 40709.
/// Evidence: Observed in Rakion.bin 0x00473A90 and RakionWorldServ.exe 0x00425D80 / 0x00425FA0.
/// </summary>
public sealed class WorldUdpPacketHandler : IUdpPacketHandler
{
    private readonly ILogger<WorldUdpPacketHandler> _logger;

    public WorldUdpPacketHandler(ILogger<WorldUdpPacketHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask HandleDatagramAsync(
        UdpServer server,
        ReadOnlyMemory<byte> datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        if (UdpProbePacket.TryParseRequest(datagram.Span, out var probe))
        {
            if (probe.Opcode is (ushort)RakionOpcode.UdpHolePunch1 or (ushort)RakionOpcode.UdpHolePunch2)
            {
                byte flag = server.PortFlag != 0 || probe.Opcode == (ushort)RakionOpcode.UdpHolePunch2 ? (byte)1 : (byte)0;
                byte[] response = UdpProbePacket.CreateResponse(probe.Cookie, flag);
                await server.SendToAsync(response, remoteEndPoint, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "[UDP:{Port}] Answered hole punch probe (Opcode 0x{Opcode:X4}, UserIndex {UserIndex}, UserId {UserId}, Cookie 0x{Cookie:X8}) from {RemoteEndPoint}.",
                    server.Port,
                    probe.Opcode,
                    probe.UserIndex,
                    probe.UserId,
                    probe.Cookie,
                    remoteEndPoint);
                return;
            }
        }

        _logger.LogDebug(
            "[UDP:{Port}] Received {Length} bytes from {RemoteEndPoint}.",
            server.Port,
            datagram.Length,
            remoteEndPoint);
    }
}

