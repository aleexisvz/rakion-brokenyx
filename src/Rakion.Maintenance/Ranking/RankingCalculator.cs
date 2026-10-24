using Rakion.Database.Entities;

namespace Rakion.Maintenance.Ranking;

/// <summary>
/// The legacy ranking rules of RankUpdate.exe (Observed in its SQL and in 0x402300 / 0x402409):
/// <list type="bullet">
/// <item>Total rank per country by experience, highest first; ties share the rank of the first one (100, 100, 90
/// gives 1, 1, 3).</item>
/// <item>Grade 26..15 by fixed experience bands below 32 000.</item>
/// <item>From 32 000 experience, per country in rank order: 1 player grade 1, 4 grade 2, 16 grade 3, then grades
/// 4..13 up to cumulative cuts of 0.1 % (+1), 1, 3, 6, 10, 17, 27, 40, 56 and 76 % of the remaining players, and
/// grade 14 for the rest. A band whose cut is already reached keeps receiving players, as in the legacy loop.</item>
/// <item>Class rank across all countries by experience, with the same tie rule.</item>
/// </list>
/// </summary>
public static class RankingCalculator
{
    public const int HighExp = 32_000;

    private static readonly (int MinExp, byte Grade)[] LowBands =
    [
        (28_000, 15), (24_000, 16), (20_000, 17), (17_000, 18), (13_000, 19), (10_500, 20),
        (8_000, 21), (6_000, 22), (4_000, 23), (2_500, 24), (1_000, 25), (0, 26),
    ];

    /// <param name="candidates">Characters ordered by experience, highest first (then id).</param>
    public static IReadOnlyList<RankingEntry> Compute(IReadOnlyList<RankingCandidate> candidates)
    {
        var totalRank = new Dictionary<int, int>();
        var grade = new Dictionary<int, byte>();
        foreach (var country in candidates.GroupBy(c => c.Country))
        {
            var ordered = country.OrderByDescending(c => c.Exp).ThenBy(c => c.CharacterId).ToList();
            AssignCompetitionRanks(ordered, totalRank);
            AssignGrades(ordered, grade);
        }

        var classRank = new Dictionary<int, int>();
        foreach (var characterClass in candidates.GroupBy(c => c.ClassId))
        {
            AssignCompetitionRanks(characterClass.OrderByDescending(c => c.Exp).ThenBy(c => c.CharacterId).ToList(), classRank);
        }

        return candidates
            .Select(c => new RankingEntry(c, totalRank[c.CharacterId], classRank[c.CharacterId], grade[c.CharacterId]))
            .ToList();
    }

    public static byte LowGrade(int exp) => LowBands.First(band => exp >= band.MinExp).Grade;

    private static void AssignCompetitionRanks(List<RankingCandidate> ordered, Dictionary<int, int> ranks)
    {
        int rank = 0;
        int? previousExp = null;
        for (int position = 1; position <= ordered.Count; position++)
        {
            var candidate = ordered[position - 1];
            if (candidate.Exp != previousExp)
            {
                rank = position;
                previousExp = candidate.Exp;
            }

            ranks[candidate.CharacterId] = rank;
        }
    }

    private static void AssignGrades(List<RankingCandidate> ordered, Dictionary<int, byte> grades)
    {
        var high = ordered.Where(c => c.Exp >= HighExp).ToList();
        foreach (var candidate in ordered.Where(c => c.Exp < HighExp))
        {
            grades[candidate.CharacterId] = LowGrade(candidate.Exp);
        }

        int total = high.Count;
        int rest = total - 21;
        int[] cuts =
        [
            (rest / 1000) + 1, rest / 100, rest * 3 / 100, rest * 6 / 100, rest / 10,
            rest * 17 / 100, rest * 27 / 100, rest * 40 / 100, rest * 56 / 100, rest * 76 / 100,
        ];

        byte current = 0;
        int left = 0;
        int assigned = 0;
        foreach (var candidate in high)
        {
            if (left == 0)
            {
                current++;
                left = current switch
                {
                    1 => 1,
                    2 => 4,
                    3 => 16,
                    >= 4 and <= 13 => NextBand(current),
                    _ => -1,
                };
            }

            grades[candidate.CharacterId] = current;
            left--;
        }

        int NextBand(byte band)
        {
            // Grade 4 takes 0.1 % + 1; each later grade fills up to its cumulative cut (legacy 0x4024AF..0x4025F1).
            int size = band == 4 ? cuts[0] : cuts[band - 4] - assigned + 1;
            assigned = band == 4 ? size : assigned + size;
            return size;
        }
    }
}
