using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Import;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class ShopRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private InventoryRepository _invRepo = null!;
    private UserRepository _userRepo = null!;
    private AccountBalanceRepository _balanceRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new SqliteTestConnectionFactory($"ShopDb_{Guid.NewGuid():N}");
        _invRepo = new InventoryRepository(_factory);
        _userRepo = new UserRepository(_factory);
        _balanceRepo = new AccountBalanceRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public void Parser_ReadsLegacyIteminfoRowsAndSkipsTheRest()
    {
        const string dump = """
            CREATE TABLE `iteminfo` (`id` int(11) NOT NULL DEFAULT '0');
            INSERT INTO `iteminfo` VALUES ('1001', '0', '1', '2', '1', '2700', '0', '0', '0', '0', '0', '1', '0', '6', '100', '0');
            INSERT INTO `useriteminfo` VALUES ('1', '1', '1001', '1', '0', '0', '0');
            INSERT INTO `iteminfo` VALUES ('12000', '12', '31', '0', '2', '0', '500', '0', '0', '0', '0', '0', '0', '0', '0', '1');
            INSERT INTO `iteminfo` VALUES ('bad', '0');
            """;

        var items = LegacyItemCatalogParser.Parse(new StringReader(dump));

        Assert.HasCount(2, items);
        Assert.AreEqual(1001, items[0].Id);
        Assert.AreEqual((byte)1, items[0].Shop);
        Assert.AreEqual(2700, items[0].GoldPrice);
        Assert.AreEqual((byte)2, items[0].RequiredLevel);
        Assert.AreEqual(100, items[0].MaxCp);
        Assert.AreEqual((CharacterClass)31, items[1].Class);
        Assert.AreEqual(500, items[1].CashPrice);
        Assert.AreEqual(1, items[1].Power);
    }

    [TestMethod]
    public async Task UpsertItemTemplates_InsertsThenUpdates()
    {
        await _invRepo.UpsertItemTemplatesAsync([new ItemEntity { Id = 1001, Shop = 1, GoldPrice = 2700 }]);
        await _invRepo.UpsertItemTemplatesAsync([new ItemEntity { Id = 1001, Shop = 3, GoldPrice = 3000 }]);

        var template = await _invRepo.GetItemTemplateAsync(1001);

        Assert.IsNotNull(template);
        Assert.AreEqual((byte)3, template.Shop);
        Assert.AreEqual(3000, template.GoldPrice);
    }

    [TestMethod]
    public async Task Purchase_WithGold_ChargesAndPlacesItem()
    {
        int userId = await CreateUserAsync(gold: 5000, cash: 100);

        var outcome = await _invRepo.PurchaseIntoBoxAsync(userId, 1001, payWithGold: true, price: 2700, slot: 29, level: 1);

        Assert.IsNotNull(outcome);
        Assert.AreEqual(2300L, outcome.Gold);
        Assert.AreEqual(100, outcome.Cash);
        var item = (await _invRepo.GetUserItemsAsync(userId)).Single();
        Assert.AreEqual((0, (byte)29, 1001), (item.CharacterId, item.SlotIndex, item.ItemId));
        Assert.AreEqual(outcome.UserItemId, item.Id);
    }

    [TestMethod]
    public async Task Purchase_WithCash_ChargesCash()
    {
        int userId = await CreateUserAsync(gold: 0, cash: 800);

        var outcome = await _invRepo.PurchaseIntoBoxAsync(userId, 12000, payWithGold: false, price: 500, slot: 0, level: 1);

        Assert.IsNotNull(outcome);
        Assert.AreEqual(300, outcome.Cash);
        Assert.AreEqual(300, await _balanceRepo.GetCashAsync(userId));
    }

    [TestMethod]
    public async Task Purchase_TooExpensiveOrSlotTaken_ChangesNothing()
    {
        int userId = await CreateUserAsync(gold: 1000, cash: 0);
        await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 1002, SlotIndex = 5 });

        Assert.IsNull(await _invRepo.PurchaseIntoBoxAsync(userId, 1001, true, 2700, 4, 1));
        Assert.IsNull(await _invRepo.PurchaseIntoBoxAsync(userId, 1001, true, 100, 5, 1));

        Assert.AreEqual(1000L, await _balanceRepo.GetGoldAsync(userId));
        Assert.HasCount(1, await _invRepo.GetUserItemsAsync(userId));
    }

    [TestMethod]
    public async Task SellFromBox_RemovesItemAndCreditsGold()
    {
        int userId = await CreateUserAsync(gold: 1000, cash: 50);
        var item = await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 1001, SlotIndex = 4 });

        var outcome = await _invRepo.SellFromBoxAsync(userId, 4, 1001, 1080);

        Assert.IsNotNull(outcome);
        Assert.AreEqual(item.Id, outcome.UserItemId);
        Assert.AreEqual(2080L, outcome.Gold);
        Assert.AreEqual(50, outcome.Cash);
        Assert.IsEmpty(await _invRepo.GetUserItemsAsync(userId));
    }

    [TestMethod]
    public async Task SellFromBox_WrongItemOrEquippedOrEmpty_ChangesNothing()
    {
        int userId = await CreateUserAsync(gold: 1000, cash: 0);
        await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 1001, SlotIndex = 4 });
        await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 9, ItemId = 1002, SlotIndex = 5 });

        Assert.IsNull(await _invRepo.SellFromBoxAsync(userId, 4, 1002, 10));
        Assert.IsNull(await _invRepo.SellFromBoxAsync(userId, 5, 1002, 10));
        Assert.IsNull(await _invRepo.SellFromBoxAsync(userId, 6, 1001, 10));

        Assert.AreEqual(1000L, await _balanceRepo.GetGoldAsync(userId));
        Assert.HasCount(2, await _invRepo.GetUserItemsAsync(userId));
    }

    [TestMethod]
    public async Task ReinforceInBox_ConsumesStoneAndChangesLevel()
    {
        int userId = await CreateUserAsync(gold: 0, cash: 0);
        var sword = await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 1001, SlotIndex = 0, Level = 1 });
        await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 13002, SlotIndex = 1 });

        byte? up = await _invRepo.ReinforceInBoxAsync(userId, 0, 1001, 1, 13002, +1);

        Assert.AreEqual((byte)2, up);
        var items = await _invRepo.GetUserItemsAsync(userId);
        Assert.AreEqual(sword.Id, items.Single().Id);
        Assert.AreEqual((byte)2, items.Single().Level);
    }

    [TestMethod]
    public async Task ReinforceInBox_FloorsAtZero_AndNeedsBothItems()
    {
        int userId = await CreateUserAsync(gold: 0, cash: 0);
        await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 1001, SlotIndex = 0, Level = 1 });
        await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = 0, ItemId = 13002, SlotIndex = 1 });

        Assert.IsNull(await _invRepo.ReinforceInBoxAsync(userId, 0, 1001, 2, 13002, +1));
        Assert.IsNull(await _invRepo.ReinforceInBoxAsync(userId, 0, 1002, 1, 13002, +1));
        byte? down = await _invRepo.ReinforceInBoxAsync(userId, 0, 1001, 1, 13002, -3);

        Assert.AreEqual((byte)0, down);
        Assert.HasCount(1, await _invRepo.GetUserItemsAsync(userId));
    }

    private Task<int> CreateUserAsync(long gold, int cash) =>
        _userRepo.CreateUserAsync(
            new UserEntity { Username = $"buyer{Guid.NewGuid():N}"[..11], PasswordHash = "x", Email = "b@test" },
            initialGold: gold,
            initialCash: cash);
}
