namespace Rakion.Database.Entities;

/// <summary>
/// Represents account-level game state, currency, slot progression and subscription attributes.
/// </summary>
public sealed class UserGameInfoEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key referencing UserEntity.Id.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Account username for rapid display and legacy indexing.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// In-game gold balance. Default 10,000 for new accounts.
    /// </summary>
    public long Gold { get; set; } = 10_000;

    public bool TutorialCompleted { get; set; }

    public bool StageLevelFree { get; set; }

    /// <summary>
    /// Number of unlocked character slots (default 3, maximum 6).
    /// </summary>
    public byte SlotCount { get; set; } = 3;

    /// <summary>
    /// Number of unlocked inventory bags (default 1, maximum 3).
    /// </summary>
    public byte BagCount { get; set; } = 1;

    public bool IsBanned { get; set; }

    public DateTime? BanDate { get; set; }

    public string? BanReason { get; set; }

    public int ClanId { get; set; }

    public int ClanPoint { get; set; }

    public int ClanRank { get; set; }

    public int ClanGrade { get; set; }

    public string? BuddyName { get; set; }

    public long PowerTime { get; set; }

    public int PowerLevelPoint { get; set; }

    public DateTime? PowerTimeDate { get; set; }

    public string? ActiveCharacterName { get; set; }

    public short RankGrade { get; set; }

    public int Country { get; set; } = 9;

    public DateTime? LastConnect { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

