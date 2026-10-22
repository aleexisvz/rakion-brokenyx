using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.Network.Tcp;
using Rakion.World.Features.Economy;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class ShopModuleTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private InventoryRepository _inventoryRepository = null!;
    private AccountBalanceRepository _balanceRepository = null!;
    private UserRepository _userRepository = null!;
    private RecordingSender _sender = null!;
    private WorldOpcodeRegistry _registry = null!;
    private int _userId;

    [TestInitialize]
    public async Task Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_shop_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _inventoryRepository = new InventoryRepository(_dbFactory);
        _balanceRepository = new AccountBalanceRepository(_dbFactory);
        _userRepository = new UserRepository(_dbFactory);
        _sender = new RecordingSender();
        _registry = new WorldOpcodeRegistry();
        new ShopModule(_sender, _inventoryRepository, _balanceRepository, _userRepository, NullLogger<ShopModule>.Instance).Register(_registry);

        _userId = await _userRepository.CreateUserAsync(new UserEntity { Username = "shopper", PasswordHash = "x", Email = "s@test" }, initialGold: 5000, initialCash: 600);
        await _inventoryRepository.UpsertItemTemplatesAsync(
        [
            new ItemEntity { Id = 1001, Shop = 1, GoldPrice = 2700 },
            new ItemEntity { Id = 12000, Shop = 2, CashPrice = 500 },
            new ItemEntity { Id = 9000, Shop = 0, GoldPrice = 1 },
            new ItemEntity { Id = 9100, Type = 10, Shop = 3, GoldPrice = 1 },
        ]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task Buy_WithGold_PutsItemInHighestFreeSlotOfTheBag()
    {
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = _userId, CharacterId = 0, ItemId = 1001, SlotIndex = 29 });

        var response = await BuyAsync(new InventoryBuyRequestPacket(1001, PayWithGold: true));

        Assert.AreEqual(InventoryBuyResponsePacket.ResultSuccess, response.Result);
        Assert.AreEqual(2300u, response.Gold);
        Assert.AreEqual(600u, response.Cash);
        Assert.AreEqual(new BoughtItem(28, 1001, 1), response.Items!.Single());
        Assert.HasCount(2, await _inventoryRepository.GetUserItemsAsync(_userId));
    }

    [TestMethod]
    public async Task Buy_WithCash_ChargesCash()
    {
        var response = await BuyAsync(new InventoryBuyRequestPacket(12000, PayWithGold: false));

        Assert.AreEqual(InventoryBuyResponsePacket.ResultSuccess, response.Result);
        Assert.AreEqual(100u, response.Cash);
        Assert.AreEqual(5000u, response.Gold);
    }

    [TestMethod]
    [DataRow((ushort)1001, false, DisplayName = "gold-only item paid with cash")]
    [DataRow((ushort)12000, true, DisplayName = "cash-only item paid with gold")]
    [DataRow((ushort)9000, true, DisplayName = "not sold")]
    [DataRow((ushort)9100, true, DisplayName = "package")]
    [DataRow((ushort)4242, true, DisplayName = "unknown item")]
    public async Task Buy_NotBuyable_ChangesNothing(ushort itemId, bool payWithGold)
    {
        var response = await BuyAsync(new InventoryBuyRequestPacket(itemId, payWithGold));

        Assert.AreEqual(InventoryBuyResponsePacket.ResultNotBuyable, response.Result);
        Assert.AreEqual(5000u, response.Gold);
        Assert.AreEqual(600u, response.Cash);
        Assert.IsEmpty(await _inventoryRepository.GetUserItemsAsync(_userId));
    }

    [TestMethod]
    public async Task Buy_NotEnoughGold_ReportsNotBuyable()
    {
        await _balanceRepository.TryDeductGoldAsync(_userId, 4000);

        var response = await BuyAsync(new InventoryBuyRequestPacket(1001, PayWithGold: true));

        Assert.AreEqual(InventoryBuyResponsePacket.ResultNotBuyable, response.Result);
        Assert.AreEqual(1000u, response.Gold);
    }

    [TestMethod]
    public async Task Buy_FullBag_ReportsBoxFull()
    {
        for (byte slot = 0; slot < 30; slot++)
        {
            await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = _userId, CharacterId = 0, ItemId = 1001, SlotIndex = slot });
        }

        var response = await BuyAsync(new InventoryBuyRequestPacket(1001, PayWithGold: true));

        Assert.AreEqual(InventoryBuyResponsePacket.ResultBoxFull, response.Result);
        Assert.AreEqual(5000u, response.Gold);
    }

    [TestMethod]
    public async Task Buy_WithCoupon_IsRejected()
    {
        var response = await BuyAsync(new InventoryBuyRequestPacket(1001, PayWithGold: true, UsesCoupon: 1, CouponSlot: 3));

        Assert.AreEqual(InventoryBuyResponsePacket.ResultCouponInvalid, response.Result);
    }

    private async Task<InventoryBuyResponsePacket> BuyAsync(InventoryBuyRequestPacket request)
    {
        var session = new WorldSession(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = _userId, Username = "shopper" },
            ActiveCharacter = new CharacterEntity { Id = 9, UserId = _userId, Name = "Buyer" },
        };

        using var writer = new PacketWriter(request.Opcode);
        request.Serialize(writer);
        byte[] payload = writer.Build()[PacketHeader.HeaderSize..];

        Assert.IsTrue(_registry.TryGetHandler((ushort)ClientOpcode.InventoryBuy, out var handler));
        await handler(session, payload, CancellationToken.None);
        return (InventoryBuyResponsePacket)_sender.Sent.Last().Packet;
    }

    private sealed class RecordingSender : ISessionSender
    {
        public List<(IWorldSessionView Session, IPacket Packet)> Sent { get; } = [];

        public ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken)
        {
            Sent.Add((session, packet));
            return ValueTask.CompletedTask;
        }
    }
}
