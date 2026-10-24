namespace Rakion.Database.Entities;

/// <summary>New ranks of one character: rank in its country, rank in its class and grade (1 best .. 26).</summary>
public sealed record RankingEntry(RankingCandidate Candidate, int TotalRank, int ClassRank, byte Grade);
