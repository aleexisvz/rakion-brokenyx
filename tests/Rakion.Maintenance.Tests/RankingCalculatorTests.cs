using Rakion.Database.Entities;
using Rakion.Maintenance.Ranking;

namespace Rakion.Maintenance.Tests;

[TestClass]
public sealed class RankingCalculatorTests
{
    [TestMethod]
    public void TiesShareTheFirstRank_PerCountryAndPerClass()
    {
        var entries = RankingCalculator.Compute(
        [
            Candidate(1, exp: 100, country: 9, classId: 0),
            Candidate(2, exp: 100, country: 9, classId: 1),
            Candidate(3, exp: 90, country: 9, classId: 0),
            Candidate(4, exp: 95, country: 1, classId: 0),
        ]);

        var byId = entries.ToDictionary(e => e.Candidate.CharacterId);
        Assert.AreEqual((1, 1, 3), (byId[1].TotalRank, byId[2].TotalRank, byId[3].TotalRank));
        Assert.AreEqual(1, byId[4].TotalRank, "own country");
        Assert.AreEqual((1, 2, 3), (byId[1].ClassRank, byId[4].ClassRank, byId[3].ClassRank), "class rank across countries");
        Assert.AreEqual(1, byId[2].ClassRank);
    }

    [TestMethod]
    public void LowExperienceUsesTheFixedBands()
    {
        Assert.AreEqual((byte)26, RankingCalculator.LowGrade(0));
        Assert.AreEqual((byte)25, RankingCalculator.LowGrade(1_000));
        Assert.AreEqual((byte)20, RankingCalculator.LowGrade(12_999));
        Assert.AreEqual((byte)15, RankingCalculator.LowGrade(31_999));
    }

    [TestMethod]
    public void HighExperienceFillsTheLegacyBandsInOrder()
    {
        // 1121 players over 32 000: grades 1..3 take 21, then 0.1 % of 1100 (+1) = 2, then the cumulative cuts
        // 11, 33, 66, 110, 187, 297, 440, 616 and 836 (each band adds cut - assigned + 1), and grade 14 the rest.
        var candidates = Enumerable.Range(1, 1121).Select(i => Candidate(i, exp: 100_000 - i, country: 9, classId: 0)).ToList();

        var grades = RankingCalculator.Compute(candidates).GroupBy(e => e.Grade).ToDictionary(g => g.Key, g => g.Count());

        int[] expected = [1, 4, 16, 2, 10, 22, 33, 44, 77, 110, 143, 176, 220, 263];
        for (byte grade = 1; grade <= 14; grade++)
        {
            Assert.AreEqual(expected[grade - 1], grades[grade], $"grade {grade}");
        }
    }

    private static RankingCandidate Candidate(int id, int exp, int country, byte classId) =>
        new(id, $"user{id}", $"Char{id}", 1, classId, exp, 0, 0, 0, 0, 0, country);
}
