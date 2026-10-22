using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Network.Udp;
using Rakion.World.Session;

namespace Rakion.World.Handlers;

/// <summary>
/// UDP packet handler processing NAT traversal, hole punching probes, and relay traffic on ports 40708 and 40709.
/// Evidence: Observed in Rakion.bin 0x00473A90 and RakionWorldServ.exe 0x00425D80 / 0x00425FA0.
/// </summary>
public sealed class WorldUdpPacketHandler : IUdpPacketHandler
{
    private readonly ILogger<WorldUdpPacketHandler> _logger;
    private readonly UdpEndpointRegistry? _endpoints;

    public WorldUdpPacketHandler(ILogger<WorldUdpPacketHandler> logger)
        : this(null, logger)
    {
    }

    public WorldUdpPacketHandler(UdpEndpointRegistry? endpoints, ILogger<WorldUdpPacketHandler> logger)
    {
        _endpoints = endpoints;
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
            if (probe.Opcode is (ushort)ClientOpcode.UdpHolePunch1 or (ushort)ClientOpcode.UdpHolePunch2)
            {
                byte flag = server.PortFlag != 0 || probe.Opcode == (ushort)ClientOpcode.UdpHolePunch2 ? (byte)1 : (byte)0;
                byte[] response = UdpProbePacket.CreateResponse(probe.Cookie, flag);
                await server.SendToAsync(response, remoteEndPoint, cancellationToken).ConfigureAwait(false);

                // Inferred: peers reach the player at the endpoint the probe reports; when the client leaves it
                // empty, the observed source is stored in the same encoding (raw in_addr, port in network order).
                uint ip = probe.Ip != 0 ? probe.Ip : BinaryPrimitives.ReadUInt32LittleEndian(remoteEndPoint.Address.MapToIPv4().GetAddressBytes());
                ushort port = probe.Port != 0 ? probe.Port : BinaryPrimitives.ReverseEndianness((ushort)remoteEndPoint.Port);
                bool recorded = _endpoints?.TryRecord(probe.UserIndex, probe.UserId, flag != 0, ip, port) ?? false;
                _logger.LogDebug(
                    "[UDP:{Port}] Probe endpoint for user index {UserIndex}: reported {ReportedIp:X8}:{ReportedPort:X4}, observed {Remote}, stored: {Recorded}.",
                    server.Port,
                    probe.UserIndex,
                    probe.Ip,
                    probe.Port,
                    remoteEndPoint,
                    recorded);

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

