namespace Rakion.World.Features.Economy;

/// <summary>
/// Builds the account box and every character's equipment that World sends at login (lane A, roadmap 4.1).
/// </summary>
public interface IAccountInventoryProvider
{
    /// <param name="bagCount">Unlocked bags (user_game_info.bag_count); 30 box slots per bag (legacy purchase search, imul 0x1E).</param>
    Task<AccountInventory> GetAccountInventoryAsync(int userId, byte bagCount, CancellationToken cancellationToken);
}
