namespace Rakion.Database.Repositories;

/// <summary>Character state after a round reward was stored.</summary>
public sealed record RoundRewardOutcome(byte Level, int Exp, int LevelPoint, bool LeveledUp, long Gold)
{
    public int Wins { get; init; }

    public int Losses { get; init; }

    public int Draws { get; init; }

    /// <summary>Levels of the creatures in equipment slots 10, 11 and 12 (0 when the slot holds none).</summary>
    public byte Creature1Level { get; init; }

    public byte Creature2Level { get; init; }

    public byte Creature3Level { get; init; }

    public bool CreatureLeveledUp { get; init; }
}
