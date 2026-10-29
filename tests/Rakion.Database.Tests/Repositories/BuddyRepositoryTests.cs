using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Database.Tests.Repositories;

[TestClass]
public sealed class BuddyRepositoryTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private BuddyRepository _repository = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new SqliteTestConnectionFactory($"BuddyRepo_{Guid.NewGuid():N}");
        _repository = new BuddyRepository(_factory);
        var users = new UserRepository(_factory);
        int alice = await users.CreateUserAsync(new UserEntity { Username = "alice", PasswordHash = "x", Email = "a@test" });
        await users.CreateUserAsync(new UserEntity { Username = "bob", PasswordHash = "x", Email = "b@test" });
        await new CharacterRepository(_factory).CreateCharacterAsync(alice, "Alicia", CharacterClass.Mage, 0);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Friendships_AreMutual_AndRefuseSelfUnknownAndRepeats()
    {
        Assert.IsTrue(await _repository.AddFriendAsync("alice", "bob"));
        Assert.IsFalse(await _repository.AddFriendAsync("alice", "bob"), "already friends");
        Assert.IsFalse(await _repository.AddFriendAsync("alice", "alice"));
        Assert.IsFalse(await _repository.AddFriendAsync("alice", "nobody"));

        var bobFriends = await _repository.GetFriendsAsync("bob");
        Assert.AreEqual(new BuddyRelation("alice", "Alicia", string.Empty), bobFriends.Single(), "the first character is the shown name");

        Assert.IsTrue(await _repository.RemoveFriendAsync("bob", "alice"));
        Assert.IsEmpty(await _repository.GetFriendsAsync("alice"));
        Assert.IsFalse(await _repository.RemoveFriendAsync("bob", "alice"));
    }

    [TestMethod]
    public async Task Groups_AreStoredPerOwner_AndReplacedById()
    {
        Assert.IsTrue(await _repository.SaveGroupAsync("alice", new BuddyGroupRow(1, "Friends", 0)));
        Assert.IsTrue(await _repository.SaveGroupAsync("alice", new BuddyGroupRow(2, "Clan", 1)));
        Assert.IsTrue(await _repository.SaveGroupAsync("alice", new BuddyGroupRow(1, "Amigos", 0)));

        CollectionAssert.AreEqual(
            new[] { new BuddyGroupRow(1, "Amigos", 0), new BuddyGroupRow(2, "Clan", 1) },
            (await _repository.GetGroupsAsync("alice")).ToArray());
        Assert.IsEmpty(await _repository.GetGroupsAsync("bob"));
    }

    [TestMethod]
    public async Task DisplayName_PrefersTheMessengerName()
    {
        var alice = await _repository.GetAccountAsync("alice");
        Assert.AreEqual("Alicia", alice!.DisplayName);

        await _repository.SetDisplayNameAsync(alice.UserId, "AliceNick");

        Assert.AreEqual("AliceNick", (await _repository.GetAccountAsync("alice"))!.DisplayName);
        Assert.AreEqual("bob", (await _repository.GetAccountAsync("bob"))!.DisplayName, "no character: the login name");
        Assert.IsNull(await _repository.GetAccountAsync("nobody"));
    }
}
