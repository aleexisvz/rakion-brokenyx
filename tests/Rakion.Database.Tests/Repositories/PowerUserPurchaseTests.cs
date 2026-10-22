using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class PowerUserPurchaseTests
{
    private const long Now = 1_000_000;
    private const long Month = 30 * 1440;
    private const long MaxAhead = 180 * 1440;

    private SqliteTestConnectionFactory _factory = null!;
    private AccountBalanceRepository _balanceRepo = null!;
    private int _userId;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new SqliteTestConnectionFactory($"PowerDb_{Guid.NewGuid():N}");
        _balanceRepo = new AccountBalanceRepository(_factory);
        _userId = await new UserRepository(_factory).CreateUserAsync(
            new UserEntity { Username = "power", PasswordHash = "x", Email = "p@test" }, initialGold: 100, initialCash: 10000);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task NewCard_StartsFromNowAndAddsPoints()
    {
        var outcome = await BuyAsync(price: 8000, extend: false);

        Assert.AreEqual(new PowerUserPurchaseOutcome(PowerUserPurchaseStatus.Bought, 100, 2000, Now + Month, 5), outcome);
        Assert.AreEqual(2000, await _balanceRepo.GetCashAsync(_userId));
    }

    [TestMethod]
    public async Task Extend_AddsToCurrentExpiry_OrStartsFromNowWhenExpired()
    {
        await SetPowerTimeAsync(Now + 100);
        var active = await BuyAsync(price: 6000, extend: true);
        await SetPowerTimeAsync(Now - 100);
        var expired = await BuyAsync(price: 1000, extend: true);

        Assert.AreEqual(Now + 100 + Month, active.PowerTime);
        Assert.AreEqual(Now + Month, expired.PowerTime);
        Assert.AreEqual(10, expired.PowerLevelPoints);
    }

    [TestMethod]
    public async Task NotEnoughCashOrBeyondLimit_ChangesNothing()
    {
        var poor = await BuyAsync(price: 20000, extend: false);
        await SetPowerTimeAsync(Now + MaxAhead + 1);
        var tooFar = await BuyAsync(price: 6000, extend: true);

        Assert.AreEqual(PowerUserPurchaseStatus.NotEnoughCash, poor.Status);
        Assert.AreEqual(PowerUserPurchaseStatus.LimitReached, tooFar.Status);
        Assert.AreEqual(10000, await _balanceRepo.GetCashAsync(_userId));
        Assert.AreEqual(0, tooFar.PowerLevelPoints);
    }

    private Task<PowerUserPurchaseOutcome> BuyAsync(int price, bool extend) =>
        _balanceRepo.BuyPowerUserAsync(_userId, price, extend, Now, Month, MaxAhead, pointsGained: 5);

    private async Task SetPowerTimeAsync(long value)
    {
        await using var connection = await _factory.CreateConnectionAsync();
        await connection.ExecuteAsync("UPDATE user_game_info SET power_time = @Value WHERE user_id = @Id", new { Value = value, Id = _userId });
    }
}
