using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Cryptography;

namespace Rakion.Core.Tests.Cryptography;

[TestClass]
public sealed class RakionWorldCryptoTests
{
    [TestMethod]
    public void RoundTrip_ArbitraryPayload_EncryptsAndDecryptsAccurately()
    {
        // 70 bytes of sample world login request plaintext
        byte[] originalPlaintext = new byte[70];
        BinaryPrimitives.WriteUInt16LittleEndian(originalPlaintext.AsSpan(0, 2), 0x0002); // WorldLogin opcode
        originalPlaintext[2] = 0; // flag
        BinaryPrimitives.WriteUInt32LittleEndian(originalPlaintext.AsSpan(3, 4), 882901754); // ticket
        System.Text.Encoding.ASCII.GetBytes("admin\0").CopyTo(originalPlaintext, 19);

        // Encrypted wire packet: 2 bytes header + 6 blocks * 16 bytes = 98 bytes
        Span<byte> encryptedWire = stackalloc byte[128];
        int wireLen = RakionWorldCrypto.Encrypt(originalPlaintext, encryptedWire);

        Assert.AreEqual(98, wireLen);
        ushort wireHeaderLen = BinaryPrimitives.ReadUInt16LittleEndian(encryptedWire[..2]);
        Assert.AreEqual(98, wireHeaderLen);

        // Decrypt ciphertext slice (skipping 2-byte header)
        ReadOnlySpan<byte> ciphertext = encryptedWire.Slice(2, wireLen - 2);
        Span<byte> decryptedPlaintext = stackalloc byte[72]; // 6 * 12 = 72 bytes
        bool success = RakionWorldCrypto.TryDecrypt(ciphertext, decryptedPlaintext, out int bytesDecrypted);

        Assert.IsTrue(success);
        Assert.AreEqual(72, bytesDecrypted);

        // Verify opcode and payload preserved
        ushort decryptedOpcode = BinaryPrimitives.ReadUInt16LittleEndian(decryptedPlaintext[..2]);
        Assert.AreEqual((ushort)0x0002, decryptedOpcode);
        Assert.AreEqual((byte)0, decryptedPlaintext[2]);
        uint decryptedTicket = BinaryPrimitives.ReadUInt32LittleEndian(decryptedPlaintext.Slice(3, 4));
        Assert.AreEqual(882901754u, decryptedTicket);
        string decryptedUser = System.Text.Encoding.ASCII.GetString(decryptedPlaintext.Slice(19, 5));
        Assert.AreEqual("admin", decryptedUser);
    }

    [TestMethod]
    public void Decrypt_InvalidMagic_ReturnsFalse()
    {
        byte[] plaintext = new byte[12];
        Span<byte> wire = stackalloc byte[32];
        int wireLen = RakionWorldCrypto.Encrypt(plaintext, wire);

        // Tamper with encrypted bytes
        wire[5] ^= 0xFF;

        Span<byte> dec = stackalloc byte[12];
        bool success = RakionWorldCrypto.TryDecrypt(wire.Slice(2, wireLen - 2), dec, out _);

        Assert.IsFalse(success);
    }

    [TestMethod]
    public void Decrypt_NonMultipleOf16Length_ReturnsFalse()
    {
        Span<byte> invalidCiphertext = stackalloc byte[15];
        Span<byte> dec = stackalloc byte[12];
        bool success = RakionWorldCrypto.TryDecrypt(invalidCiphertext, dec, out _);

        Assert.IsFalse(success);
    }

    [TestMethod]
    public void RoundTrip_EncryptPacket_WithSequenceId_PreservesSequenceAndOpcode()
    {
        byte[] payload = System.Text.Encoding.ASCII.GetBytes("hello_world_payload");
        byte[] wire = RakionWorldCrypto.EncryptPacket(1234, 0x0002, payload);

        ushort wireLen = BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(0, 2));
        Assert.AreEqual(wire.Length, wireLen);

        Span<byte> dec = stackalloc byte[128];
        bool ok = RakionWorldCrypto.TryDecrypt(wire.AsSpan(2, wire.Length - 2), dec, out int decLen);
        Assert.IsTrue(ok);

        ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(dec[..2]);
        ushort op = BinaryPrimitives.ReadUInt16LittleEndian(dec[2..4]);
        Assert.AreEqual((ushort)1234, seq);
        Assert.AreEqual((ushort)0x0002, op);

        string roundtripPayload = System.Text.Encoding.ASCII.GetString(dec.Slice(4, payload.Length));
        Assert.AreEqual("hello_world_payload", roundtripPayload);
    }
}
