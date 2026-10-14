using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Opcode 0x1310 / 0x3000 / 0x4000 (SV_AUTH_LOGIN): Authentication request sent by the client.
/// Evidence: Observed in BrokenServer.Systems.oPCode and IScavengerWorldNet::SendLogin.
/// </summary>
public sealed record LoginRequestPacket(
    string Username,
    string Password,
    ushort ClientVersion = 1026,
    string ClientIp = "127.0.0.1",
    byte Flag = 0,
    ushort SubOpcode = (ushort)RakionOpcode.Login,
    bool IsLauncherHandshake = false) : IPacket
{
    public ushort Opcode => SubOpcode;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(Username);
        writer.WriteNullTerminatedString(Password);
        writer.WriteUInt16(ClientVersion);
        writer.WriteNullTerminatedString(ClientIp);
        writer.WriteByte(Flag);
    }

    /// <summary>
    /// Parses an incoming login payload, automatically resolving whether the sub-opcode was prefixed
    /// or supplied by the header opcode.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> payload, ushort headerOpcode, out LoginRequestPacket packet)
    {
        if (payload.IsEmpty)
        {
            packet = new LoginRequestPacket(string.Empty, string.Empty);
            return false;
        }

        ushort resolvedSubOpcode = headerOpcode;
        ReadOnlySpan<byte> data = payload;

        // If header opcode is None (0x0000), the sub-opcode (0x1310, 0x3000, 0x4000, 0x5000) is the first UInt16
        if (headerOpcode == (ushort)RakionOpcode.None && data.Length >= 2)
        {
            resolvedSubOpcode = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
            data = data[2..];
        }

        try
        {
            var reader = new PacketReader(data);
            string username = reader.ReadNullTerminatedString();
            if (string.IsNullOrWhiteSpace(username) && resolvedSubOpcode == (ushort)RakionOpcode.Login && data.Length >= 28)
            {
                throw new PacketParseException("Empty username in fixed launcher packet.");
            }

            string password = reader.ReadNullTerminatedString();

            ushort clientVersion = reader.Remaining >= 2 ? reader.ReadUInt16() : (ushort)1026;
            string clientIp = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : "127.0.0.1";
            byte flag = reader.Remaining > 0 ? reader.ReadByte() : (byte)0;

            packet = new LoginRequestPacket(username, password, clientVersion, clientIp, flag, resolvedSubOpcode);
            return true;
        }
        catch (PacketParseException)
        {
            // Fallback for NyxLauncher fixed 70-byte packet structure
            if (resolvedSubOpcode == (ushort)RakionOpcode.Login && data.Length >= 28)
            {
                int userOffset = (data[0] == 0 && data[8] != 0) ? 8 : 0;
                ReadOnlySpan<byte> userSlice = data.Slice(userOffset, Math.Min(20, data.Length - userOffset));
                int nullIdx = userSlice.IndexOf((byte)0);
                string user = System.Text.Encoding.Latin1.GetString(nullIdx >= 0 ? userSlice[..nullIdx] : userSlice).Trim();

                string pass = string.Empty;
                int passOffset = userOffset + 24;
                if (data.Length >= passOffset + 16)
                {
                    ReadOnlySpan<byte> passSlice = data.Slice(passOffset, Math.Min(32, data.Length - passOffset));
                    int passNullIdx = passSlice.IndexOf((byte)0);
                    pass = System.Text.Encoding.Latin1.GetString(passNullIdx >= 0 ? passSlice[..passNullIdx] : passSlice).Trim();
                }

                packet = new LoginRequestPacket(user, pass, 1026, "127.0.0.1", 0, resolvedSubOpcode, IsLauncherHandshake: true);
                return true;
            }

            packet = new LoginRequestPacket(string.Empty, string.Empty);
            return false;
        }
    }
}
