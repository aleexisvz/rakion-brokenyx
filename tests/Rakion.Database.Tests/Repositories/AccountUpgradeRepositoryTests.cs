using Dapper;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class AccountUpgradeRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private AccountUpgradeRepository _repository = null!;
    private int _userId;
    private int _characterId;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new SqliteTestConnectionFactory($"UpgradeDb_{Guid.NewGuid():N}");
        _repository = new AccountUpgradeRepository(_factory);
        _userId = await new UserRepository(_factory).CreateUserAsync(new UserEntity { Username = "buyer", PasswordHash = "x", Email = "b@test" }, initialGold: 200000);
        _characterId = (await new CharacterRepository(_factory).CreateCharacterAsync(_userId, "Buyer", CharacterClass.Swordman, 0)).Id;
        await ExecuteAsync("UPDATE cash_balances SET cash_amount = 20000 WHERE user_id = @Id;", new { Id = _userId });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task ResetStats_KeepsThreePointsPerLevel_AndReturnsTheRestAsPowerPoints()
    {
        await ExecuteAsync("UPDATE characters SET level = 5, level_point = 2, hit1 = 10, hp = 4 WHERE id = @Id;", new { Id = _characterId });
        await ExecuteAsync("UPDATE user_game_info SET power_level_point = 1 WHERE user_id = @Id;", new { Id = _userId });

        var outcome = await _repository.ResetStatsAsync(_userId, _characterId, 7000);

        Assert.AreEqual(new StatResetOutcome(StatResetStatus.Reset, 13000, 12, 5), outcome);
        Assert.AreEqual(0, await ScalarAsync("SELECT hit1 + hp FROM characters WHERE id = @Id;", new { Id = _characterId }));
        Assert.AreEqual(StatResetStatus.NothingToReset, (await _repository.ResetStatsAsync(_userId, _characterId, 7000)).Status);
    }

    [TestMethod]
    public async Task ResetStats_RefusesWithoutEnoughCash()
    {
        await ExecuteAsync("UPDATE characters SET hit1 = 1 WHERE id = @Id;", new { Id = _characterId });

        var outcome = await _repository.ResetStatsAsync(_userId, _characterId, 30000);

        Assert.AreEqual(StatResetStatus.NotEnoughCash, outcome.Status);
        Assert.AreEqual(1, await ScalarAsync("SELECT hit1 FROM characters WHERE id = @Id;", new { Id = _characterId }));
    }

    [TestMethod]
    public async Task BuyBag_ChargesCash_UpToTheMaximum()
    {
        var first = await _repository.BuyBagAsync(_userId, 8000, maxBags: 2);
        var second = await _repository.BuyBagAsync(_userId, 8000, maxBags: 2);

        Assert.AreEqual(new UpgradeOutcome(UpgradeStatus.Bought, 200000, 12000, 2), first);
        Assert.AreEqual(UpgradeStatus.AtMaximum, second.Status);
        Assert.AreEqual(12000, second.Cash);
    }

    [TestMethod]
    public async Task BuyPotionSlot_UsesThePriceForTheCurrentCount()
    {
        (bool, int)? Price(byte slots) => slots switch
        {
            3 => (false, 8000),
            4 => (true, 100000),
            5 => (false, 31000),
            _ => null,
        };

        var first = await _repository.BuyPotionSlotAsync(_userId, _characterId, Price);
        var second = await _repository.BuyPotionSlotAsync(_userId, _characterId, Price);
        var third = await _repository.BuyPotionSlotAsync(_userId, _characterId, Price);

        Assert.AreEqual(new UpgradeOutcome(UpgradeStatus.Bought, 200000, 12000, 4), first);
        Assert.AreEqual(new UpgradeOutcome(UpgradeStatus.Bought, 100000, 12000, 5), second);
        Assert.AreEqual(UpgradeStatus.NotEnoughMoney, third.Status);
        Assert.AreEqual(UpgradeStatus.CharacterNotFound, (await _repository.BuyPotionSlotAsync(_userId + 1, _characterId, Price)).Status);
    }

    private async Task ExecuteAsync(string sql, object args)
    {
        await using var connection = await _factory.CreateConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    private async Task<int> ScalarAsync(string sql, object args)
    {
        await using var connection = await _factory.CreateConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(sql, args);
    }
}
