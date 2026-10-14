namespace Rakion.Database.Security;

/// <summary>
/// Abstraction for password hashing and validation with legacy migration support.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Generates a cryptographically secure hash using modern BCrypt.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Verifies whether the provided plaintext password matches the stored hash representation.
    /// Detects whether the hash is legacy (MD5 or plaintext) and flags <paramref name="needsRehash"/>
    /// so the caller can seamlessly upgrade the stored hash upon successful login.
    /// </summary>
    bool VerifyPassword(string providedPassword, string storedHash, out bool needsRehash);
}

