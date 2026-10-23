namespace Rakion.Database.Repositories;

/// <summary>Character state after a round reward was stored.</summary>
public sealed record RoundRewardOutcome(byte Level, int Exp, int LevelPoint, bool LeveledUp, long Gold);
