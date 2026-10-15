using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Opcode 0xCDDF: Sent by the Rakion client to request the list of active world servers.
/// Evidence: Observed in BrokenServer.Systems.oPCode and client initialization.
/// </summary>
public sealed record ServerListRequestPacket(ushort ClientVersion = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ServerListRequest;

    public void Serialize(PacketWriter writer)
    {
        if (ClientVersion > 0)
        {
            writer.WriteUInt16(ClientVersion);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out ServerListRequestPacket packet)
    {
        if (payload.Length >= 2)
        {
            var reader = new PacketReader(payload);
            ushort version = reader.ReadUInt16();
            packet = new ServerListRequestPacket(version);
            return true;
        }

        packet = new ServerListRequestPacket(0);
        return true;
    }
}
