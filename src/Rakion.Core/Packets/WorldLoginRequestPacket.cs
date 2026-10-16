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
            // Retail v258 client layout (?SendLogin@IScavengerWorldNet@@UAEXPAD0G0E@Z at 0x36190AD0):
            // Payload after [opcode(2)][seq(2)] is:
            //   [0]    = byte Language
            //   [1..2] = ushort (0x0044 = 68, or payload.Length)
            //   [3..]  = szUserID\0 + szPassword\0 + szLocalIP(8-byte MD5)\0 + ushort wIDCIndex
            // Note: If Rakion.bin's .poly unpacker stub fails to strip argv[0] from lpCmdLine,
            // szUserID at [3..] will be the quoted executable path ("C:\...\Rakion.bin").
            ushort subPacketLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
            if (payload.Length >= 5 && (subPacketLen == 0x44 || subPacketLen == payload.Length || payload[1] == 0x44) && payload[3] >= 0x20 && payload[3] < 0x7F)
            {
                var remainingSpan = payload.Slice(3);
                var retailSubReader = new PacketReader(remainingSpan);
                string userIdOrPath = retailSubReader.ReadNullTerminatedString();

                string secondToken = string.Empty;
                if (remainingSpan.Slice(retailSubReader.Position).IndexOf((byte)0) >= 0)
                {
                    secondToken = retailSubReader.ReadNullTerminatedString();
                }

                string thirdToken = string.Empty;
                if (remainingSpan.Slice(retailSubReader.Position).IndexOf((byte)0) >= 0)
                {
                    thirdToken = retailSubReader.ReadNullTerminatedString();
                }

                if (userIdOrPath.Contains(":\\", System.StringComparison.Ordinal))
                {
                    packet = new WorldLoginRequestPacket(0, secondToken, thirdToken, userIdOrPath, 0);
                    return true;
                }

                packet = new WorldLoginRequestPacket(0, userIdOrPath, secondToken, string.Empty, 0);
                return true;
            }

            // Fallback search if executable path appears at a different offset:
            int colonBackslash = payload.IndexOf(":\\"u8);
            if (colonBackslash >= 1)
            {
                try
                {
                    int pathStart = colonBackslash - 1; // drive letter before ":\"
                    var pathReader = new PacketReader(payload.Slice(pathStart));
                    string clientPath = pathReader.ReadNullTerminatedString();
                    if (!string.IsNullOrEmpty(clientPath))
                    {
                        while (pathReader.Remaining > 0 && pathReader.PeekByte() == 0)
                        {
                            pathReader.ReadByte();
                        }

                        string user = pathReader.Remaining > 0 ? pathReader.ReadNullTerminatedString() : string.Empty;

                        while (pathReader.Remaining > 0 && pathReader.PeekByte() == 0)
                        {
                            pathReader.ReadByte();
                        }

                        string pass = pathReader.Remaining > 0 ? pathReader.ReadNullTerminatedString() : string.Empty;
                        ushort country = pathReader.Remaining >= 2 ? pathReader.ReadUInt16() : (ushort)0;

                        packet = new WorldLoginRequestPacket(0, user, pass, clientPath, country);
                        return true;
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

