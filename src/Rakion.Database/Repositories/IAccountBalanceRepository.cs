namespace Rakion.Database.Repositories;

/// <summary>
/// Data repository for gold and cash currency balances with atomic ACID transaction guarantees.
/// Prevents concurrency race conditions, integer underflows, and item duplication exploits.
/// </summary>
public interface IAccountBalanceRepository
{
    Task<long> GetGoldAsync(int userId, CancellationToken ct = default);
    Task<int> GetCashAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Atomically deducts gold from the user's account if sufficient balance is available.
    /// Returns true if successful; false if insufficient funds or user not found.
    /// </summary>
    Task<bool> TryDeductGoldAsync(int userId, long amount, CancellationToken ct = default);

    /// <summary>
    /// Atomically credits gold to the user's account.
    /// </summary>
    Task<bool> AddGoldAsync(int userId, long amount, CancellationToken ct = default);

    /// <summary>
    /// Atomically deducts cash from the user's account if sufficient balance is available.
    /// Returns true if successful; false if insufficient funds or user not found.
    /// </summary>
    /// <summary>
    /// Buys PowerUser time in one transaction: refuses when the current expiry is already more than
    /// <paramref name="maxAheadMinutes"/> past <paramref name="nowMinutes"/>, charges <paramref name="cashPrice"/>
    /// (guarded), adds <paramref name="pointsGained"/> PowerUser points and sets the expiry to
    /// expiry + duration when extending, or now + duration otherwise. Times are LegacyGameClock minutes.
    /// </summary>
    Task<PowerUserPurchaseOutcome> BuyPowerUserAsync(
        int userId,
        int cashPrice,
        bool extend,
        long nowMinutes,
        long durationMinutes,
        long maxAheadMinutes,
        int pointsGained,
        CancellationToken ct = default);

    Task<bool> TryDeductCashAsync(int userId, int amount, CancellationToken ct = default);

    /// <summary>
    /// Atomically credits cash to the user's account.
    /// </summary>
    Task<bool> AddCashAsync(int userId, int amount, CancellationToken ct = default);
}

