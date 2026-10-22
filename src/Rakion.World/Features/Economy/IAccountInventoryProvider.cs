using Rakion.Core.Packets;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Builds the account item box that World sends in WorldLoginPart2 (lane A, roadmap 4.1).
/// </summary>
public interface IAccountInventoryProvider
{
    /// <param name="bagCount">Unlocked bags (user_game_info.bag_count); the client shows 40 box slots per bag.</param>
    Task<AccountBoxBlock> GetAccountBoxAsync(int userId, byte bagCount, CancellationToken cancellationToken);
}
