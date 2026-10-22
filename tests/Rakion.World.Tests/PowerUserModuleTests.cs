using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Core.Utilities;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.Network.Tcp;
using Rakion.World.Features.Economy;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class PowerUserModuleTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0);

    private SqliteTestConnectionFactory _dbFactory = null!;
    private RecordingSender _sender = null!;
    private WorldOpcodeRegistry _registry = null!;
    private int _userId;

    [TestInitialize]
    public async Task Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_power_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _sender = new RecordingSender();
        _registry = new WorldOpcodeRegistry();
        new PowerUserModule(_sender, new AccountBalanceRepository(_dbFactory), new FixedTimeProvider(Now), NullLogger<PowerUserModule>.Instance)
            .Register(_registry);
        _userId = await new UserRepository(_dbFactory).CreateUserAsync(
            new UserEntity { Username = "pucard", PasswordHash = "x", Email = "pu@test" }, initialGold: 100, initialCash: 9000);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task NewCard_Costs8000CashAndLasts30Days()
    {
        var response = await BuyAsync([0, 0]);

        Assert.AreEqual(InventoryBuyPowerUserResponsePacket.ResultSuccess, response.Result);
        Assert.AreEqual(1000u, response.Cash);
        Assert.AreEqual(100u, response.Gold);
        Assert.AreEqual(LegacyGameClock.ToMinutes(Now) + 30u * 1440u, response.PowerUserExpiry);
        Assert.AreEqual((ushort)5, response.PowerUserPoints);
    }

    [TestMethod]
    public async Task SecondNewCard_WithoutCash_ReportsNotEnoughCash()
    {
        await BuyAsync([0, 0]);
        var second = await BuyAsync([0, 0]);

        Assert.AreEqual(InventoryBuyPowerUserResponsePacket.ResultNotEnoughCash, second.Result);
        Assert.AreEqual(1000u, second.Cash);
    }

    [TestMethod]
    public async Task Coupon_IsRejected_AndInvalidOptionIsIgnored()
    {
        var coupon = await BuyAsync([1, 1, 3, 0]);
        Assert.IsTrue(_registry.TryGetHandler((ushort)ClientOpcode.InventoryBuyPowerUser, out var handler));
        await handler(CreateSession(), new byte[] { 2, 0 }, CancellationToken.None);

        Assert.AreEqual(InventoryBuyPowerUserResponsePacket.ResultCouponInvalid, coupon.Result);
        Assert.HasCount(1, _sender.Sent);
    }

    private async Task<InventoryBuyPowerUserResponsePacket> BuyAsync(byte[] payload)
    {
        Assert.IsTrue(_registry.TryGetHandler((ushort)ClientOpcode.InventoryBuyPowerUser, out var handler));
        await handler(CreateSession(), payload, CancellationToken.None);
        return (InventoryBuyPowerUserResponsePacket)_sender.Sent.Last().Packet;
    }

    private WorldSession CreateSession() =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = _userId, Username = "pucard" },
            ActiveCharacter = new CharacterEntity { Id = 9, UserId = _userId, Name = "Powered" },
        };

    private sealed class RecordingSender : ISessionSender
    {
        public List<(IWorldSessionView Session, IPacket Packet)> Sent { get; } = [];

        public ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken)
        {
            Sent.Add((session, packet));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
