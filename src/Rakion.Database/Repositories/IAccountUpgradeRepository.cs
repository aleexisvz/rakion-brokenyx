namespace Rakion.Database.Repositories;

/// <summary>
/// Paid account upgrades: stat reset (4.7), extra bag and potion slots (4.8). Every call charges and applies the
/// change in one transaction and returns the balances after it.
/// </summary>
public interface IAccountUpgradeRepository
{
    /// <summary>
    /// Clears the character's stats for <paramref name="cashPrice"/> cash: 3 level points per level above 1 stay on
    /// the character and the rest of the points goes back to the account's PowerUser points.
    /// </summary>
    Task<StatResetOutcome> ResetStatsAsync(int userId, int characterId, int cashPrice, CancellationToken ct = default);

    /// <summary>Adds one bag for <paramref name="cashPrice"/> cash while the account has fewer than <paramref name="maxBags"/>.</summary>
    Task<UpgradeOutcome> BuyBagAsync(int userId, int cashPrice, byte maxBags, CancellationToken ct = default);

    /// <summary>
    /// Adds one potion slot to the character; <paramref name="priceForCurrentSlots"/> gives the price for the current
    /// count, or null when no more slots can be bought.
    /// </summary>
    Task<UpgradeOutcome> BuyPotionSlotAsync(
        int userId,
        int characterId,
        Func<byte, (bool PayWithGold, int Price)?> priceForCurrentSlots,
        CancellationToken ct = default);
}
