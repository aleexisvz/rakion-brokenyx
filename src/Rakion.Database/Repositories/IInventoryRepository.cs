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

    /// <summary>
    /// Atomically swaps the contents of two account box slots (rows with character_id = 0).
    /// The destination may be empty. Returns false when the source slot is empty.
    /// </summary>
    Task<bool> SwapBoxSlotsAsync(int userId, byte sourceSlot, byte destinationSlot, CancellationToken ct = default);

    /// <summary>
    /// Atomically swaps the contents of two slots of the same user, each identified by its owner
    /// (character id, or 0 for the account box) and slot index. The destination may be empty.
    /// Returns false when the source slot is empty.
    /// </summary>
    Task<bool> SwapSlotsAsync(
        int userId,
        int sourceCharacterId,
        byte sourceSlot,
        int destinationCharacterId,
        byte destinationSlot,
        CancellationToken ct = default);
}

