using Rakion.Core.Enums;

namespace Rakion.Database.Entities;

/// <summary>
/// Represents an in-game character avatar and combat statistics.
/// </summary>
public sealed class CharacterEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key referencing UserGameInfoEntity.Id (or User.Id).
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Character name displayed in-game (max 12 chars).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public bool IsUsed { get; set; } = true;

    /// <summary>
    /// Deletion security key required for character deletion (legacy default: 'sirmaster').
    /// </summary>
    public string DeleteKey { get; set; } = "sirmaster";

    public byte Auth { get; set; }

    public CharacterClass Class { get; set; }

    public byte Level { get; set; } = 1;

    public int WinCount { get; set; }

    public int LossCount { get; set; }

    public int DrawCount { get; set; }

    public int Exp { get; set; }

    /// <summary>
    /// Unallocated stat points available to distribute.
    /// </summary>
    public byte LevelPoint { get; set; }

    /// <summary>
    /// Slot index within the user's unlocked slots (0 to 5).
    /// </summary>
    public byte SlotIndex { get; set; }

    // Combat attributes
    public byte Hit1 { get; set; }
    public byte Hit2 { get; set; }
    public byte Hit3 { get; set; }
    public byte Hit4 { get; set; }
    public byte Chit { get; set; }
    public byte Hp { get; set; }
    public byte Ap { get; set; }
    public byte AttackSpeed { get; set; }
    public byte Speed { get; set; }
    public byte MaxCp { get; set; }

    // Ranking and inventory configuration
    public byte RankGrade { get; set; }
    public int TotalRank { get; set; }
    public int ClassRank { get; set; }
    public byte PotionSlotCount { get; set; } = 3;

    public DateTime? ChangeTime { get; set; }
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}

