namespace Rakion.Database.Repositories;

/// <summary>
/// Result of a stage clear: whether the rank improved (only then anything is stored), the rank stored before, the
/// drops placed in the box and the reward applied.
/// </summary>
public sealed record StageClearOutcome(
    bool Improved,
    int PreviousRank,
    IReadOnlyList<(byte Slot, ushort ItemId)> Drops,
    RoundRewardOutcome? Reward);
