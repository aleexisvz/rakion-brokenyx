using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Rakion.Core.Cryptography;

namespace Rakion.Core.Buddy;

/// <summary>
/// Messenger login cryptography. The 208-byte login request is a 32-byte credential (AES-128-ECB with a fixed key:
/// account name, NUL-terminated within 20 bytes, and the server seed at byte 20) followed by 132 bytes of login data
/// sent as 16-byte blocks, each <c>AES(sessionKey, [u32 0x2DBABE65][12 data bytes])</c>, where the session key is the
/// first 16 bytes of SHA-0(account + seed). Evidence: Buddy2.dll 0x100077F9..0x100079C5 (Observed: 0xD0-byte request,
/// 0x84-byte plaintext, marker 0x2DBABE65, logical header 0x1B); the credential key and the block layout are
/// Inferred until verified with the real client.
/// </summary>
public static class BuddyCrypto
{
    public const int LoginPayloadLength = 0xD0;
    public const int CredentialLength = 32;
    public const int LoginDataLength = 0x84;
    public const uint BlockMarker = 0x2DBABE65;
    public const uint LoginDataHeader = 0x1B;
    public const int AccountLength = 20;

    private const int PlainBlock = 12;
    private const int CipherBlock = 16;

    private static readonly byte[] CredentialKey = Convert.FromHexString("2C45926CF3396642B670D006A1FA8182");

    /// <summary>Reads the account and seed from the login credential.</summary>
    public static bool TryReadCredential(ReadOnlySpan<byte> payload, out string account, out uint seed)
    {
        account = string.Empty;
        seed = 0;
        if (payload.Length < CredentialLength)
        {
            return false;
        }

        byte[] clear = Ecb(payload[..CredentialLength], CredentialKey, encrypt: false);
        int end = clear.AsSpan(0, AccountLength).IndexOf((byte)0);
        if (end <= 0)
        {
            return false;
        }

        account = Encoding.Latin1.GetString(clear, 0, end);
        seed = BinaryPrimitives.ReadUInt32LittleEndian(clear.AsSpan(AccountLength, 4));
        return true;
    }

    /// <summary>Builds a login credential (used by tests and tools).</summary>
    public static byte[] CreateCredential(string account, uint seed)
    {
        byte[] clear = new byte[CredentialLength];
        byte[] name = Encoding.Latin1.GetBytes(account);
        name.AsSpan(0, Math.Min(name.Length, AccountLength - 1)).CopyTo(clear);
        BinaryPrimitives.WriteUInt32LittleEndian(clear.AsSpan(AccountLength, 4), seed);
        return Ecb(clear, CredentialKey, encrypt: true);
    }

    public static byte[] SessionKey(string account, uint seed)
    {
        byte[] name = Encoding.Latin1.GetBytes(account);
        byte[] material = new byte[Math.Min(name.Length, AccountLength) + 4];
        name.AsSpan(0, material.Length - 4).CopyTo(material);
        BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(material.Length - 4), seed);
        return NyxLauncherCrypto.ComputeSha0(material);
    }

    /// <summary>Decrypts marker blocks; false when a block does not carry the marker (wrong key or data).</summary>
    public static bool TryDecryptBlocks(ReadOnlySpan<byte> cipher, byte[] key, out byte[] plain)
    {
        plain = [];
        if (cipher.Length == 0 || cipher.Length % CipherBlock != 0)
        {
            return false;
        }

        int blocks = cipher.Length / CipherBlock;
        byte[] clear = Ecb(cipher, key, encrypt: false);
        var output = new byte[blocks * PlainBlock];
        for (int i = 0; i < blocks; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(clear.AsSpan(i * CipherBlock)) != BlockMarker)
            {
                return false;
            }

            clear.AsSpan((i * CipherBlock) + 4, PlainBlock).CopyTo(output.AsSpan(i * PlainBlock));
        }

        plain = output;
        return true;
    }

    /// <summary>Encrypts data into marker blocks (12 data bytes per 16-byte block, zero padded).</summary>
    public static byte[] EncryptBlocks(ReadOnlySpan<byte> plain, byte[] key)
    {
        int blocks = (plain.Length + PlainBlock - 1) / PlainBlock;
        var clear = new byte[blocks * CipherBlock];
        for (int i = 0; i < blocks; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(clear.AsSpan(i * CipherBlock), BlockMarker);
            int count = Math.Min(PlainBlock, plain.Length - (i * PlainBlock));
            plain.Slice(i * PlainBlock, count).CopyTo(clear.AsSpan((i * CipherBlock) + 4));
        }

        return Ecb(clear, key, encrypt: true);
    }

    private static byte[] Ecb(ReadOnlySpan<byte> input, byte[] key, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return encrypt ? aes.EncryptEcb(input, PaddingMode.None) : aes.DecryptEcb(input, PaddingMode.None);
    }
}
