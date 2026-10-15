using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// World server session authentication handshake request sent by client on opcode 0x0002 or 0x000C.
/// Evidence: Observed in engine.dll ?SendLogin@IScavengerWorldNet@@UAEXPAD0G0E@Z and ?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z.
/// </summary>
public sealed record WorldLoginRequestPacket(
    uint SessionTicket,
    string Username,
    string Password = "",
    string ClientPath = "",
    ushort Country = 0) : IPacket
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
            // Retail v258 client layout (?SendLogin@IScavengerWorldNet@@UAEXPAD0G0E@Z):
            // [0]   byte Flag (0)
            // [1..] ClientPath (null-terminated string, e.g. "C:\...\Rakion.bin")
            // [..]  Username (null-terminated string)
            // [..]  Password (null-terminated string)
            // [..]  Country (ushort)
            if (payload.Length >= 6 && payload[0] == 0 && payload[1] != 0)
            {
                try
                {
                    var pathReader = new PacketReader(payload.Slice(1));
                    string clientPath = pathReader.ReadNullTerminatedString();
                    if (!string.IsNullOrEmpty(clientPath) &&
                        (clientPath.Contains('\\') || clientPath.Contains('/') ||
                         clientPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                         clientPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        string user = pathReader.Remaining > 0 ? pathReader.ReadNullTerminatedString() : string.Empty;
                        string pass = pathReader.Remaining > 0 ? pathReader.ReadNullTerminatedString() : string.Empty;
                        ushort country = pathReader.Remaining >= 2 ? pathReader.ReadUInt16() : (ushort)0;

                        if (!string.IsNullOrEmpty(user))
                        {
                            packet = new WorldLoginRequestPacket(0, user, pass, clientPath, country);
                            return true;
                        }
                    }
                }
                catch
                {
                    // Fall through to other layouts
                }
            }

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

            // Direct ticket layout (?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z):
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

