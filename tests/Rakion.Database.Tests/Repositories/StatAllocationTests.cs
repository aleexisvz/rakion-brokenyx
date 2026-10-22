using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class StatAllocationTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private CharacterRepository _charRepo = null!;
    private UserRepository _userRepo = null!;
    private int _userId;
    private int _characterId;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new SqliteTestConnectionFactory($"StatDb_{Guid.NewGuid():N}");
        _charRepo = new CharacterRepository(_factory);
        _userRepo = new UserRepository(_factory);
        _userId = await _userRepo.CreateUserAsync(new UserEntity { Username = "statuser", PasswordHash = "x", Email = "s@test" });
        _characterId = (await _charRepo.CreateCharacterAsync(_userId, "Statty", CharacterClass.Swordman, 0)).Id;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Allocate_SpendsLevelPointFirst()
    {
        await SetAsync("UPDATE characters SET level_point = 2 WHERE id = @Id", new { Id = _characterId });
        await SetAsync("UPDATE user_game_info SET power_level_point = 1 WHERE user_id = @Id", new { Id = _userId });

        var outcome = await _charRepo.AllocateStatPointAsync(_userId, _characterId, CharacterStat.MaxCp, 50);

        Assert.AreEqual(new StatAllocationOutcome(StatAllocationStatus.Allocated, 1, 1, 1), outcome);
        var character = await _charRepo.GetCharacterByIdAsync(_characterId);
        Assert.AreEqual((byte)1, character!.MaxCp);
        Assert.AreEqual((byte)1, character.LevelPoint);
    }

    [TestMethod]
    public async Task Allocate_FallsBackToPowerUserPoints()
    {
        await SetAsync("UPDATE user_game_info SET power_level_point = 1 WHERE user_id = @Id", new { Id = _userId });

        var outcome = await _charRepo.AllocateStatPointAsync(_userId, _characterId, CharacterStat.Hit1, 50);

        Assert.AreEqual(new StatAllocationOutcome(StatAllocationStatus.Allocated, 0, 0, 1), outcome);
        Assert.AreEqual(0, (await _userRepo.GetGameInfoByUserIdAsync(_userId))!.PowerLevelPoint);
    }

    [TestMethod]
    public async Task Allocate_WithoutPointsOrAtMaximum_ChangesNothing()
    {
        var noPoints = await _charRepo.AllocateStatPointAsync(_userId, _characterId, CharacterStat.Hp, 50);
        await SetAsync("UPDATE characters SET level_point = 3, hp = 50 WHERE id = @Id", new { Id = _characterId });
        var atMaximum = await _charRepo.AllocateStatPointAsync(_userId, _characterId, CharacterStat.Hp, 50);
        var otherUser = await _charRepo.AllocateStatPointAsync(_userId + 1, _characterId, CharacterStat.Hp, 50);

        Assert.AreEqual(StatAllocationStatus.NoPoints, noPoints.Status);
        Assert.AreEqual(new StatAllocationOutcome(StatAllocationStatus.AtMaximum, 3, 0, 50), atMaximum);
        Assert.AreEqual(StatAllocationStatus.CharacterNotFound, otherUser.Status);
        Assert.AreEqual((byte)3, (await _charRepo.GetCharacterByIdAsync(_characterId))!.LevelPoint);
    }

    private async Task SetAsync(string sql, object args)
    {
        await using var connection = await _factory.CreateConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }
}
