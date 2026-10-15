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
            // Padded test layout: [0]=0, [1..5]=ticket, [5..17]=12 zeroes padding, [17..]=Username
            if (payload.Length >= 18 && payload[0] == 0 && payload.Slice(5, 12).IndexOfAnyExcept((byte)0) < 0)
            {
                uint retailTicket = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
                var retailReader = new PacketReader(payload.Slice(17));
                string retailUsername = retailReader.ReadNullTerminatedString();
                if (!string.IsNullOrEmpty(retailUsername))
                {
                    packet = new WorldLoginRequestPacket(retailTicket, retailUsername);
                    return true;
                }
            }

            // Direct retail client layout (IScavengerWorldNet::SendLogin):
            // [0..3] = uint Ticket
            // [4..]  = null-terminated string Username
            uint ticket = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);
            var reader = new PacketReader(payload[4..]);
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

