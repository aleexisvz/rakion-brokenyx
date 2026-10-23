using Dapper;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Import;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class MatchResultRepositoryTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private MatchResultRepository _repository = null!;
    private int _userId;
    private int _characterId;

    [TestInitialize]
    public async Task Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"match_result_test_{Guid.NewGuid():N}");
        _repository = new MatchResultRepository(_dbFactory);
        _userId = await new UserRepository(_dbFactory).CreateUserAsync(new UserEntity { Username = "fighter", PasswordHash = "x", Email = "f@test" }, initialGold: 100);
        _characterId = (await new CharacterRepository(_dbFactory).CreateCharacterAsync(_userId, "Fighter", CharacterClass.Swordman, 0)).Id;

        await _repository.UpsertClassLevelsAsync(
        [
            new ClassLevelEntity(0, 1, 70),
            new ClassLevelEntity(0, 2, 200),
            new ClassLevelEntity(0, 3, 400),
        ]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task ApplyRoundReward_AddsExpAndGold_WithoutLevelUpBelowThreshold()
    {
        var outcome = await _repository.ApplyRoundRewardAsync(_userId, _characterId, exp: 70, gold: 25);

        Assert.AreEqual(new RoundRewardOutcome(1, 70, 0, false, 125), outcome);
    }

    [TestMethod]
    public async Task ApplyRoundReward_LevelsUpSeveralTimes_WithThreePointsEach()
    {
        await _repository.ApplyRoundRewardAsync(_userId, _characterId, exp: 50, gold: 0);
        var outcome = await _repository.ApplyRoundRewardAsync(_userId, _characterId, exp: 250, gold: 0);

        Assert.AreEqual((byte)3, outcome!.Level);
        Assert.AreEqual(300, outcome.Exp);
        Assert.AreEqual(6, outcome.LevelPoint);
        Assert.IsTrue(outcome.LeveledUp);
        var stored = await new CharacterRepository(_dbFactory).GetCharacterByIdAsync(_characterId);
        Assert.AreEqual((byte)3, stored!.Level);
    }

    [TestMethod]
    public async Task ApplyRoundReward_RejectsAnotherUsersCharacter()
    {
        Assert.IsNull(await _repository.ApplyRoundRewardAsync(_userId + 1, _characterId, 10, 10));
    }

    [TestMethod]
    public void AddExperience_CapsAtLevel99()
    {
        var thresholds = new Dictionary<byte, int> { [99] = 1000 };

        var outcome = MatchResultRepository.AddExperience(99, 990, 0, 500, thresholds);

        Assert.AreEqual((byte)99, outcome.Level);
        Assert.AreEqual(1000, outcome.Exp);
    }

    [TestMethod]
    public void LegacyClassLevelParser_ReadsDumpRows()
    {
        const string dump = """
            INSERT INTO `classlevelinfo` VALUES ('0', '0', '0');
            INSERT INTO `classlevelinfo` VALUES ('3', '1', '70');
            INSERT INTO `iteminfo` VALUES ('1', '2');
            """;

        var rows = LegacyClassLevelParser.Parse(new StringReader(dump));

        CollectionAssert.AreEqual(new[] { new ClassLevelEntity(0, 0, 0), new ClassLevelEntity(3, 1, 70) }, rows.ToArray());
    }

    [TestMethod]
    public async Task ApplyRoundReward_CountsTheRoundResult()
    {
        await _repository.ApplyRoundRewardAsync(_userId, _characterId, 0, 0, RoundResult.Win);
        await _repository.ApplyRoundRewardAsync(_userId, _characterId, 0, 0, RoundResult.Draw);
        var outcome = await _repository.ApplyRoundRewardAsync(_userId, _characterId, 0, 0, RoundResult.Win);

        Assert.AreEqual((2, 0, 1), (outcome!.Wins, outcome.Losses, outcome.Draws));
        var stored = await new CharacterRepository(_dbFactory).GetCharacterByIdAsync(_characterId);
        Assert.AreEqual(2, stored!.WinCount);
        Assert.AreEqual(1, stored.DrawCount);
    }

    [TestMethod]
    public async Task ApplyRoundReward_AddsCappedCreatureExp_AndLevelsCreaturesUp()
    {
        await _repository.UpsertCreatureLevelsAsync(
        [
            new CreatureLevelEntity(15, 1, 0),
            new CreatureLevelEntity(15, 2, 35),
            new CreatureLevelEntity(15, 3, 105),
        ]);
        await using (var connection = await _dbFactory.CreateConnectionAsync())
        {
            const string insert = "INSERT INTO user_items (user_id, character_id, item_id, level, slot_index, exp) VALUES (@UserId, @CharacterId, @ItemId, @Level, @Slot, @Exp);";
            await connection.ExecuteAsync(insert, new { UserId = _userId, CharacterId = _characterId, ItemId = 8015, Level = 2, Slot = 10, Exp = 30 });
            await connection.ExecuteAsync(insert, new { UserId = _userId, CharacterId = _characterId, ItemId = 8015, Level = 2, Slot = 12, Exp = 0 });
            await connection.ExecuteAsync(insert, new { UserId = _userId, CharacterId = 0, ItemId = 8015, Level = 1, Slot = 11, Exp = 0 });
        }

        var outcome = await _repository.ApplyRoundRewardAsync(_userId, _characterId, 0, 0, creatureExp: [500, 7, 4]);

        Assert.AreEqual((byte)4, outcome!.Creature1Level, "130 exp passes 35 and 105, then no row for level 4");
        Assert.AreEqual((byte)0, outcome.Creature2Level, "slot 11 holds no equipped creature");
        Assert.AreEqual((byte)2, outcome.Creature3Level);
        Assert.IsTrue(outcome.CreatureLeveledUp);
        await using var check = await _dbFactory.CreateConnectionAsync();
        var exps = (await check.QueryAsync<long>("SELECT exp FROM user_items WHERE user_id = @UserId ORDER BY slot_index;", new { UserId = _userId })).ToList();
        CollectionAssert.AreEqual(new long[] { 130, 0, 4 }, exps);
    }

    [TestMethod]
    public void LegacyCreatureLevelParser_ReadsDumpRows_UpToLevel99()
    {
        const string dump = """
            INSERT INTO `npcinfo` VALUES ('0', '2', '35', '1');
            INSERT INTO `npcinfo` VALUES ('0', '120', '999', '1');
            """;

        var rows = LegacyCreatureLevelParser.Parse(new StringReader(dump));

        CollectionAssert.AreEqual(new[] { new CreatureLevelEntity(0, 2, 35) }, rows.ToArray());
    }

    [TestMethod]
    public async Task ApplyStageClear_PaysOnlyForABetterRank_AndStoresDrops()
    {
        await using (var connection = await _dbFactory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO item_templates (id) VALUES (8001);");
            await connection.ExecuteAsync("INSERT INTO user_items (user_id, character_id, item_id, slot_index) VALUES (@UserId, 0, 8001, 0);", new { UserId = _userId });
        }

        var first = await _repository.ApplyStageClearAsync(_userId, _characterId, stage: 3, rank: 2, drops: [8001, 9999], exp: 10, gold: 5);
        var same = await _repository.ApplyStageClearAsync(_userId, _characterId, stage: 3, rank: 2, drops: [8001], exp: 10, gold: 5);
        var better = await _repository.ApplyStageClearAsync(_userId, _characterId, stage: 3, rank: 4, drops: [], exp: 10, gold: 5);

        Assert.IsTrue(first!.Improved);
        Assert.AreEqual(0, first.PreviousRank);
        CollectionAssert.AreEqual(new[] { ((byte)1, (ushort)8001) }, first.Drops.ToArray(), "unknown item skipped, slot 0 taken");
        Assert.AreEqual(1, first.Reward!.Draws);
        Assert.AreEqual(105L, first.Reward.Gold);
        Assert.IsFalse(same!.Improved);
        Assert.IsNull(same.Reward);
        Assert.IsTrue(better!.Improved);
        Assert.AreEqual(2, better.PreviousRank);
        Assert.AreEqual(110L, better.Reward!.Gold, "the repeated rank paid nothing");

        var ranks = await _repository.GetStageRanksAsync([_characterId, 999]);
        Assert.AreEqual((byte)4, ranks[_characterId][3]);
        Assert.HasCount(100, ranks[999]);
        Assert.IsNull(await _repository.ApplyStageClearAsync(_userId + 1, _characterId, 1, 1, [], 0, 0));
    }
}
