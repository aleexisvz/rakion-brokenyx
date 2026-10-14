namespace Rakion.Database.Entities;

/// <summary>
/// Represents a social friendship link between users.
/// </summary>
public sealed class BuddyEntity
{
    public string UserId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string BuddyUserId { get; set; } = string.Empty;
}

