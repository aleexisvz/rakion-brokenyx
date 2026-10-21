using System.Buffers.Binary;
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
///   - Derived:
///       Payload bytes [0..31] are encrypted with StaticKey (AES-128-ECB).
///         Block 1 [0..15]  = Username (null-padded string, max 16 bytes).
///         Block 2 [16..31] = UInt32 salt at [0..3] + padding.
///       Payload bytes [32..63] are encrypted with SessionKey (AES-128-ECB), where
///         SessionKey = SHA1(Username[..min(len, 16)] + Password + Salt)[..16].
///         Block 1 [32..47] = Magic 0xA8C2F5D3 (4 bytes) + Password[0..11] (12 bytes).
///         Block 2 [48..63] = Magic 0xA8C2F5D3 (4 bytes) + Password[12..19] (8 bytes) + ClientVersion (4 bytes UInt32 LE).
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

        // Block 1 (bytes 0..15): null-padded Latin1 username
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
    /// Derives the 16-byte session AES key from username, candidate password, and salt:
    /// SessionKey = SHA1(username[..min(len, 16)] + password + salt)[..16].
    /// </summary>
    public static byte[] DeriveSessionKey(string username, string password, uint salt)
    {
        string truncatedUser = username.Length > 16 ? username[..16] : username;
        byte[] userBytes = Encoding.Latin1.GetBytes(truncatedUser);
        byte[] passBytes = Encoding.Latin1.GetBytes(password);
        byte[] saltBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(saltBytes, salt);

        byte[] combined = new byte[userBytes.Length + passBytes.Length + saltBytes.Length];
        Buffer.BlockCopy(userBytes, 0, combined, 0, userBytes.Length);
        Buffer.BlockCopy(passBytes, 0, combined, userBytes.Length, passBytes.Length);
        Buffer.BlockCopy(saltBytes, 0, combined, userBytes.Length + passBytes.Length, saltBytes.Length);

        byte[] hash = SHA1.HashData(combined);
        byte[] sessionKey = new byte[16];
        Buffer.BlockCopy(hash, 0, sessionKey, 0, 16);
        return sessionKey;
    }

    /// <summary>
    /// Verifies the second 32 bytes of the launcher payload against a candidate password.
    /// If candidate password is valid, decrypts the two 16-byte blocks, verifies the magic constant,
    /// verifies that the decrypted password slice matches, and extracts the client version.
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

        // Verify Block 1 magic (bytes 0..3)
        uint magic1 = BinaryPrimitives.ReadUInt32LittleEndian(decrypted[..4]);
        if (magic1 != BlockMagic)
        {
            return false;
        }

        // Verify Block 2 magic (bytes 16..19)
        uint magic2 = BinaryPrimitives.ReadUInt32LittleEndian(decrypted.Slice(16, 4));
        if (magic2 != BlockMagic)
        {
            return false;
        }

        // Reconstruct decrypted password: 12 bytes from block 1 + 8 bytes from block 2
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

        // 2. Part 2: Pass blocks encrypted with derived session key (32 bytes)
        byte[] sessionKey = DeriveSessionKey(username, password, salt);
        Span<byte> part2Plain = stackalloc byte[32];
        part2Plain.Clear();

        // Block 1: Magic (4 bytes) + password[0..11] (12 bytes)
        BinaryPrimitives.WriteUInt32LittleEndian(part2Plain[..4], BlockMagic);
        byte[] passBytes = Encoding.Latin1.GetBytes(password);
        int passBlock1Len = Math.Min(passBytes.Length, 12);
        passBytes.AsSpan(0, passBlock1Len).CopyTo(part2Plain.Slice(4, passBlock1Len));

        // Block 2: Magic (4 bytes) + password[12..19] (8 bytes) + version (4 bytes)
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
