using Dapper;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class AdminRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private AdminRepository _repository = null!;
    private int _userId;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new SqliteTestConnectionFactory($"AdminDb_{Guid.NewGuid():N}");
        _repository = new AdminRepository(_factory);
        _userId = await new UserRepository(_factory).CreateUserAsync(new UserEntity { Username = "target", PasswordHash = "x", Email = "t@test" });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task BanAndUnban_UpdateTheGameInfo()
    {
        var until = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.IsTrue(await _repository.BanAsync(_userId, until, "spam"));
        var banned = await new UserRepository(_factory).GetGameInfoByUserIdAsync(_userId);
        Assert.IsFalse(banned!.IsBanned);
        Assert.AreEqual(until.Date, banned.BanDate!.Value.Date, "SQLite may read the date back in local time");

        Assert.IsTrue(await _repository.BanAsync(_userId, null, "cheat"));
        Assert.IsTrue((await new UserRepository(_factory).GetGameInfoByUserIdAsync(_userId))!.IsBanned);

        Assert.IsTrue(await _repository.UnbanAsync(_userId));
        var free = await new UserRepository(_factory).GetGameInfoByUserIdAsync(_userId);
        Assert.IsFalse(free!.IsBanned);
        Assert.IsNull(free.BanDate);
    }

    [TestMethod]
    public async Task GiveItem_UsesTheLowestFreeSlot_AndRefusesUnknownItemsOrAFullBox()
    {
        await using (var connection = await _factory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO item_templates (id) VALUES (1001);");
            await connection.ExecuteAsync("INSERT INTO user_items (user_id, character_id, item_id, slot_index) VALUES (@UserId, 0, 1001, 0);", new { UserId = _userId });
        }

        Assert.AreEqual((byte)1, await _repository.GiveItemAsync(_userId, 1001, boxSlots: 2));
        Assert.IsNull(await _repository.GiveItemAsync(_userId, 1001, boxSlots: 2), "box full");
        Assert.IsNull(await _repository.GiveItemAsync(_userId, 4242, boxSlots: 30), "unknown item");
    }
}
