using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Data repository for item templates and player inventory/equipment instances.
/// </summary>
public interface IInventoryRepository
{
    Task<ItemEntity?> GetItemTemplateAsync(int itemId, CancellationToken ct = default);
    Task<IReadOnlyList<UserItemEntity>> GetUserItemsAsync(int userId, int? characterId = null, CancellationToken ct = default);
    Task<UserItemEntity> AddUserItemAsync(UserItemEntity item, CancellationToken ct = default);
    Task<bool> RemoveUserItemAsync(int userItemId, int userId, CancellationToken ct = default);
    Task<bool> UpdateSlotAsync(int userItemId, int userId, byte newSlotIndex, CancellationToken ct = default);
    Task<bool> UpdateLevelAndExpAsync(int userItemId, byte newLevel, long newExp, CancellationToken ct = default);
}

