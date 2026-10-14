using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Database.Security;

namespace Rakion.Database.Tests.Security;

[TestClass]
public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [TestMethod]
    public void HashPassword_GeneratesBcryptHash()
    {
        string raw = "SecretPassword123!";
        string hash = _hasher.HashPassword(raw);

        Assert.IsNotNull(hash);
        Assert.IsTrue(hash.StartsWith("$2a$") || hash.StartsWith("$2b$"));
        Assert.IsTrue(_hasher.VerifyPassword(raw, hash, out bool needsRehash));
        Assert.IsFalse(needsRehash, "Fresh BCrypt hash should not require rehash.");
    }

    [TestMethod]
    public void VerifyPassword_RejectsIncorrectPassword()
    {
        string raw = "SecretPassword123!";
        string hash = _hasher.HashPassword(raw);

        bool valid = _hasher.VerifyPassword("WrongPassword", hash, out bool needsRehash);

        Assert.IsFalse(valid);
        Assert.IsFalse(needsRehash);
    }

    [TestMethod]
    public void VerifyPassword_AcceptsLegacyMd5AndRequiresRehash()
    {
        string raw = "123456";
        string md5Hex = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(raw)));

        bool valid = _hasher.VerifyPassword(raw, md5Hex, out bool needsRehash);

        Assert.IsTrue(valid, "Legacy MD5 hash must verify correctly.");
        Assert.IsTrue(needsRehash, "Legacy MD5 hash must trigger transparent rehash flag.");
    }

    [TestMethod]
    public void VerifyPassword_AcceptsLegacyPlaintextAndRequiresRehash()
    {
        string raw = "plainPass789";

        bool valid = _hasher.VerifyPassword(raw, raw, out bool needsRehash);

        Assert.IsTrue(valid, "Legacy plaintext must verify correctly.");
        Assert.IsTrue(needsRehash, "Legacy plaintext must trigger transparent rehash flag.");
    }

    [TestMethod]
    public void VerifyPassword_ReturnsFalseForEmptyOrNull()
    {
        Assert.IsFalse(_hasher.VerifyPassword("", "someHash", out _));
        Assert.IsFalse(_hasher.VerifyPassword("password", "", out _));
    }
}

