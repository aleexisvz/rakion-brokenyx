using System.Buffers.Binary;
using System.Net;
using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Represents a single world server entry in the server list response.
/// </summary>
public sealed record GameServerEntry(
    ushort ServerId,
    string Name,
    IPAddress IpAddress,
    ushort Port,
    ushort UsedSalas,
    ushort MaxSalas,
    ushort UsedSlots,
    ushort MaxSlots,
    bool IsOnline = true);

/// <summary>
/// Opcode 0x0101: Response sent to the Rakion client containing active world servers.
/// Evidence: Observed in BrokenServer.Systems.ServerListPacket (decompiled from BrokenServer_unpacked.exe).
/// </summary>
public sealed record ServerListResponsePacket(IReadOnlyList<GameServerEntry> Servers) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ServerListResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Servers.Count);
        Span<byte> portBytes = stackalloc byte[2];

        foreach (var server in Servers)
        {
            if (server.IsOnline)
            {
                // Write 4-byte IPv4 address
                byte[] ipBytes = server.IpAddress.GetAddressBytes();
                if (ipBytes.Length != 4)
                {
                    // Fallback to loopback if IPv6 or invalid
                    ipBytes = [127, 0, 0, 1];
                }
                writer.WriteBytes(ipBytes);

                // Port is written in network byte order (big endian) as observed in BrokenServer decompilation
                BinaryPrimitives.WriteUInt16BigEndian(portBytes, server.Port);
                writer.WriteBytes(portBytes);

                writer.WriteUInt16(server.UsedSalas);
                writer.WriteUInt16(server.MaxSalas);
                writer.WriteUInt16(server.UsedSlots);
                writer.WriteUInt16(server.MaxSlots);
            }
            else
            {
                // BrokenServer writes ushort(0) for offline servers
                writer.WriteUInt16(0);
            }
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out ServerListResponsePacket packet)
    {
        if (payload.IsEmpty)
        {
            packet = new ServerListResponsePacket([]);
            return false;
        }

        var reader = new PacketReader(payload);
        byte count = reader.ReadByte();
        var list = new List<GameServerEntry>(count);

        for (int i = 0; i < count; i++)
        {
            if (reader.Remaining < 2)
            {
                packet = new ServerListResponsePacket([]);
                return false;
            }

            // Check if remaining payload is at least a full active server entry (14 bytes)
            if (reader.Remaining >= 14)
            {
                ReadOnlySpan<byte> ipSpan = reader.ReadBytes(4);
                var ip = new IPAddress(ipSpan);

                // Port in big endian
                ReadOnlySpan<byte> portSpan = reader.ReadBytes(2);
                ushort port = BinaryPrimitives.ReadUInt16BigEndian(portSpan);

                ushort usedSalas = reader.ReadUInt16();
                ushort maxSalas = reader.ReadUInt16();
                ushort usedSlots = reader.ReadUInt16();
                ushort maxSlots = reader.ReadUInt16();

                list.Add(new GameServerEntry(
                    (ushort)(i + 1),
                    $"WorldServer {i + 1}",
                    ip,
                    port,
                    usedSalas,
                    maxSalas,
                    usedSlots,
                    maxSlots,
                    IsOnline: true));
            }
            else
            {
                // Offline marker (ushort 0)
                ushort status = reader.ReadUInt16();
                list.Add(new GameServerEntry(
                    (ushort)(i + 1),
                    $"WorldServer {i + 1}",
                    IPAddress.Loopback,
                    0,
                    0,
                    0,
                    0,
                    0,
                    IsOnline: false));
            }
        }

        packet = new ServerListResponsePacket(list);
        return true;
    }
}
