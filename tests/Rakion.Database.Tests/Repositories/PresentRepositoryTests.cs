using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class PresentRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private PresentRepository _presents = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new SqliteTestConnectionFactory($"PresentDb_{Guid.NewGuid():N}");
        _presents = new PresentRepository(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Presents_AreTakenOldestFirst_IntoAnEmptyBoxSlot()
    {
        Assert.IsNull(await _presents.PeekAsync(1));
        int first = await _presents.SendAsync(1, 12001);
        int second = await _presents.SendAsync(1, 3001);
        await _presents.SendAsync(2, 3002);

        Assert.AreEqual(2, await _presents.CountAsync(1));
        Assert.AreEqual(new PendingPresent(first, 12001), await _presents.PeekAsync(1));
        Assert.AreEqual(new PendingPresent(second, 3001), await _presents.GetAsync(1, second));
        Assert.IsNull(await _presents.GetAsync(2, second), "another account's present");

        await new InventoryRepository(_factory).AddUserItemAsync(new UserItemEntity { UserId = 1, CharacterId = 0, ItemId = 1001, SlotIndex = 4 });
        Assert.AreEqual(PresentAcceptStatus.SlotTaken, await _presents.AcceptAsync(1, first, 4, 10));
        Assert.AreEqual(PresentAcceptStatus.Accepted, await _presents.AcceptAsync(1, first, 5, 10));
        Assert.AreEqual(PresentAcceptStatus.NotFound, await _presents.AcceptAsync(1, first, 6, 10));

        var potion = (await new InventoryRepository(_factory).GetUserItemsAsync(1)).Single(i => i.SlotIndex == 5);
        Assert.AreEqual((12001, 10L), (potion.ItemId, potion.Exp));

        Assert.IsTrue(await _presents.DisposeAsync(1, second));
        Assert.IsFalse(await _presents.DisposeAsync(1, second));
        Assert.IsNull(await _presents.PeekAsync(1));
    }
}
