using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Contract C-4 (roadmap 6.6): stores what a character earns in a match and keeps the level table.
/// </summary>
public interface IMatchResultRepository
{
    /// <summary>Creates the level table when missing and inserts or updates its rows. Returns the rows written.</summary>
    Task<int> UpsertClassLevelsAsync(IEnumerable<ClassLevelEntity> levels, CancellationToken ct = default);

    /// <summary>Creates the creature level table when missing and inserts or updates its rows. Returns the rows written.</summary>
    Task<int> UpsertCreatureLevelsAsync(IEnumerable<CreatureLevelEntity> levels, CancellationToken ct = default);

    /// <summary>
    /// Adds a round's experience to the character and its gold to the account in one transaction, levelling up while
    /// the total exceeds the level table (+3 level points per level, level 99 at most). In the same transaction it
    /// counts the round <paramref name="result"/> (<see cref="RoundResult"/>) and adds <paramref name="creatureExp"/>
    /// to the creatures in equipment slots 10..12. Returns null when the character does not belong to the user.
    /// </summary>
    Task<RoundRewardOutcome?> ApplyRoundRewardAsync(
        int userId,
        int characterId,
        int exp,
        int gold,
        byte result = RoundResult.None,
        IReadOnlyList<int>? creatureExp = null,
        CancellationToken ct = default);

    /// <summary>
    /// Stage clear (legacy 0x425010): when <paramref name="rank"/> beats the stored rank of the stage, stores it, puts
    /// the drops in the box and applies the reward like a round (counted as a draw), all in one transaction. A rank
    /// that is not better changes nothing (<see cref="StageClearOutcome.Improved"/> false). Returns null when the
    /// character does not belong to the user.
    /// </summary>
    Task<StageClearOutcome?> ApplyStageClearAsync(
        int userId,
        int characterId,
        byte stage,
        byte rank,
        IReadOnlyList<ushort> drops,
        int exp,
        int gold,
        IReadOnlyList<int>? creatureExp = null,
        CancellationToken ct = default);

    /// <summary>Best rank per stage (100 bytes) for each character; characters without clears get zeros.</summary>
    Task<IReadOnlyDictionary<int, byte[]>> GetStageRanksAsync(IReadOnlyCollection<int> characterIds, CancellationToken ct = default);
}
