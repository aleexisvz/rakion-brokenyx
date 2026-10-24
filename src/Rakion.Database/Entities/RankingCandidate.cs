namespace Rakion.Database.Entities;

/// <summary>
/// A character taking part in the ranking, with the ranks it had before (legacy RankUpdate.exe population query:
/// characters of accounts connected within the last months, ordered by experience; Observed).
/// </summary>
public sealed record RankingCandidate(
    int CharacterId,
    string Username,
    string Name,
    byte Level,
    byte ClassId,
    int Exp,
    int Win,
    int Lose,
    int Draw,
    int PreviousTotalRank,
    int PreviousClassRank,
    int Country);
