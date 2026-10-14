using Rakion.Core.Enums;

namespace Rakion.Database.Entities;

/// <summary>
/// Master template for weapons, armor, potions, rings, amulets, and summonable creature cells.
/// </summary>
public sealed class ItemEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Item type category (weapon, armor, creature, consumable, etc.).
    /// </summary>
    public byte Type { get; set; }

    /// <summary>
    /// Compatible character class (or 255 for all classes).
    /// </summary>
    public CharacterClass Class { get; set; }

    /// <summary>
    /// Minimum character level required to equip or use.
    /// </summary>
    public byte RequiredLevel { get; set; }

    /// <summary>
    /// Shop availability flag (0 = not sold, >0 = shop catalog category).
    /// </summary>
    public byte Shop { get; set; }

    public int GoldPrice { get; set; }

    public int CashPrice { get; set; }

    // Stat bonuses provided by the item
    public int Hit1 { get; set; }
    public int Hit2 { get; set; }
    public int Hit3 { get; set; }
    public int Hit4 { get; set; }
    public int Chit { get; set; }
    public int Ap { get; set; }
    public int Hp { get; set; }
    public int MaxCp { get; set; }
    public int Power { get; set; }
}

