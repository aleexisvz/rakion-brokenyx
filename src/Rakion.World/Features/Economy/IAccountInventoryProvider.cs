using Rakion.Core.Packets;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Builds the account item box that World sends in WorldLoginPart2 (lane A, roadmap 4.1).
/// </summary>
public interface IAccountInventoryProvider
{
    Task<AccountBoxBlock> GetAccountBoxAsync(int userId, CancellationToken cancellationToken);
}
