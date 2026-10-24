namespace Rakion.Database.Repositories;

/// <summary>
/// GM actions on accounts (roadmap 7.3). Gold and cash use <see cref="IAccountBalanceRepository"/>.
/// </summary>
public interface IAdminRepository
{
    /// <summary>Bans the account until <paramref name="untilUtc"/>, or permanently when it is null.</summary>
    Task<bool> BanAsync(int userId, DateTime? untilUtc, string reason, CancellationToken ct = default);

    Task<bool> UnbanAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Puts one item into the lowest free slot of the first <paramref name="boxSlots"/> box slots, with
    /// <paramref name="exp"/> (a potion's quantity). Returns the slot, or null when the item does not exist or the box
    /// is full.
    /// </summary>
    Task<byte?> GiveItemAsync(int userId, int itemId, int boxSlots, long exp = 0, CancellationToken ct = default);
}
