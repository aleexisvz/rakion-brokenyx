using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class InventorySwapTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private InventoryRepository _invRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new SqliteTestConnectionFactory($"InventorySwapDb_{Guid.NewGuid():N}");
        _invRepo = new InventoryRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task SwapBoxSlots_IntoEmptySlot_MovesItem()
    {
        var item = await AddAsync(userId: 1, characterId: 0, itemId: 1001, slot: 0);

        Assert.IsTrue(await _invRepo.SwapBoxSlotsAsync(1, 0, 5));

        Assert.AreEqual((byte)5, await SlotOfAsync(1, item.Id));
    }

    [TestMethod]
    public async Task SwapBoxSlots_WithOccupiedDestination_SwapsBothItems()
    {
        var first = await AddAsync(userId: 1, characterId: 0, itemId: 1001, slot: 0);
        var second = await AddAsync(userId: 1, characterId: 0, itemId: 1002, slot: 1);

        Assert.IsTrue(await _invRepo.SwapBoxSlotsAsync(1, 0, 1));

        Assert.AreEqual((byte)1, await SlotOfAsync(1, first.Id));
        Assert.AreEqual((byte)0, await SlotOfAsync(1, second.Id));
    }

    [TestMethod]
    public async Task SwapBoxSlots_EmptySourceOrOtherOwner_ReturnsFalseAndChangesNothing()
    {
        var other = await AddAsync(userId: 2, characterId: 0, itemId: 1001, slot: 0);
        var equipped = await AddAsync(userId: 1, characterId: 9, itemId: 1002, slot: 0);

        Assert.IsFalse(await _invRepo.SwapBoxSlotsAsync(1, 0, 3));

        Assert.AreEqual((byte)0, await SlotOfAsync(2, other.Id));
        Assert.AreEqual((byte)0, await SlotOfAsync(1, equipped.Id));
    }

    [TestMethod]
    public async Task SwapSlots_BetweenBoxAndEquipment_MovesOwnerAndSlot()
    {
        var boxed = await AddAsync(userId: 1, characterId: 0, itemId: 1001, slot: 7);
        var worn = await AddAsync(userId: 1, characterId: 9, itemId: 2001, slot: 2);

        Assert.IsTrue(await _invRepo.SwapSlotsAsync(1, 0, 7, 9, 2));

        var items = await _invRepo.GetUserItemsAsync(1);
        var nowWorn = items.Single(i => i.Id == boxed.Id);
        var nowBoxed = items.Single(i => i.Id == worn.Id);
        Assert.AreEqual((9, (byte)2), (nowWorn.CharacterId, nowWorn.SlotIndex));
        Assert.AreEqual((0, (byte)7), (nowBoxed.CharacterId, nowBoxed.SlotIndex));
    }

    [TestMethod]
    public async Task ConsumePotion_LowersTheQuantity_AndRefusesAnEmptyOrWrongSlot()
    {
        var potion = await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = 1, CharacterId = 0, ItemId = 12001, SlotIndex = 3, Exp = 2 });

        Assert.AreEqual(1L, await _invRepo.ConsumePotionAsync(1, 3, 12001));
        Assert.AreEqual(0L, await _invRepo.ConsumePotionAsync(1, 3, 12001));
        Assert.IsNull(await _invRepo.ConsumePotionAsync(1, 3, 12001), "none left");
        Assert.IsNull(await _invRepo.ConsumePotionAsync(1, 3, 12002), "another item");
        Assert.IsNull(await _invRepo.ConsumePotionAsync(2, 3, 12001), "another account");
        Assert.AreEqual(0L, (await _invRepo.GetUserItemsAsync(1)).Single(i => i.Id == potion.Id).Exp);
    }

    [TestMethod]
    public async Task StackBoxItems_AddsTheSourceQuantityToTheTarget_AndDeletesTheSource()
    {
        var source = await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = 1, CharacterId = 0, ItemId = 12001, SlotIndex = 2, Exp = 5 });
        var target = await _invRepo.AddUserItemAsync(new UserItemEntity { UserId = 1, CharacterId = 0, ItemId = 12002, SlotIndex = 7, Exp = 3 });

        Assert.AreEqual(((int?)12001, (int?)12002), await _invRepo.GetBoxItemIdsAsync(1, 2, 7));
        Assert.IsFalse(await _invRepo.StackBoxItemsAsync(1, 2, 2), "same slot");
        Assert.IsFalse(await _invRepo.StackBoxItemsAsync(1, 2, 9), "empty target");
        Assert.IsTrue(await _invRepo.StackBoxItemsAsync(1, 2, 7));

        var items = await _invRepo.GetUserItemsAsync(1);
        Assert.IsFalse(items.Any(i => i.Id == source.Id));
        Assert.AreEqual(8L, items.Single(i => i.Id == target.Id).Exp);
    }

    private Task<UserItemEntity> AddAsync(int userId, int characterId, int itemId, byte slot) =>
        _invRepo.AddUserItemAsync(new UserItemEntity { UserId = userId, CharacterId = characterId, ItemId = itemId, SlotIndex = slot });

    private async Task<byte> SlotOfAsync(int userId, int rowId) =>
        (await _invRepo.GetUserItemsAsync(userId)).Single(i => i.Id == rowId).SlotIndex;
}
