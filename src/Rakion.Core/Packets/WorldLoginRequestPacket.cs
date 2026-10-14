using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// World server session authentication handshake request sent by client on opcode 0x0002 or 0x000C.
/// Evidence: Observed in engine.dll ?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z.
/// </summary>
public sealed record WorldLoginRequestPacket(
    uint SessionTicket,
    string Username) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.WorldLogin;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt32(SessionTicket);
        writer.WriteNullTerminatedString(Username);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out WorldLoginRequestPacket packet)
    {
        if (payload.Length < 5)
        {
            packet = default!;
            return false;
        }

        try
        {
            var reader = new PacketReader(payload);
            uint ticket = reader.ReadUInt32();
            string username = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : string.Empty;
            packet = new WorldLoginRequestPacket(ticket, username);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

