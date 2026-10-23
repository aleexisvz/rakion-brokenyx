using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Lane A shop purchases (roadmap 4.2) and sales (4.3). Mirrors the legacy rules (RakionWorldServ.exe
/// 0x40CB10 / 0x40A6C0 for buying, 0x40CD70 / 0x40A810 for selling); each money change runs in one
/// database transaction.
/// </summary>
public sealed class ShopModule : IWorldOpcodeModule
{
    // Observed: the legacy server searches bag_count * 30 box slots for a free one (imul 0x1E at 0x40CC4E).
    private const int SlotsPerBag = 30;

    // Observed default of useriteminfo.level in the legacy schema.
    private const byte NewItemLevel = 1;

    // Observed: the legacy shop stores a quantity in the exp column for items 12000..12999 (potions), read from
    // the item's catalog row (RakionWorldServ.exe 0x4174E9): the u16 at +0xE of the loaded row, which the loader
    // 0x40A670 fills with hit2 (Observed).
    private const int FirstPotionId = 12000;
    private const int LastPotionId = 12999;

    // Observed at 0x40CC52: type 10 items are packages that expand into several items.
    private const byte PackageItemType = 10;

    private readonly ISessionSender _sender;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IAccountBalanceRepository _balanceRepository;
    private readonly IUserRepository _userRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShopModule> _logger;

    public ShopModule(
        ISessionSender sender,
        IInventoryRepository inventoryRepository,
        IAccountBalanceRepository balanceRepository,
        IUserRepository userRepository,
        TimeProvider timeProvider,
        ILogger<ShopModule> logger)
    {
        _sender = sender;
        _inventoryRepository = inventoryRepository;
        _balanceRepository = balanceRepository;
        _userRepository = userRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.InventoryBuy, HandleBuyAsync);
        registry.Map(ClientOpcode.InventorySell, HandleSellAsync);
    }

    private async ValueTask HandleBuyAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!InventoryBuyRequestPacket.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[{ConnectionId}] InventoryBuy rejected: malformed payload ({Length} bytes).", view.ConnectionId, payload.Length);
            return;
        }

        if (view.UserId is not int userId || view.CharacterId is null)
        {
            _logger.LogWarning("[{ConnectionId}] InventoryBuy rejected: no authenticated user with an active character.", view.ConnectionId);
            return;
        }

        var (result, bought, gold, cash) = await TryBuyAsync(userId, request, view, cancellationToken).ConfigureAwait(false);
        var response = new InventoryBuyResponsePacket(
            result,
            (uint)Math.Clamp(gold, 0, uint.MaxValue),
            (uint)Math.Max(cash, 0),
            bought is null ? null : [bought.Value]);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(byte Result, BoughtItem? Item, long Gold, int Cash)> TryBuyAsync(
        int userId,
        InventoryBuyRequestPacket request,
        IWorldSessionView view,
        CancellationToken cancellationToken)
    {
        byte result = InventoryBuyResponsePacket.ResultNotBuyable;
        ItemEntity? template = null;
        int price = 0;

        if (request.UsesCoupon != 0)
        {
            result = InventoryBuyResponsePacket.ResultCouponInvalid;
            _logger.LogInformation("[{ConnectionId}] InventoryBuy {ItemId}: coupons are not supported yet.", view.ConnectionId, request.ItemId);
        }
        else
        {
            template = await _inventoryRepository.GetItemTemplateAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
            var buyerInfo = await _userRepository.GetGameInfoByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            long now = LegacyGameClock.ToMinutes(_timeProvider.GetLocalNow().DateTime);
            bool isPowerUser = buyerInfo is not null && buyerInfo.PowerTime > now;
            string? reason = RejectionReason(template, request.PayWithGold, isPowerUser);
            if (reason is not null)
            {
                _logger.LogInformation("[{ConnectionId}] InventoryBuy {ItemId} rejected: {Reason}.", view.ConnectionId, request.ItemId, reason);
                template = null;
            }
            else
            {
                price = request.PayWithGold ? template!.GoldPrice : template!.CashPrice;
            }
        }

        if (template is not null)
        {
            var gameInfo = await _userRepository.GetGameInfoByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            byte? slot = await FindFreeBoxSlotAsync(userId, gameInfo?.BagCount ?? 1, cancellationToken).ConfigureAwait(false);
            if (slot is null)
            {
                result = InventoryBuyResponsePacket.ResultBoxFull;
            }
            else
            {
                var outcome = await _inventoryRepository
                    .PurchaseIntoBoxAsync(
                        userId,
                        template.Id,
                        request.PayWithGold,
                        price,
                        slot.Value,
                        NewItemLevel,
                        cancellationToken,
                        template.Id is >= FirstPotionId and <= LastPotionId ? template.Hit2 : 0)
                    .ConfigureAwait(false);
                if (outcome is not null)
                {
                    _logger.LogInformation(
                        "[{ConnectionId}] User {UserId} bought item {ItemId} for {Price} {Currency} into box slot {Slot} (row {RowId}); balance gold {Gold}, cash {Cash}.",
                        view.ConnectionId,
                        userId,
                        template.Id,
                        price,
                        request.PayWithGold ? "gold" : "cash",
                        slot.Value,
                        outcome.UserItemId,
                        outcome.Gold,
                        outcome.Cash);
                    return (InventoryBuyResponsePacket.ResultSuccess, new BoughtItem(slot.Value, (ushort)template.Id, NewItemLevel), outcome.Gold, outcome.Cash);
                }

                _logger.LogInformation("[{ConnectionId}] InventoryBuy {ItemId}: not enough {Currency} for {Price}.", view.ConnectionId, template.Id, request.PayWithGold ? "gold" : "cash", price);
            }
        }

        long gold = await _balanceRepository.GetGoldAsync(userId, cancellationToken).ConfigureAwait(false);
        int cash = await _balanceRepository.GetCashAsync(userId, cancellationToken).ConfigureAwait(false);
        return (result, null, gold, cash);
    }

    private async ValueTask HandleSellAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!InventorySellRequestPacket.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[{ConnectionId}] InventorySell rejected: malformed payload ({Length} bytes).", view.ConnectionId, payload.Length);
            return;
        }

        if (view.UserId is not int userId || view.CharacterId is null)
        {
            _logger.LogWarning("[{ConnectionId}] InventorySell rejected: no authenticated user with an active character.", view.ConnectionId);
            return;
        }

        SaleOutcome? outcome = null;
        if (request.BoxSlot >= AccountBoxBlock.SlotCount)
        {
            // The legacy server disconnects here (error 0x3B); answering keeps the client from hanging.
            _logger.LogWarning("[{ConnectionId}] InventorySell rejected: box slot {Slot} out of range.", view.ConnectionId, request.BoxSlot);
        }
        else
        {
            var boxItems = await _inventoryRepository.GetUserItemsAsync(userId, characterId: 0, cancellationToken).ConfigureAwait(false);
            var item = boxItems.FirstOrDefault(i => i.SlotIndex == request.BoxSlot);
            var template = item is null
                ? null
                : await _inventoryRepository.GetItemTemplateAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
            long? price = item is null ? null : SellPrice(item.ItemId, template);

            if (item is null || price is null)
            {
                _logger.LogInformation(
                    "[{ConnectionId}] InventorySell slot {Slot} rejected: {Reason}.",
                    view.ConnectionId,
                    request.BoxSlot,
                    item is null ? "empty slot" : $"item {item.ItemId} cannot be sold yet");
            }
            else
            {
                outcome = await _inventoryRepository
                    .SellFromBoxAsync(userId, request.BoxSlot, item.ItemId, price.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (outcome is not null)
                {
                    _logger.LogInformation(
                        "[{ConnectionId}] User {UserId} sold item {ItemId} (row {RowId}) from box slot {Slot} for {Price} gold; balance gold {Gold}, cash {Cash}.",
                        view.ConnectionId,
                        userId,
                        item.ItemId,
                        outcome.UserItemId,
                        request.BoxSlot,
                        price.Value,
                        outcome.Gold,
                        outcome.Cash);
                }
            }
        }

        long gold = outcome?.Gold ?? await _balanceRepository.GetGoldAsync(userId, cancellationToken).ConfigureAwait(false);
        int cash = outcome?.Cash ?? await _balanceRepository.GetCashAsync(userId, cancellationToken).ConfigureAwait(false);
        var response = new InventorySellResponsePacket(
            outcome is null ? InventorySellResponsePacket.ResultNotSellable : InventorySellResponsePacket.ResultSuccess,
            (uint)Math.Clamp(gold, 0, uint.MaxValue),
            (uint)Math.Max(cash, 0),
            request.BoxSlot);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gold paid for selling an item, as the legacy server computes it (RakionWorldServ.exe 0x40CD70 / 0x40A810):
    /// gold-shop items pay 40 % of their gold price, cash-shop items 150 % of their cash price (as gold), other
    /// items nothing; ids 12000..12999 pay nothing. Constants 0.4 and 1.5 Observed; truncation as _ftol.
    /// Returns null for creature items (8000..8999), whose legacy price table is not known yet.
    /// </summary>
    public static long? SellPrice(int itemId, ItemEntity? template)
    {
        if (itemId is >= 8000 and <= 8999)
        {
            return null; // UNKNOWN: level-based price table of creature items.
        }

        if (itemId is >= 12000 and <= 12999 || template is null)
        {
            return 0;
        }

        return template.Shop switch
        {
            1 => (long)(template.GoldPrice * 0.4),
            2 => (long)(template.CashPrice * 1.5),
            _ => 0,
        };
    }

    // Same rules as the legacy price check (RakionWorldServ.exe 0x40A6C0): shop 1 = gold only,
    // 2 = cash only, 3 = both, anything else is not sold.
    private static string? RejectionReason(ItemEntity? template, bool payWithGold, bool isPowerUser)
    {
        if (template is null || template.Id is <= 0 or > ushort.MaxValue)
        {
            return "unknown item";
        }

        if (template.Shop is 0 or > 3)
        {
            return "not sold in the shop";
        }

        if (payWithGold ? template.Shop == 2 : template.Shop == 1)
        {
            return payWithGold ? "cash-only item" : "gold-only item";
        }

        if (template.Type == PackageItemType)
        {
            return "packages are not supported yet";
        }

        // Legacy 0x40A6C0: a template with the power flag set (+0x1C) is only sold while the buyer's
        // PowerUser state is active (Observed check; the flag's meaning Derived).
        if (template.Power != 0 && !isPowerUser)
        {
            return "PowerUser-only item and the buyer has no active PowerUser";
        }

        return null;
    }

    // The legacy server takes the highest free slot within the unlocked bags (0x40CCBE..0x40CCE6).
    private async Task<byte?> FindFreeBoxSlotAsync(int userId, byte bagCount, CancellationToken cancellationToken)
    {
        int limit = Math.Min(Math.Max((int)bagCount, 1) * SlotsPerBag, AccountBoxBlock.SlotCount);
        var items = await _inventoryRepository.GetUserItemsAsync(userId, characterId: 0, cancellationToken).ConfigureAwait(false);
        var used = items.Select(i => (int)i.SlotIndex).ToHashSet();
        for (int slot = limit - 1; slot >= 0; slot--)
        {
            if (!used.Contains(slot))
            {
                return (byte)slot;
            }
        }

        return null;
    }
}
