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
}
