using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Cryptography;

namespace Rakion.Core.Tests.Cryptography;

[TestClass]
public sealed class NyxLauncherCryptoTests
{
    [TestMethod]
    public void RoundTrip_ValidCredentials_EncryptsAndDecryptsAccurately()
    {
        string username = "testuser";
        string password = "SecretPassword123";
        uint salt = 0x98765432;
        ushort version = 1026;

        byte[] payload = new byte[NyxLauncherCrypto.PayloadLength];
        NyxLauncherCrypto.EncryptPayload(username, password, salt, version, payload);

        // Verify Part 1
        bool part1Success = NyxLauncherCrypto.TryDecryptPart1(payload.AsSpan(0, 32), out string decryptedUser, out uint decryptedSalt);
        Assert.IsTrue(part1Success);
        Assert.AreEqual(username, decryptedUser);
        Assert.AreEqual(salt, decryptedSalt);

        // Verify Part 2 challenge
        bool part2Success = NyxLauncherCrypto.TryVerifyPasswordChallenge(payload.AsSpan(32, 32), username, password, salt, out ushort decryptedVersion);
        Assert.IsTrue(part2Success);
        Assert.AreEqual(version, decryptedVersion);
    }

    [TestMethod]
    public void VerifyPasswordChallenge_WrongPassword_ReturnsFalse()
    {
        string username = "admin";
        string correctPassword = "adminPassword";
        string wrongPassword = "wrongPassword";
        uint salt = 0x11223344;
        ushort version = 1026;

        byte[] payload = new byte[NyxLauncherCrypto.PayloadLength];
        NyxLauncherCrypto.EncryptPayload(username, correctPassword, salt, version, payload);

        bool success = NyxLauncherCrypto.TryVerifyPasswordChallenge(payload.AsSpan(32, 32), username, wrongPassword, salt, out _);
        Assert.IsFalse(success);
    }

    [TestMethod]
    public void VerifyPasswordChallenge_WrongSalt_ReturnsFalse()
    {
        string username = "admin";
        string password = "adminPassword";
        uint correctSalt = 0x11223344;
        uint wrongSalt = 0x99887766;
        ushort version = 1026;

        byte[] payload = new byte[NyxLauncherCrypto.PayloadLength];
        NyxLauncherCrypto.EncryptPayload(username, password, correctSalt, version, payload);

        bool success = NyxLauncherCrypto.TryVerifyPasswordChallenge(payload.AsSpan(32, 32), username, password, wrongSalt, out _);
        Assert.IsFalse(success);
    }

    [TestMethod]
    public void BuildPacket_ProducesExpected70BytesFrame()
    {
        byte[] packet = NyxLauncherCrypto.BuildPacket("player1", "password123", 0xABCDEF01, 1026);

        Assert.HasCount(70, packet);
        ushort totalLen = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));
        ushort subOpcode = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));

        Assert.AreEqual((ushort)70, totalLen);
        Assert.AreEqual((ushort)0x0000, opcode);
        Assert.AreEqual((ushort)0x1310, subOpcode);

        bool part1Success = NyxLauncherCrypto.TryDecryptPart1(packet.AsSpan(6, 32), out string user, out uint salt);
        Assert.IsTrue(part1Success);
        Assert.AreEqual("player1", user);
        Assert.AreEqual(0xABCDEF01u, salt);

        bool part2Success = NyxLauncherCrypto.TryVerifyPasswordChallenge(packet.AsSpan(38, 32), "player1", "password123", salt, out ushort ver);
        Assert.IsTrue(part2Success);
        Assert.AreEqual((ushort)1026, ver);
    }

    [TestMethod]
    public void TryDecryptPart1_BufferTooShort_ReturnsFalse()
    {
        byte[] shortBuffer = new byte[16];
        bool success = NyxLauncherCrypto.TryDecryptPart1(shortBuffer, out _, out _);
        Assert.IsFalse(success);
    }
}
