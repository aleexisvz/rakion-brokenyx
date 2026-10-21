using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Rakion.Core.Cryptography;

/// <summary>
/// Implements clean-room cryptographic routines for NyxLauncher authentication (Opcode 0x1310).
///
/// Evidence:
///   - Observed: Static 16-byte AES key located at VA 0x464FA0 in NyxLauncher.exe
///     (a9 27 53 04 1b fc ac e6 5b 23 38 34 68 46 03 8c).
///   - Observed: Block magic 0xA8C2F5D3 located at VA 0x4053E5 in NyxLauncher.exe.
///   - Observed: Wire framing of 70 bytes (0x0046 length, 0x0000 opcode, 0x1310 sub-opcode, 64-byte payload).
///   - Observed: Session AES key derivation via Softnyx modified SHA-0 (FIPS 180 original without the 1-bit shift):
///       SessionKey = SHA0(Username[..min(len, 16)] + Password + Salt)[..16] (as little-endian uint32s).
///   - Observed: Part 1 (bytes [0..31]) encrypted with StaticKey (AES-128-ECB).
///       Block 0 [0..15]  = Username (null-padded Latin1 string, max 16 bytes).
///       Block 1 [16..31] = UInt32 salt at [0..3] + padding.
///   - Observed: Part 2 (bytes [32..63]) encrypted with SessionKey (AES-128-ECB).
///       Block 0 [32..47] = Magic 0xA8C2F5D3 (4 bytes LE) + Password[0..11] (12 bytes).
///       Block 1 [48..63] = Magic 0xA8C2F5D3 (4 bytes LE) + Password[12..19] (8 bytes) + ClientVersion (4 bytes UInt32 LE).
/// </summary>
public static class NyxLauncherCrypto
{
    public const int PacketLength = 70;
    public const int HeaderSize = 6; // 4-byte header + 2-byte sub-opcode
    public const int PayloadLength = 64;
    public const int BlockSize = 16;
    public const uint BlockMagic = 0xA8C2F5D3;

    public static readonly byte[] StaticKey =
    [
        0xA9, 0x27, 0x53, 0x04, 0x1B, 0xFC, 0xAC, 0xE6,
        0x5B, 0x23, 0x38, 0x34, 0x68, 0x46, 0x03, 0x8C
    ];

    /// <summary>
    /// Attempts to decrypt the first 32 bytes of the launcher payload using the static key,
    /// extracting the username and random salt.
    /// </summary>
    public static bool TryDecryptPart1(ReadOnlySpan<byte> payloadPart1, out string username, out uint salt)
    {
        username = string.Empty;
        salt = 0;

        if (payloadPart1.Length < 32)
        {
            return false;
        }

        Span<byte> decrypted = stackalloc byte[32];
        using (var aes = Aes.Create())
        {
            aes.Key = StaticKey;
            aes.DecryptEcb(payloadPart1[..32], decrypted, PaddingMode.None);
        }

        // Block 0 (bytes 0..15): null-padded Latin1 username
        ReadOnlySpan<byte> userSlice = decrypted[..16];
        int nullIdx = userSlice.IndexOf((byte)0);
        ReadOnlySpan<byte> validUserBytes = nullIdx >= 0 ? userSlice[..nullIdx] : userSlice;
        string candidate = Encoding.Latin1.GetString(validUserBytes).Trim();

        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length < 2 ||
            candidate.Length > 16 ||
            !candidate.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))
        {
            return false;
        }

        username = candidate;
        salt = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.Slice(16, 4));
        return true;
    }

    /// <summary>
    /// Computes the 16-byte session AES key using Softnyx's modified SHA-0 hashing function:
    /// SessionKey = SHA0(Username[..min(len, 16)] + Password + Salt)[..16] (as little-endian uint32s).
    /// </summary>
    public static byte[] DeriveSessionKey(string username, string password, uint salt)
    {
        string truncatedUser = username.Length > 16 ? username[..16] : username;
        byte[] userBytes = Encoding.Latin1.GetBytes(truncatedUser);
        byte[] passBytes = Encoding.Latin1.GetBytes(password);
        byte[] saltBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(saltBytes, salt);

        byte[] combined = new byte[userBytes.Length + passBytes.Length + 4];
        Buffer.BlockCopy(userBytes, 0, combined, 0, userBytes.Length);
        Buffer.BlockCopy(passBytes, 0, combined, userBytes.Length, passBytes.Length);
        Buffer.BlockCopy(saltBytes, 0, combined, userBytes.Length + passBytes.Length, 4);

        return ComputeSha0(combined);
    }

    /// <summary>
    /// Clean-room implementation of NIST FIPS 180 (SHA-0), outputting the first 16 bytes
    /// (H0..H3) as little-endian 32-bit words.
    /// </summary>
    public static byte[] ComputeSha0(ReadOnlySpan<byte> message)
    {
        uint h0 = 0x67452301;
        uint h1 = 0xEFCDAB89;
        uint h2 = 0x98BADCFE;
        uint h3 = 0x10325476;
        uint h4 = 0xC3D2E1F0;

        ulong bitLength = (ulong)message.Length * 8;
        int paddedLength = ((message.Length + 8) / 64 + 1) * 64;
        byte[] padded = new byte[paddedLength];
        message.CopyTo(padded);
        padded[message.Length] = 0x80;

        BinaryPrimitives.WriteUInt64BigEndian(padded.AsSpan(paddedLength - 8), bitLength);

        Span<uint> w = stackalloc uint[80];

        for (int i = 0; i < paddedLength; i += 64)
        {
            for (int t = 0; t < 16; t++)
            {
                w[t] = BinaryPrimitives.ReadUInt32BigEndian(padded.AsSpan(i + t * 4, 4));
            }

            for (int t = 16; t < 80; t++)
            {
                // SHA-0: NO ROTATION! (FIPS 180 original)
                w[t] = w[t - 3] ^ w[t - 8] ^ w[t - 14] ^ w[t - 16];
            }

            uint a = h0;
            uint b = h1;
            uint c = h2;
            uint d = h3;
            uint e = h4;

            for (int t = 0; t < 80; t++)
            {
                uint f, k;
                if (t < 20)
                {
                    f = (b & c) | (~b & d);
                    k = 0x5A827999;
                }
                else if (t < 40)
                {
                    f = b ^ c ^ d;
                    k = 0x6ED9EBA1;
                }
                else if (t < 60)
                {
                    f = (b & c) | (b & d) | (c & d);
                    k = 0x8F1BBCDC;
                }
                else
                {
                    f = b ^ c ^ d;
                    k = 0xCA62C1D6;
                }

                uint temp = BitOperations.RotateLeft(a, 5) + f + e + k + w[t];
                e = d;
                d = c;
                c = BitOperations.RotateLeft(b, 30);
                b = a;
                a = temp;
            }

            h0 += a;
            h1 += b;
            h2 += c;
            h3 += d;
            h4 += e;
        }

        byte[] output = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), h0);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), h1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), h2);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12, 4), h3);
        return output;
    }

    /// <summary>
    /// Verifies the second 32 bytes of the launcher payload against a candidate password.
    /// If candidate password is valid, decrypts the two 16-byte blocks with the derived SHA-0 key,
    /// verifies the magic constant 0xA8C2F5D3, verifies that the decrypted password slice matches,
    /// and extracts the client version.
    /// </summary>
    public static bool TryVerifyPasswordChallenge(
        ReadOnlySpan<byte> payloadPart2,
        string username,
        string candidatePassword,
        uint salt,
        out ushort clientVersion)
    {
        clientVersion = 1026;

        if (payloadPart2.Length < 32)
        {
            return false;
        }

        byte[] sessionKey = DeriveSessionKey(username, candidatePassword, salt);
        Span<byte> decrypted = stackalloc byte[32];

        try
        {
            using var aes = Aes.Create();
            aes.Key = sessionKey;
            aes.DecryptEcb(payloadPart2[..32], decrypted, PaddingMode.None);
        }
        catch (CryptographicException)
        {
            return false;
        }

        // Verify Block 0 magic (bytes 0..3)
        uint magic1 = BinaryPrimitives.ReadUInt32LittleEndian(decrypted[..4]);
        if (magic1 != BlockMagic)
        {
            return false;
        }

        // Verify Block 1 magic (bytes 16..19)
        uint magic2 = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.Slice(16, 4));
        if (magic2 != BlockMagic)
        {
            return false;
        }

        // Reconstruct decrypted password: 12 bytes from block 0 + 8 bytes from block 1
        Span<byte> passBuffer = stackalloc byte[20];
        decrypted.Slice(4, 12).CopyTo(passBuffer[..12]);
        decrypted.Slice(20, 8).CopyTo(passBuffer.Slice(12, 8));

        int nullIdx = passBuffer.IndexOf((byte)0);
        string decryptedPass = Encoding.Latin1.GetString(nullIdx >= 0 ? passBuffer[..nullIdx] : passBuffer);

        if (!string.Equals(decryptedPass, candidatePassword, StringComparison.Ordinal))
        {
            return false;
        }

        uint rawVersion = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.Slice(28, 4));
        clientVersion = rawVersion > 0 ? (ushort)rawVersion : (ushort)1026;
        return true;
    }

    /// <summary>
    /// Attempts to decrypt the second 32 bytes of payload directly using the static key
    /// (fallback for mock or custom launchers that encrypt both parts with StaticKey).
    /// </summary>
    public static bool TryDecryptPart2Direct(
        ReadOnlySpan<byte> payloadPart2,
        out string password,
        out ushort clientVersion)
    {
        password = string.Empty;
        clientVersion = 1026;

        if (payloadPart2.Length < 32)
        {
            return false;
        }

        Span<byte> decrypted = stackalloc byte[32];
        try
        {
            using var aes = Aes.Create();
            aes.Key = StaticKey;
            aes.DecryptEcb(payloadPart2[..32], decrypted, PaddingMode.None);
        }
        catch (CryptographicException)
        {
            return false;
        }

        uint magic1 = BinaryPrimitives.ReadUInt32LittleEndian(decrypted[..4]);
        uint magic2 = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.Slice(16, 4));

        if (magic1 == BlockMagic && magic2 == BlockMagic)
        {
            Span<byte> passBuffer = stackalloc byte[20];
            decrypted.Slice(4, 12).CopyTo(passBuffer[..12]);
            decrypted.Slice(20, 8).CopyTo(passBuffer.Slice(12, 8));

            int nullIdx = passBuffer.IndexOf((byte)0);
            password = Encoding.Latin1.GetString(nullIdx >= 0 ? passBuffer[..nullIdx] : passBuffer);
            uint rawVersion = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.Slice(28, 4));
            clientVersion = rawVersion > 0 ? (ushort)rawVersion : (ushort)1026;
            return true;
        }

        // Check if block contains printable ASCII password directly
        int pNullIdx = decrypted.IndexOf((byte)0);
        ReadOnlySpan<byte> passSlice = pNullIdx >= 0 ? decrypted[..pNullIdx] : decrypted[..20];
        string cand = Encoding.Latin1.GetString(passSlice).Trim();
        if (!string.IsNullOrWhiteSpace(cand) && cand.All(c => !char.IsControl(c)))
        {
            password = cand;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Encrypts an authentication request payload exactly as NyxLauncher.exe does.
    /// Produces exactly 64 bytes in destination buffer.
    /// </summary>
    public static void EncryptPayload(
        string username,
        string password,
        uint salt,
        ushort clientVersion,
        Span<byte> destination)
    {
        if (destination.Length < PayloadLength)
        {
            throw new ArgumentException($"Destination must be at least {PayloadLength} bytes.", nameof(destination));
        }

        // 1. Part 1: Username block + Salt block (32 bytes)
        Span<byte> part1Plain = stackalloc byte[32];
        part1Plain.Clear();

        string userTruncated = username.Length > 16 ? username[..16] : username;
        Encoding.Latin1.GetBytes(userTruncated).CopyTo(part1Plain[..16]);
        BinaryPrimitives.WriteUInt32LittleEndian(part1Plain.Slice(16, 4), salt);

        using (var aes = Aes.Create())
        {
            aes.Key = StaticKey;
            aes.EncryptEcb(part1Plain, destination[..32], PaddingMode.None);
        }

        // 2. Part 2: Pass blocks encrypted with derived SHA-0 session key (32 bytes)
        byte[] sessionKey = DeriveSessionKey(username, password, salt);
        Span<byte> part2Plain = stackalloc byte[32];
        part2Plain.Clear();

        // Block 0: Magic (4 bytes) + password[0..11] (12 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(part2Plain[..4], BlockMagic);
        byte[] passBytes = Encoding.Latin1.GetBytes(password);
        int passBlock1Len = Math.Min(passBytes.Length, 12);
        passBytes.AsSpan(0, passBlock1Len).CopyTo(part2Plain.Slice(4, passBlock1Len));

        // Block 1: Magic (4 bytes) + password[12..19] (8 bytes) + version (4 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(part2Plain.Slice(16, 4), BlockMagic);
        if (passBytes.Length > 12)
        {
            int passBlock2Len = Math.Min(passBytes.Length - 12, 8);
            passBytes.AsSpan(12, passBlock2Len).CopyTo(part2Plain.Slice(20, passBlock2Len));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(part2Plain.Slice(28, 4), clientVersion);

        using (var aesSession = Aes.Create())
        {
            aesSession.Key = sessionKey;
            aesSession.EncryptEcb(part2Plain, destination.Slice(32, 32), PaddingMode.None);
        }
    }

    /// <summary>
    /// Builds a full 70-byte packet (4-byte header + 2-byte sub-opcode 0x1310 + 64-byte payload)
    /// suitable for wire transmission.
    /// </summary>
    public static byte[] BuildPacket(
        string username,
        string password,
        uint salt = 0x12345678,
        ushort clientVersion = 1026)
    {
        byte[] packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), PacketLength);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 0x1310);
        EncryptPayload(username, password, salt, clientVersion, packet.AsSpan(HeaderSize, PayloadLength));
        return packet;
    }
}
