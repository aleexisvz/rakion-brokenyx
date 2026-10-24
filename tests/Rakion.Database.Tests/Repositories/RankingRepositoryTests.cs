using Dapper;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class RankingRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private RankingRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new SqliteTestConnectionFactory($"RankingDb_{Guid.NewGuid():N}");
        _repository = new RankingRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Candidates_AreActiveNonExcludedCharacters_AndSaveStoresRanksAndSnapshots()
    {
        int active = await CreateCharacterAsync("active", "Active", exp: 500, lastConnect: new DateTime(2026, 10, 1), auth: 0);
        await CreateCharacterAsync("idle", "Idle", exp: 900, lastConnect: new DateTime(2026, 1, 1), auth: 0);
        await CreateCharacterAsync("staff", "Staff", exp: 999, lastConnect: new DateTime(2026, 10, 1), auth: 0x34);

        var candidates = await _repository.GetCandidatesAsync(new DateTime(2026, 8, 1), [2, 10, 0x34]);

        Assert.HasCount(1, candidates);
        Assert.AreEqual(active, candidates[0].CharacterId);
        Assert.AreEqual("active", candidates[0].Username);

        await _repository.SaveAsync([new RankingEntry(candidates[0], TotalRank: 1, ClassRank: 1, Grade: 26)]);
        await _repository.SaveAsync([new RankingEntry(candidates[0], TotalRank: 2, ClassRank: 3, Grade: 25)]);

        var stored = await new CharacterRepository(_factory).GetCharacterByIdAsync(active);
        Assert.AreEqual((25, 2, 3), (stored!.RankGrade, stored.TotalRank, stored.ClassRank));
        await using var connection = await _factory.CreateConnectionAsync();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ranking_total;"), "the snapshot is replaced, not appended");
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>("SELECT `rank` FROM ranking_class WHERE character_id = @Id;", new { Id = active }));
    }

    private async Task<int> CreateCharacterAsync(string username, string name, int exp, DateTime lastConnect, int auth)
    {
        int userId = await new UserRepository(_factory).CreateUserAsync(new UserEntity { Username = username, PasswordHash = "x", Email = $"{username}@test" });
        int characterId = (await new CharacterRepository(_factory).CreateCharacterAsync(userId, name, CharacterClass.Swordman, 0)).Id;
        await using var connection = await _factory.CreateConnectionAsync();
        await connection.ExecuteAsync("UPDATE characters SET exp = @Exp, auth = @Auth WHERE id = @Id;", new { Exp = exp, Auth = auth, Id = characterId });
        await connection.ExecuteAsync("UPDATE user_game_info SET last_connect = @LastConnect WHERE user_id = @Id;", new { LastConnect = lastConnect, Id = userId });
        return characterId;
    }
}
