namespace Rakion.Database.Repositories;

/// <summary>Result of a stat reset with the cash and points after it.</summary>
public sealed record StatResetOutcome(StatResetStatus Status, int Cash, int LevelPoints, int PowerPoints);
