using Microsoft.Extensions.Logging;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
using Rakion.Database.Repositories;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Maps the account's shared items (character id 0) to the 120-slot box, the way the legacy
/// DBCommandCharacterList does: each row goes to the slot named by its slot index, and rows whose
/// rental time already passed are listed as expired instead.
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

    public async Task<AccountBoxBlock> GetAccountBoxAsync(int userId, CancellationToken cancellationToken)
    {
        var items = await _inventoryRepository.GetUserItemsAsync(userId, characterId: null, cancellationToken).ConfigureAwait(false);
        uint now = LegacyGameClock.ToMinutes(_timeProvider.GetLocalNow().DateTime);

        var slots = new AccountBoxSlot[AccountBoxBlock.SlotCount];
        var expired = new List<ushort>();

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

            if (item.CharacterId != 0)
            {
                continue;
            }

            if (item.SlotIndex >= AccountBoxBlock.SlotCount || !slots[item.SlotIndex].IsEmpty)
            {
                _logger.LogWarning(
                    "User {UserId}: box item row {RowId} has slot {Slot} out of range or already used; skipped.",
                    userId,
                    item.Id,
                    item.SlotIndex);
                continue;
            }

            slots[item.SlotIndex] = new AccountBoxSlot(
                (ushort)item.ItemId,
                item.Level,
                (uint)Math.Clamp(item.Exp, 0, uint.MaxValue),
                (uint)Math.Max(item.LimitTimeMinutes, 0));
        }

        return new AccountBoxBlock(slots, expired);
    }
}
