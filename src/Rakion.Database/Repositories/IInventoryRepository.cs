using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Data repository for item templates and player inventory/equipment instances.
/// </summary>
public interface IInventoryRepository
{
    Task<ItemEntity?> GetItemTemplateAsync(int itemId, CancellationToken ct = default);

    /// <summary>Inserts the templates or updates the existing rows with the same id. Returns the row count.</summary>
    Task<int> UpsertItemTemplatesAsync(IReadOnlyList<ItemEntity> templates, CancellationToken ct = default);

    /// <summary>
    /// Charges the price and puts one new item into an empty account box slot, in one transaction.
    /// Returns null (and changes nothing) when the balance is too low or the slot is no longer empty;
    /// otherwise the balances after the purchase.
    /// </summary>
    /// <summary>
    /// Removes the item with the given id from an account box slot and credits the gold, in one transaction.
    /// Returns null (and changes nothing) when that slot no longer holds that item; otherwise the balances.
    /// </summary>
    /// <summary>
    /// Consumes the stone in <paramref name="stoneSlot"/> and changes the level of the item in
    /// <paramref name="targetSlot"/> by <paramref name="levelDelta"/> (never below 0), in one transaction. Both are
    /// account box slots and must still hold the expected item ids. Returns the new level, or null when they don't.
    /// </summary>
    Task<byte?> ReinforceInBoxAsync(
        int userId,
        byte targetSlot,
        int targetItemId,
        byte stoneSlot,
        int stoneItemId,
        int levelDelta,
        CancellationToken ct = default);

    Task<SaleOutcome?> SellFromBoxAsync(int userId, byte slot, int itemId, long goldGained, CancellationToken ct = default);

    Task<PurchaseOutcome?> PurchaseIntoBoxAsync(
        int userId,
        int itemId,
        bool payWithGold,
        int price,
        byte slot,
        byte level,
        CancellationToken ct = default);
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

