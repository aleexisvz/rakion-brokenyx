using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>Reads the ranking population and stores a computed ranking (roadmap 7.1).</summary>
public interface IRankingRepository
{
    /// <summary>
    /// Characters of accounts connected since <paramref name="activeSince"/>, without the given character auth values,
    /// ordered by experience (highest first) and then id.
    /// </summary>
    Task<IReadOnlyList<RankingCandidate>> GetCandidatesAsync(DateTime activeSince, IReadOnlyCollection<int> excludedAuth, CancellationToken ct = default);

    /// <summary>
    /// Writes the ranks to the characters and replaces the ranking snapshots (total and per class) used by ranking
    /// pages, in one transaction.
    /// </summary>
    Task SaveAsync(IReadOnlyList<RankingEntry> entries, CancellationToken ct = default);
}
