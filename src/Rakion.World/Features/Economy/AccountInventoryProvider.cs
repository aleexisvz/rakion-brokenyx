using Microsoft.Extensions.Logging;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
using Rakion.Database.Repositories;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Maps the account's items the way the legacy DBCommandCharacterList does: shared rows (character id 0) go
/// to the 120-slot box and character rows to that character's 19 equipment slots, each at the slot named by
/// its slot index; rows whose rental time already passed are listed as expired instead.
/// </summary>
public sealed class AccountInventoryProvider : IAccountInventoryProvider
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountInventoryProvider> _logger;

    public AccountInventoryProvider(
        IInventoryRepository inventoryRepository,
        TimeProvider timeProvider,
        ILogger<AccountInventoryProvider> logger)
    {
        _inventoryRepository = inventoryRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AccountInventory> GetAccountInventoryAsync(int userId, byte bagCount, CancellationToken cancellationToken)
    {
        var items = await _inventoryRepository.GetUserItemsAsync(userId, characterId: null, cancellationToken).ConfigureAwait(false);
        uint now = LegacyGameClock.ToMinutes(_timeProvider.GetLocalNow().DateTime);

        var slots = new ItemSlot[AccountBoxBlock.SlotCount];
        var expired = new List<ushort>();
        var equipment = new Dictionary<int, ItemSlot[]>();

        foreach (var item in items)
        {
            if (item.ItemId is <= 0 or > ushort.MaxValue)
            {
                _logger.LogWarning("User {UserId}: item row {RowId} has item id {ItemId} outside u16; skipped.", userId, item.Id, item.ItemId);
                continue;
            }

            // Legacy rule: a rented item whose limit time passed is reported as expired, wherever it sits.
            if (item.LimitTimeMinutes > 0 && (uint)item.LimitTimeMinutes < now)
            {
                if (expired.Count < byte.MaxValue)
                {
                    expired.Add((ushort)item.ItemId);
                }

                continue;
            }

            ItemSlot[] target = slots;
            if (item.CharacterId != 0 && !equipment.TryGetValue(item.CharacterId, out target!))
            {
                target = new ItemSlot[CharacterEquipment.SlotCount];
                equipment[item.CharacterId] = target;
            }

            if (item.SlotIndex >= target.Length || !target[item.SlotIndex].IsEmpty)
            {
                _logger.LogWarning(
                    "User {UserId}: item row {RowId} (character {CharacterId}) has slot {Slot} out of range or already used; skipped.",
                    userId,
                    item.Id,
                    item.CharacterId,
                    item.SlotIndex);
                continue;
            }

            target[item.SlotIndex] = new ItemSlot(
                (ushort)item.ItemId,
                item.Level,
                (uint)Math.Clamp(item.Exp, 0, uint.MaxValue),
                (uint)Math.Max(item.LimitTimeMinutes, 0));
        }

        _logger.LogInformation(
            "User {UserId}: account box with {ItemCount} items, {EquippedCount} equipped items on {CharacterCount} characters, {ExpiredCount} expired, {BagCount} bags.",
            userId,
            slots.Count(s => !s.IsEmpty),
            equipment.Values.Sum(e => e.Count(s => !s.IsEmpty)),
            equipment.Count,
            expired.Count,
            bagCount);

        return new AccountInventory(new AccountBoxBlock(slots, expired, bagCount), equipment);
    }
}
