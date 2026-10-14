namespace Rakion.Database.Entities;

/// <summary>
/// Specific item instance belonging to a player (equipped or in bag).
/// Tracks blacksmith enchant level, rental duration, slot index, and cell experience.
/// </summary>
public sealed class UserItemEntity
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Character ID that owns or equips the item (or 0 if account-wide inventory).
    /// </summary>
    public int CharacterId { get; set; }

    /// <summary>
    /// Foreign key referencing ItemEntity.Id.
    /// </summary>
    public int ItemId { get; set; }

    /// <summary>
    /// Unique item serial number.
    /// </summary>
    public int SerialNumber { get; set; } = 8000;

    public byte SerialType { get; set; } = 3;

    /// <summary>
    /// Blacksmith enchant/refine level (1 to 15+).
    /// </summary>
    public byte Level { get; set; } = 1;

    /// <summary>
    /// Remaining rental minutes or Unix timestamp expiration (0 or negative = permanent).
    /// </summary>
    public int LimitTimeMinutes { get; set; }

    /// <summary>
    /// Inventory bag or equipment slot index.
    /// </summary>
    public byte SlotIndex { get; set; } = 1;

    /// <summary>
    /// Accumulated experience points for summonable creatures (cells).
    /// </summary>
    public long Exp { get; set; }
}

