namespace Rakion.Database.Repositories;

/// <summary>A Messenger account: its login name and the name its friends see.</summary>
public sealed record BuddyAccount(int UserId, string Account, string DisplayName);

/// <summary>A Messenger group of an account.</summary>
public sealed record BuddyGroupRow(int Id, string Name, int Flags);

/// <summary>A friend of an account with the group the owner placed it in.</summary>
public sealed record BuddyRelation(string Account, string DisplayName, string GroupName);

/// <summary>
/// Messenger friends (legacy <c>buddylist</c>, here <c>buddies</c>). A friendship is stored as two rows, one per side,
/// written together.
/// </summary>
public interface IBuddyRepository
{
    /// <summary>The account by login name; the display name is the Messenger name, else the first character, else the login.</summary>
    Task<BuddyAccount?> GetAccountAsync(string account, CancellationToken ct = default);

    Task<IReadOnlyList<BuddyRelation>> GetFriendsAsync(string account, CancellationToken ct = default);

    /// <summary>Creates both directions of the friendship. False when the friend does not exist, is the owner or already a friend.</summary>
    Task<bool> AddFriendAsync(string owner, string friend, CancellationToken ct = default);

    /// <summary>Removes both directions. False when they were not friends.</summary>
    Task<bool> RemoveFriendAsync(string owner, string friend, CancellationToken ct = default);

    Task<IReadOnlyList<BuddyGroupRow>> GetGroupsAsync(string owner, CancellationToken ct = default);

    /// <summary>Stores a group of the owner, replacing one with the same id. False when the owner has 50 groups.</summary>
    Task<bool> SaveGroupAsync(string owner, BuddyGroupRow group, CancellationToken ct = default);

    /// <summary>Stores the name friends see (sent by the client after choosing a character).</summary>
    Task SetDisplayNameAsync(int userId, string displayName, CancellationToken ct = default);
}
