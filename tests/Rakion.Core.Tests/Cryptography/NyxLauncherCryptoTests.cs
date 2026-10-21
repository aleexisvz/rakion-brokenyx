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
    public void TryDecryptPart1_WithCapturedNyxLauncherPacket_DecodesUsernameAndSalt()
    {
        // Real packet captured live from NyxLauncher.exe (64 bytes payload)
        byte[] payload = Convert.FromHexString(
            "A41302F43298A2CC6E856CAC2F62806179D94F40C03757CE15EEF7F3042D336EA1FF79DABFCD6EC2B1DD0AEDDD6E17C1AAA4D4880DA1CD12B1AF1273876B42CF");

        bool success = NyxLauncherCrypto.TryDecryptPart1(payload.AsSpan(0, 32), out string username, out uint salt);

        Assert.IsTrue(success);
        Assert.AreEqual("admin", username);
        Assert.AreEqual(0x29u, salt);
    }

    [TestMethod]
    public void TryVerifyPasswordChallenge_WithCapturedNyxLauncherPacket_AsdasdUser()
    {
        // Real packet captured live from NyxLauncher.exe (user "asdasd", pass "asdasd")
        // Payload (64 bytes):
        byte[] payload = Convert.FromHexString(
            "4D2FDCE7BFDA1966949AB21CD2D37C6D845C31AFBF300DD17F6071F0CAE7B236AC4404380CEC772E5EE62F250BE2AFB7B0B85046998BD6623D9727DD3271CBC8");

        bool part1Ok = NyxLauncherCrypto.TryDecryptPart1(payload.AsSpan(0, 32), out string user, out uint salt);
        Assert.IsTrue(part1Ok);
        Assert.AreEqual("asdasd", user);
        Assert.AreEqual(0x29u, salt);

        // Verification with correct password must succeed
        bool correctPass = NyxLauncherCrypto.TryVerifyPasswordChallenge(payload.AsSpan(32, 32), user, "asdasd", salt, out ushort version);
        Assert.IsTrue(correctPass);
        Assert.AreEqual((ushort)1026, version);

        // Verification with wrong password must fail
        bool wrongPass = NyxLauncherCrypto.TryVerifyPasswordChallenge(payload.AsSpan(32, 32), user, "wrongpass", salt, out _);
        Assert.IsFalse(wrongPass);
    }
}
