namespace Rakion.Database.Repositories;

/// <summary>One player's result of one round, as the legacy LogFieldGamePointList row.</summary>
public sealed record MatchPointLogEntry(
    int HostCharacterId,
    string HostCharacterName,
    int CharacterId,
    string CharacterName,
    bool PowerUser,
    byte Mode,
    byte MaxRounds,
    byte Round,
    bool Win,
    bool Loss,
    long Exp,
    long Gold);

/// <summary>A character whose results over the checked days reached a threshold (legacy LogFieldGameAbuserCheck).</summary>
public sealed record AbuserFlag(int CharacterId, string CharacterName, bool PowerUser, long Wins, long Losses, long Exp, long Gold, DateTime CheckedAt);

/// <summary>
/// Abuse detection (roadmap 7.2), as the legacy pair World + RankUpdate.exe: every round result is logged, and a daily
/// check flags characters whose totals over the last days reach a threshold. Nothing is banned automatically.
/// </summary>
public interface IAbuserRepository
{
    Task LogMatchPointAsync(MatchPointLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Flags (and stores) every character whose wins, experience or gold logged since <paramref name="since"/> reach a
    /// threshold; returns the new flags.
    /// </summary>
    Task<IReadOnlyList<AbuserFlag>> RunCheckAsync(DateTime since, int winSum, long expSum, long goldSum, CancellationToken ct = default);

    /// <summary>The most recent flags, newest first.</summary>
    Task<IReadOnlyList<AbuserFlag>> GetRecentFlagsAsync(int count, CancellationToken ct = default);
}
