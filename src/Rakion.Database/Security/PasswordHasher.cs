using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;

namespace Rakion.Database.Security;

/// <summary>
/// Production password hasher supporting modern BCrypt with transparent upgrade
/// for legacy Rakion passwords (MD5 hex hashes or plaintext).
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int DefaultWorkFactor = 11;

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, DefaultWorkFactor);
    }

    public bool VerifyPassword(string providedPassword, string storedHash, out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrEmpty(providedPassword) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        // 1. Modern BCrypt hash check ($2a$, $2b$, $2x$, $2y$)
        if (IsBcryptHash(storedHash))
        {
            try
            {
                bool isValid = BCrypt.Net.BCrypt.EnhancedVerify(providedPassword, storedHash);
                if (isValid)
                {
                    // Check if current hash uses an obsolete work factor
                    needsRehash = BCrypt.Net.BCrypt.PasswordNeedsRehash(storedHash, DefaultWorkFactor);
                }
                return isValid;
            }
            catch (SaltParseException)
            {
                // Fallthrough to legacy check if format was malformed
            }
        }

        // 2. Legacy MD5 32-character hexadecimal check
        if (storedHash.Length == 32 && IsHex(storedHash))
        {
            string computedMd5 = ComputeMd5Hex(providedPassword);
            if (string.Equals(computedMd5, storedHash, StringComparison.OrdinalIgnoreCase))
            {
                needsRehash = true;
                return true;
            }
        }

        // 3. Legacy plaintext fallback (observed in some early private server setups)
        if (string.Equals(providedPassword, storedHash, StringComparison.Ordinal))
        {
            needsRehash = true;
            return true;
        }

        return false;
    }

    private static bool IsBcryptHash(string value)
    {
        return value.Length >= 4 &&
               value[0] == '$' &&
               value[1] == '2' &&
               (value[2] == 'a' || value[2] == 'b' || value[2] == 'y' || value[2] == 'x') &&
               value[3] == '$';
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static string ComputeMd5Hex(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = MD5.HashData(inputBytes);
        return Convert.ToHexStringLower(hash);
    }
}

