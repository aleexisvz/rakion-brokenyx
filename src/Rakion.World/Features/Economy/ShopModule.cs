using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Lane A shop purchases (roadmap 4.2). Mirrors the legacy validation (RakionWorldServ.exe 0x40CB10 and
/// 0x40A6C0) and charges inside one database transaction.
/// </summary>
public sealed class ShopModule : IWorldOpcodeModule
{
    // Observed: the legacy server searches bag_count * 30 box slots for a free one (imul 0x1E at 0x40CC4E).
    private const int SlotsPerBag = 30;

    // Observed default of useriteminfo.level in the legacy schema.
    private const byte NewItemLevel = 1;

    // Observed at 0x40CC52: type 10 items are packages that expand into several items.
    private const byte PackageItemType = 10;

    private readonly ISessionSender _sender;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IAccountBalanceRepository _balanceRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<ShopModule> _logger;

    public ShopModule(
        ISessionSender sender,
        IInventoryRepository inventoryRepository,
        IAccountBalanceRepository balanceRepository,
        IUserRepository userRepository,
        ILogger<ShopModule> logger)
    {
        _sender = sender;
        _inventoryRepository = inventoryRepository;
        _balanceRepository = balanceRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.InventoryBuy, HandleBuyAsync);
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
            string? reason = RejectionReason(template, request.PayWithGold);
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
                    .PurchaseIntoBoxAsync(userId, template.Id, request.PayWithGold, price, slot.Value, NewItemLevel, cancellationToken)
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

    // Same rules as the legacy price check (RakionWorldServ.exe 0x40A6C0): shop 1 = gold only,
    // 2 = cash only, 3 = both, anything else is not sold.
    private static string? RejectionReason(ItemEntity? template, bool payWithGold)
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

        // UNKNOWN: a non-zero power flag makes the legacy check depend on the player's PowerUser state.
        if (template.Power != 0)
        {
            return "PowerUser items are not supported yet";
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
