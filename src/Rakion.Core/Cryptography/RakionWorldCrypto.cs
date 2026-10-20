using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Rakion.Core.Cryptography;

/// <summary>
/// Implements the clean-room Rijndael/AES-128 cryptographic layer used by the Rakion
/// v258 client and World Server for TCP communications on port 40708.
///
/// Wire Framing:
///   [0..1] ushort TotalLength (including 2-byte header, equals 2 + BlockCount * 16)
///   [2..]  Ciphertext (BlockCount * 16 bytes)
///
/// Block Structure (ECB Mode):
///   Each 16-byte block contains:
///     [0..3]  uint Magic (0x0000C47F)
///     [4..15] byte[12] Plaintext data
///
/// Plaintext Message Structure:
///   [0..1] ushort Opcode
///   [2..]  Payload bytes
/// </summary>
public static class RakionWorldCrypto
{
    public const uint BlockMagic = 0x0000C47F;
    public const int BlockSize = 16;
    public const int DataPerBlock = 12;
    public const int HeaderSize = 2;

    public static readonly byte[] Key =
    [
        0xE1, 0x3A, 0x7E, 0xF5, 0x37, 0x2C, 0x10, 0x4D,
        0x4E, 0xCE, 0xB3, 0x0C, 0x56, 0x26, 0xA4, 0x8E
    ];

    /// <summary>
    /// Attempts to decrypt a WorldServer ciphertext buffer into plaintext.
    /// </summary>
    public static bool TryDecrypt(
        ReadOnlySpan<byte> ciphertext,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;

        if (ciphertext.IsEmpty || (ciphertext.Length % BlockSize) != 0)
        {
            return false;
        }

        int blockCount = ciphertext.Length / BlockSize;
        int requiredPlaintextLen = blockCount * DataPerBlock;
        if (destination.Length < requiredPlaintextLen)
        {
            return false;
        }

        using var aes = Aes.Create();
        aes.Key = Key;

        Span<byte> decryptedBlocks = stackalloc byte[ciphertext.Length];
        aes.DecryptEcb(ciphertext, decryptedBlocks, PaddingMode.None);

        for (int i = 0; i < blockCount; i++)
        {
            int blockOffset = i * BlockSize;
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(decryptedBlocks.Slice(blockOffset, 4));
            if (magic != BlockMagic)
            {
                return false;
            }

            ReadOnlySpan<byte> dataSlice = decryptedBlocks.Slice(blockOffset + 4, DataPerBlock);
            dataSlice.CopyTo(destination.Slice(i * DataPerBlock, DataPerBlock));
        }

        bytesWritten = requiredPlaintextLen;
        return true;
    }

    /// <summary>
    /// Encrypts plaintext message into a framed WorldServer packet (including 2-byte length header).
    /// </summary>
    public static int Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> destination)
    {
        int blockCount = (plaintext.Length + DataPerBlock - 1) / DataPerBlock;
        if (blockCount == 0)
        {
            blockCount = 1;
        }

        int totalLength = HeaderSize + blockCount * BlockSize;
        if (destination.Length < totalLength)
        {
            throw new ArgumentException("Destination buffer too small for encrypted WorldServer packet.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination[..HeaderSize], (ushort)totalLength);

        Span<byte> rawBlocks = stackalloc byte[blockCount * BlockSize];
        rawBlocks.Clear();

        for (int i = 0; i < blockCount; i++)
        {
            int rawOffset = i * BlockSize;
            BinaryPrimitives.WriteUInt32LittleEndian(rawBlocks.Slice(rawOffset, 4), BlockMagic);

            int plainOffset = i * DataPerBlock;
            int bytesToCopy = Math.Min(DataPerBlock, plaintext.Length - plainOffset);
            if (bytesToCopy > 0)
            {
                plaintext.Slice(plainOffset, bytesToCopy).CopyTo(rawBlocks.Slice(rawOffset + 4, bytesToCopy));
            }
        }

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.EncryptEcb(rawBlocks, destination[HeaderSize..totalLength], PaddingMode.None);

        return totalLength;
    }

    /// <summary>
    /// Encrypts a server packet directly into a byte array for TCP transmission to client.
    /// Wire plaintext layout:
    ///   [0..1] ushort Opcode
    ///   [2..]  Payload bytes
    /// Note: As observed in engine.dll (0x361901D1, 0x36197AA7) and RakionWorldServ.exe (0x4038E0, 0x40493C),
    /// server-to-client packets have NO SequenceId in the plaintext. The payload immediately follows the 2-byte opcode.
    /// </summary>
    public static byte[] EncryptServerPacket(ushort opcode, ReadOnlySpan<byte> payload)
    {
        int plainLen = 2 + payload.Length;
        Span<byte> plain = stackalloc byte[plainLen];
        BinaryPrimitives.WriteUInt16LittleEndian(plain[..2], opcode);
        payload.CopyTo(plain[2..]);

        int blockCount = (plainLen + DataPerBlock - 1) / DataPerBlock;
        if (blockCount == 0)
        {
            blockCount = 1;
        }

        int totalLen = HeaderSize + blockCount * BlockSize;
        byte[] result = new byte[totalLen];
        Encrypt(plain, result);
        return result;
    }

    /// <summary>
    /// Encrypts a client packet directly into a byte array for TCP transmission to server (used in tests/client simulation).
    /// Wire plaintext layout:
    ///   [0..1] ushort Opcode
    ///   [2..3] ushort SequenceId
    ///   [4..]  Payload bytes
    /// Note: As observed in engine.dll 0x361905E0 (SendTCPPacket), the client writes SequenceId at [2..3].
    /// </summary>
    public static byte[] EncryptClientPacket(ushort sequenceId, ushort opcode, ReadOnlySpan<byte> payload)
    {
        int plainLen = 4 + payload.Length;
        Span<byte> plain = stackalloc byte[plainLen];
        BinaryPrimitives.WriteUInt16LittleEndian(plain[..2], opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(plain[2..4], sequenceId);
        payload.CopyTo(plain[4..]);

        int blockCount = (plainLen + DataPerBlock - 1) / DataPerBlock;
        if (blockCount == 0)
        {
            blockCount = 1;
        }

        int totalLen = HeaderSize + blockCount * BlockSize;
        byte[] result = new byte[totalLen];
        Encrypt(plain, result);
        return result;
    }

    /// <summary>
    /// Helper to encrypt a packet directly into a byte array for TCP transmission.
    /// Defaults to server-to-client packet format (Opcode + Payload).
    /// </summary>
    public static byte[] EncryptPacket(ushort opcode, ReadOnlySpan<byte> payload)
        => EncryptServerPacket(opcode, payload);

    /// <summary>
    /// Compatibility overload for server-to-client packet transmission. Note that server-to-client packets do not use SequenceId.
    /// </summary>
    public static byte[] EncryptPacket(ushort sequenceId, ushort opcode, ReadOnlySpan<byte> payload)
        => EncryptServerPacket(opcode, payload);
}
