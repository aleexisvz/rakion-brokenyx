using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.World.Features.Economy;

namespace Rakion.World.Tests;

[TestClass]
public sealed class AccountInventoryProviderTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 22, 15, 0);

    private SqliteTestConnectionFactory _dbFactory = null!;
    private InventoryRepository _inventoryRepository = null!;
    private AccountInventoryProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_account_box_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _inventoryRepository = new InventoryRepository(_dbFactory);
        _provider = new AccountInventoryProvider(_inventoryRepository, new FixedTimeProvider(Now), NullLogger<AccountInventoryProvider>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task AccountBox_PlacesSharedItemsBySlot()
    {
        uint later = LegacyGameClock.ToMinutes(Now) + 600;
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 1001, SlotIndex = 0, Level = 3, Exp = 40 });
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 1002, SlotIndex = 7, LimitTimeMinutes = (int)later });

        var box = await _provider.GetAccountBoxAsync(5, 1, CancellationToken.None);

        Assert.AreEqual(new ItemSlot(1001, 3, 40, 0), box.Slots[0]);
        Assert.AreEqual(new ItemSlot(1002, 1, 0, later), box.Slots[7]);
        Assert.AreEqual(2, box.Slots.Count(s => !s.IsEmpty));
        Assert.IsEmpty(box.ExpiredItemIds);
        Assert.AreEqual((byte)1, box.BagCount);
    }

    [TestMethod]
    public async Task AccountBox_ExcludesCharacterItemsAndOtherUsers()
    {
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 9, ItemId = 2001, SlotIndex = 0 });
        await AddAsync(new UserItemEntity { UserId = 6, CharacterId = 0, ItemId = 2002, SlotIndex = 0 });

        var box = await _provider.GetAccountBoxAsync(5, 1, CancellationToken.None);

        Assert.IsTrue(box.Slots.All(s => s.IsEmpty));
    }

    [TestMethod]
    public async Task AccountBox_ListsExpiredRentalsInsteadOfSlotting()
    {
        uint earlier = LegacyGameClock.ToMinutes(Now) - 1;
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 3001, SlotIndex = 2, LimitTimeMinutes = (int)earlier });
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 9, ItemId = 3002, SlotIndex = 1, LimitTimeMinutes = (int)earlier });

        var box = await _provider.GetAccountBoxAsync(5, 1, CancellationToken.None);

        Assert.IsTrue(box.Slots[2].IsEmpty);
        CollectionAssert.AreEquivalent(new ushort[] { 3001, 3002 }, box.ExpiredItemIds.ToArray());
    }

    [TestMethod]
    public async Task AccountBox_SkipsOutOfRangeAndDuplicateSlots()
    {
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 4001, SlotIndex = 120 });
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 4002, SlotIndex = 3 });
        await AddAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 4003, SlotIndex = 3 });

        var box = await _provider.GetAccountBoxAsync(5, 1, CancellationToken.None);

        Assert.AreEqual((ushort)4002, box.Slots[3].ItemId);
        Assert.AreEqual(1, box.Slots.Count(s => !s.IsEmpty));
    }

    private Task AddAsync(UserItemEntity item) => _inventoryRepository.AddUserItemAsync(item);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
