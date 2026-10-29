using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class AbuserRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private AbuserRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new SqliteTestConnectionFactory($"AbuserDb_{Guid.NewGuid():N}");
        _repository = new AbuserRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Check_FlagsCharactersReachingAnyThreshold_OverTheCheckedDays()
    {
        // Hero: 2 rounds, 7000 exp in total -> over the exp threshold. Calm: little of everything. Winner: 3 wins.
        await LogAsync(1, "Hero", win: true, exp: 4000, gold: 100);
        await LogAsync(1, "Hero", win: false, exp: 3000, gold: 100);
        await LogAsync(2, "Calm", win: true, exp: 100, gold: 100);
        await LogAsync(3, "Winner", win: true, exp: 10, gold: 10);
        await LogAsync(3, "Winner", win: true, exp: 10, gold: 10);
        await LogAsync(3, "Winner", win: true, exp: 10, gold: 10);

        var flags = await _repository.RunCheckAsync(DateTime.Now.AddDays(-1), winSum: 3, expSum: 6000, goldSum: 12000);

        CollectionAssert.AreEquivalent(new[] { "Hero", "Winner" }, flags.Select(f => f.CharacterName).ToArray());
        var hero = flags.Single(f => f.CharacterName == "Hero");
        Assert.AreEqual((1L, 1L, 7000L, 200L), (hero.Wins, hero.Losses, hero.Exp, hero.Gold));

        Assert.IsEmpty(await _repository.RunCheckAsync(DateTime.Now.AddMinutes(1), 3, 6000, 12000), "nothing logged since then");
        Assert.HasCount(2, await _repository.GetRecentFlagsAsync(10));
    }

    private Task LogAsync(int characterId, string name, bool win, long exp, long gold) =>
        _repository.LogMatchPointAsync(new MatchPointLogEntry(99, "Host", characterId, name, false, 2, 3, 1, win, !win, exp, gold));
}
