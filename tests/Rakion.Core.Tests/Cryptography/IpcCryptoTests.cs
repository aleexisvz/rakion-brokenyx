using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Cryptography;

namespace Rakion.Core.Tests.Cryptography;

[TestClass]
public sealed class IpcCryptoTests
{
    [TestMethod]
    public void CalculateChecksum_WithKnownData_ComputesCorrectXorChecksum()
    {
        // 0x10 ^ 0x20 ^ 0x30 = 0x00
        byte[] buffer = [0x10, 0x20, 0x30];
        byte crc = IpcCrypto.CalculateChecksum(buffer);
        Assert.AreEqual((byte)0x00, crc);

        // 0xAA ^ 0x55 = 0xFF
        byte[] buffer2 = [0xAA, 0x55];
        byte crc2 = IpcCrypto.CalculateChecksum(buffer2);
        Assert.AreEqual((byte)0xFF, crc2);
    }

    [TestMethod]
    public void Transform_XorCipher_EncryptsAndDecryptsSymmetrically()
    {
        string key = "RakionSecretKey";
        byte[] original = Encoding.Latin1.GetBytes("ServerInfoRequestPayload12345");
        byte[] workingCopy = (byte[])original.Clone();

        // Encrypt
        IpcCrypto.Transform(workingCopy, key);

        // Verify ciphered data differs from original
        CollectionAssert.AreNotEqual(original, workingCopy);

        // Decrypt
        IpcCrypto.Transform(workingCopy, key);

        // Verify roundtrip matches original
        CollectionAssert.AreEqual(original, workingCopy);
    }
}
