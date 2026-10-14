namespace Rakion.Database.Entities;

/// <summary>
/// Represents a user account authentication and authority entity.
/// </summary>
public sealed class UserEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Unique username / login identifier (max 16 characters).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Secure password hash (BCrypt, Argon2) or legacy hash representation (max 255 chars).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public short Country { get; set; }

    public bool NoCountryUpdate { get; set; }

    /// <summary>
    /// User authority level (0 = normal player, 100+ = GM / Administrator).
    /// </summary>
    public int Authority { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

