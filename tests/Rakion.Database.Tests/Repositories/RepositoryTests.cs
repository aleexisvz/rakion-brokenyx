using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class RepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private UserRepository _userRepo = null!;
    private CharacterRepository _charRepo = null!;
    private AccountBalanceRepository _balanceRepo = null!;
    private InventoryRepository _invRepo = null!;

    [TestInitialize]
    public void Setup()
    {
        string dbName = $"TestDb_{Guid.NewGuid():N}";
        _factory = new SqliteTestConnectionFactory(dbName);
        _userRepo = new UserRepository(_factory);
        _charRepo = new CharacterRepository(_factory);
        _balanceRepo = new AccountBalanceRepository(_factory);
        _invRepo = new InventoryRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task CreateUser_InitializesUserGameInfoAndCashCorrectly()
    {
        var user = new UserEntity
        {
            Username = "TestPlayer1",
            PasswordHash = "$2a$11$mockhashmockhashmockhashmockhashmockhashmockhash",
            Email = "test@rakion.local",
            Country = 9,
            Authority = 0
        };

        int userId = await _userRepo.CreateUserAsync(user, initialGold: 10_000, initialCash: 500);

        Assert.IsGreaterThan(0, userId);
        Assert.AreEqual(userId, user.Id);

        var fetchedUser = await _userRepo.GetByUsernameAsync("TestPlayer1");
        Assert.IsNotNull(fetchedUser);
        Assert.AreEqual("TestPlayer1", fetchedUser.Username);
        Assert.AreEqual(user.PasswordHash, fetchedUser.PasswordHash);

        var gameInfo = await _userRepo.GetGameInfoByUserIdAsync(userId);
        Assert.IsNotNull(gameInfo);
        Assert.AreEqual(10_000, gameInfo.Gold);
        Assert.AreEqual(3, gameInfo.SlotCount);
        Assert.IsFalse(gameInfo.IsBanned);

        long cash = await _balanceRepo.GetCashAsync(userId);
        Assert.AreEqual(500, cash);
    }

    [TestMethod]
    public async Task UpdatePasswordHash_PersistsNewHash()
    {
        var user = new UserEntity { Username = "RehashUser", PasswordHash = "oldHash" };
        int userId = await _userRepo.CreateUserAsync(user);

        await _userRepo.UpdatePasswordHashAsync(userId, "$2b$11$newUpdatedBcryptHash");

        var fetched = await _userRepo.GetByIdAsync(userId);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("$2b$11$newUpdatedBcryptHash", fetched.PasswordHash);
    }

    [TestMethod]
    public async Task CreateCharacter_EnforcesSlotLimitsAndUniqueness()
    {
        var user = new UserEntity { Username = "WarriorUser", PasswordHash = "hash" };
        int userId = await _userRepo.CreateUserAsync(user);

        // 1. Create first character in slot 0
        var c1 = await _charRepo.CreateCharacterAsync(userId, "ShadowNinja", CharacterClass.Ninja, slotIndex: 0);
        Assert.IsGreaterThan(0, c1.Id);
        Assert.AreEqual("ShadowNinja", c1.Name);
        Assert.AreEqual(CharacterClass.Ninja, c1.Class);
        Assert.AreEqual(1, c1.Level);

        // 2. Creating another character in slot 0 should fail
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await _charRepo.CreateCharacterAsync(userId, "OtherNinja", CharacterClass.Ninja, slotIndex: 0);
        });

        // 3. Creating duplicate name should fail
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await _charRepo.CreateCharacterAsync(userId, "ShadowNinja", CharacterClass.Swordman, slotIndex: 1);
        });

        // 4. Exceeding slot count (default is 4, slotIndex 4 is out of bounds [0..3])
        await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await _charRepo.CreateCharacterAsync(userId, "SlotExceeded", CharacterClass.Archer, slotIndex: 4);
        });

        // 5. Query user characters
        var chars = await _charRepo.GetCharactersByUserIdAsync(userId);
        Assert.HasCount(1, chars);
        Assert.AreEqual("ShadowNinja", chars[0].Name);
    }

    [TestMethod]
    public async Task DeleteCharacter_RequiresCorrectDeleteKey()
    {
        var user = new UserEntity { Username = "DeleteUser", PasswordHash = "hash" };
        int userId = await _userRepo.CreateUserAsync(user);
        var ninja = await _charRepo.CreateCharacterAsync(userId, "ToKill", CharacterClass.Ninja, slotIndex: 0);

        // Wrong delete key
        bool deletedWrong = await _charRepo.DeleteCharacterAsync(ninja.Id, "wrongKey");
        Assert.IsFalse(deletedWrong);

        // Correct delete key (legacy default: sirmaster)
        bool deleted = await _charRepo.DeleteCharacterAsync(ninja.Id, "sirmaster");
        Assert.IsTrue(deleted);

        // Should no longer appear in active characters
        var chars = await _charRepo.GetCharactersByUserIdAsync(userId);
        Assert.IsEmpty(chars);
    }

    [TestMethod]
    public async Task AccountBalance_AtomicDeductionsPreventUnderflow()
    {
        var user = new UserEntity { Username = "RichPlayer", PasswordHash = "hash" };
        int userId = await _userRepo.CreateUserAsync(user, initialGold: 10_000, initialCash: 100);

        // Deduct 4,000 gold -> should succeed, balance = 6,000
        bool success1 = await _balanceRepo.TryDeductGoldAsync(userId, 4_000);
        Assert.IsTrue(success1);
        Assert.AreEqual(6_000, await _balanceRepo.GetGoldAsync(userId));

        // Deduct 7,000 gold (more than 6,000) -> should fail atomically, balance stays 6,000
        bool success2 = await _balanceRepo.TryDeductGoldAsync(userId, 7_000);
        Assert.IsFalse(success2);
        Assert.AreEqual(6_000, await _balanceRepo.GetGoldAsync(userId));

        // Add 2,000 gold -> balance = 8,000
        bool successAdd = await _balanceRepo.AddGoldAsync(userId, 2_000);
        Assert.IsTrue(successAdd);
        Assert.AreEqual(8_000, await _balanceRepo.GetGoldAsync(userId));

        // Cash operations
        bool cashDeduct1 = await _balanceRepo.TryDeductCashAsync(userId, 50);
        Assert.IsTrue(cashDeduct1);
        Assert.AreEqual(50, await _balanceRepo.GetCashAsync(userId));

        bool cashDeduct2 = await _balanceRepo.TryDeductCashAsync(userId, 80);
        Assert.IsFalse(cashDeduct2);
        Assert.AreEqual(50, await _balanceRepo.GetCashAsync(userId));
    }

    [TestMethod]
    public async Task Inventory_AddAndManageItems()
    {
        var user = new UserEntity { Username = "ItemUser", PasswordHash = "hash" };
        int userId = await _userRepo.CreateUserAsync(user);

        var item = new UserItemEntity
        {
            UserId = userId,
            CharacterId = 1,
            ItemId = 1001,
            Level = 3,
            SlotIndex = 1,
            LimitTimeMinutes = 0
        };

        var created = await _invRepo.AddUserItemAsync(item);
        Assert.IsGreaterThan(0, created.Id);

        var items = await _invRepo.GetUserItemsAsync(userId, characterId: 1);
        Assert.HasCount(1, items);
        Assert.AreEqual(1001, items[0].ItemId);
        Assert.AreEqual(3, items[0].Level);

        // Update slot
        bool slotUpdated = await _invRepo.UpdateSlotAsync(created.Id, userId, newSlotIndex: 5);
        Assert.IsTrue(slotUpdated);

        // Delete item
        bool removed = await _invRepo.RemoveUserItemAsync(created.Id, userId);
        Assert.IsTrue(removed);

        var itemsAfter = await _invRepo.GetUserItemsAsync(userId, characterId: 1);
        Assert.IsEmpty(itemsAfter);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception of type {typeof(TException).Name} was not thrown.");
        }
        catch (TException)
        {
            // Expected
        }
    }
}
