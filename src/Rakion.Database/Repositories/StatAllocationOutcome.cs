namespace Rakion.Database.Repositories;

/// <summary>What happened when spending one stat point.</summary>
public enum StatAllocationStatus
{
    Allocated,
    NoPoints,
    AtMaximum,
    CharacterNotFound,
}

/// <summary>Result of a stat point allocation: the status and the values after it.</summary>
public sealed record StatAllocationOutcome(StatAllocationStatus Status, int LevelPoints, int PowerPoints, int NewValue);
