namespace Rakion.Database.Repositories;

/// <summary>A present waiting in an account's present box (legacy pendingpresents row).</summary>
public sealed record PendingPresent(int Id, int ItemId);

public enum PresentAcceptStatus
{
    Accepted,
    NotFound,
    SlotTaken,
}

/// <summary>
/// The account's present box (roadmap 4.6), the legacy pendingpresents queue: presents are taken oldest first and
/// land in a chosen account box slot.
/// </summary>
public interface IPresentRepository
{
    /// <summary>Queues a present (an item id) for the account. Returns the pending id.</summary>
    Task<int> SendAsync(int userId, int itemId, CancellationToken ct = default);

    /// <summary>The oldest present of the account, or null when the box is empty.</summary>
    Task<PendingPresent?> PeekAsync(int userId, CancellationToken ct = default);

    /// <summary>The account's present with that pending id, or null.</summary>
    Task<PendingPresent?> GetAsync(int userId, int pendingId, CancellationToken ct = default);

    /// <summary>
    /// Moves the present into the empty account box slot <paramref name="boxSlot"/> with <paramref name="exp"/> (the
    /// quantity of a potion) and removes it from the queue, in one transaction.
    /// </summary>
    Task<PresentAcceptStatus> AcceptAsync(int userId, int pendingId, byte boxSlot, long exp, CancellationToken ct = default);

    /// <summary>Throws the present away. False when the account has no such present.</summary>
    Task<bool> DisposeAsync(int userId, int pendingId, CancellationToken ct = default);
}
