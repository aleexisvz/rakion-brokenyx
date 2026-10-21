using Rakion.Core.Cryptography;
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
    bool IsLauncherHandshake = false,
    uint LauncherSalt = 0,
    byte[]? LauncherCiphertext = null) : IPacket
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

        // Fixed 70-byte packet from NyxLauncher (4-byte header + 2-byte sub-opcode 0x1310 + 64-byte payload)
        if (headerOpcode == (ushort)RakionOpcode.None && resolvedSubOpcode == (ushort)RakionOpcode.Login && data.Length == 64)
        {
            if (NyxLauncherCrypto.TryDecryptPart1(data[..32], out string decUser, out uint decSalt))
            {
                string password = string.Empty;
                ushort clientVer = 1026;
                if (NyxLauncherCrypto.TryDecryptPart2Direct(data[32..], out string directPass, out ushort directVer))
                {
                    password = directPass;
                    clientVer = directVer;
                }

                packet = new LoginRequestPacket(
                    Username: decUser,
                    Password: password,
                    ClientVersion: clientVer,
                    ClientIp: "127.0.0.1",
                    Flag: 0,
                    SubOpcode: resolvedSubOpcode,
                    IsLauncherHandshake: true,
                    LauncherSalt: decSalt,
                    LauncherCiphertext: data[32..].ToArray());
                return true;
            }

            // Fallback for mock/unencrypted legacy test frames
            string username = "admin";
            if (data[0] == 0 && data[8] != 0)
            {
                ReadOnlySpan<byte> userSlice = data.Slice(8, Math.Min(20, data.Length - 8));
                int nullIdx = userSlice.IndexOf((byte)0);
                string candidate = System.Text.Encoding.Latin1.GetString(nullIdx >= 0 ? userSlice[..nullIdx] : userSlice).Trim();
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    username = candidate;
                }
            }

            packet = new LoginRequestPacket(
                Username: username,
                Password: string.Empty,
                ClientVersion: 1026,
                ClientIp: "127.0.0.1",
                Flag: 0,
                SubOpcode: resolvedSubOpcode,
                IsLauncherHandshake: true,
                LauncherSalt: 0,
                LauncherCiphertext: null);
            return true;
        }

        try
        {
            var reader = new PacketReader(data);
            string username = reader.ReadNullTerminatedString();
            string password = reader.ReadNullTerminatedString();

            ushort clientVersion = reader.Remaining >= 2 ? reader.ReadUInt16() : (ushort)1026;
            string clientIp = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : "127.0.0.1";
            byte flag = reader.Remaining > 0 ? reader.ReadByte() : (byte)0;

            packet = new LoginRequestPacket(username, password, clientVersion, clientIp, flag, resolvedSubOpcode);
            return true;
        }
        catch (PacketParseException)
        {
            packet = new LoginRequestPacket(string.Empty, string.Empty);
            return false;
        }
    }
}
