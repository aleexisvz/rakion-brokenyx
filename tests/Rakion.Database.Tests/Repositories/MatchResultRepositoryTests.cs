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
}
