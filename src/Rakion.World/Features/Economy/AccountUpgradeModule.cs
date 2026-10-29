using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Paid account upgrades (roadmap 4.7 / 4.8 / 6.7): stat reset (0x1B), extra bag (0x32), potion slot (0x6F) and the
/// Stage purchases (rank clear 0x70, level pass 0x71), with the
/// legacy prices. Discount coupons (items 11000..11999) are refused: their discount table is not in the binary
/// (UNKNOWN).
/// </summary>
public sealed class AccountUpgradeModule : IWorldOpcodeModule
{
    /// <summary>Stat reset cash price by level (RakionWorldServ.exe 0x413FD4, Observed), rounded down to 100.</summary>
    public static int StatResetPrice(byte level) => level > 40 ? 19000 : level > 15 ? 12000 : 7000;

    public const int BagCashPrice = 8000;   // 0x415033, Observed

    /// <summary>Character slot: 12000 cash (RakionWorldServ.exe 0x40B249), at most 6 slots (0x40B25F), Observed.</summary>
    public const int CharacterSlotCashPrice = 12000;
    public const byte MaxCharacterSlots = 6;

    /// <summary>The legacy server answers 3 both at the slot maximum and without enough cash (0x40B26A, 0x415775).</summary>
    public const byte CharacterSlotRefused = 3;
    public const byte MaxBags = 4;          // Derived: the 120-slot box and 30 slots per bag (ShopModule)

    /// <summary>Potion slots 3 -> 4 -> 5 -> 6 are sold as shop items 10008..10010 (0x428E02, Observed).</summary>
    public const int FirstPotionSlotItemId = 10008;
    public const byte BasePotionSlots = 3;
    public const byte MaxPotionSlots = 6;

    /// <summary>
    /// Stage rank clear item by character level (RakionWorldServ.exe 0x429323, Observed): 10..20 -> 10011,
    /// 21..40 -> 10012, 41+ -> 10013; below level 10 the legacy server charges nothing. Prices are the catalog's cash
    /// price, with the legacy values (2900 / 6400 / 9900) when the catalog lacks the item.
    /// </summary>
    public static (int ItemId, int FallbackPrice)? StageRankClearItem(byte level) =>
        level >= 41 ? (10013, 9900) : level >= 21 ? (10012, 6400) : level >= 10 ? (10011, 2900) : null;

    /// <summary>Stage level pass: item 10014 (0x42947C, Observed), 16500 cash, valid 1440 minutes (0x41871E, Observed).</summary>
    public const int StageLevelFreeItemId = 10014;
    public const int StageLevelFreeFallbackPrice = 16500;
    public const int StageLevelFreeMinutes = 1440;

    private readonly ISessionSender _sender;
    private readonly IAccountUpgradeRepository _upgrades;
    private readonly IInventoryRepository _inventory;
    private readonly ILogger<AccountUpgradeModule> _logger;

    public AccountUpgradeModule(
        ISessionSender sender,
        IAccountUpgradeRepository upgrades,
        IInventoryRepository inventory,
        ILogger<AccountUpgradeModule> logger)
    {
        _sender = sender;
        _upgrades = upgrades;
        _inventory = inventory;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.CharacterStateClear, HandleStatResetAsync);
        registry.Map(ClientOpcode.InventoryBuyBag, HandleBuyBagAsync);
        registry.Map(ClientOpcode.InventoryBuyCharacterSlot, HandleBuyCharacterSlotAsync);
        registry.Map(ClientOpcode.InventoryBuyPotionSlot, HandleBuyPotionSlotAsync);
        registry.Map(ClientOpcode.InventoryBuyStageRankClear, HandleBuyStageRankClearAsync);
        registry.Map(ClientOpcode.InventoryBuyStageLevelFree, HandleBuyStageLevelFreeAsync);
    }

    private async ValueTask HandleBuyStageRankClearAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (view.UserId is not int userId || session.ActiveCharacter is not { } character)
        {
            _logger.LogWarning("[{ConnectionId}] BuyStageRankClear rejected: no active character.", view.ConnectionId);
            return;
        }

        int price = StageRankClearItem(character.Level) is { } item ? await CashPriceAsync(item.ItemId, item.FallbackPrice, cancellationToken).ConfigureAwait(false) : 0;
        var outcome = await _upgrades.BuyStageRankClearAsync(userId, character.Id, price, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{ConnectionId}] BuyStageRankClear for '{Character}' (level {Level}, {Price} cash): {Status}, cash {Cash}.", view.ConnectionId, character.Name, character.Level, price, outcome.Status, outcome.Cash);
        await SendStagePurchaseAsync(view, ServerOpcode.InventoryBuyStageRankClear, outcome, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleBuyStageLevelFreeAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (view.UserId is not int userId)
        {
            _logger.LogWarning("[{ConnectionId}] BuyStageLevelFree rejected: not logged in.", view.ConnectionId);
            return;
        }

        int price = await CashPriceAsync(StageLevelFreeItemId, StageLevelFreeFallbackPrice, cancellationToken).ConfigureAwait(false);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var outcome = await _upgrades.BuyStageLevelFreeAsync(userId, price, now, StageLevelFreeMinutes, cancellationToken).ConfigureAwait(false);
        if (outcome.Status == UpgradeStatus.Bought)
        {
            session.StageLevelFreeUntilMinute = now + StageLevelFreeMinutes;
        }

        _logger.LogInformation("[{ConnectionId}] BuyStageLevelFree ({Price} cash): {Status}, cash {Cash}.", view.ConnectionId, price, outcome.Status, outcome.Cash);
        await SendStagePurchaseAsync(view, ServerOpcode.InventoryBuyStageLevelFree, outcome, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<int> CashPriceAsync(int itemId, int fallback, CancellationToken cancellationToken) =>
        await _inventory.GetItemTemplateAsync(itemId, cancellationToken).ConfigureAwait(false) is { CashPrice: > 0 } item ? item.CashPrice : fallback;

    private ValueTask SendStagePurchaseAsync(IWorldSessionView view, ServerOpcode opcode, UpgradeOutcome outcome, CancellationToken cancellationToken)
    {
        byte result = outcome.Status switch
        {
            UpgradeStatus.Bought => StagePurchaseResponsePacket.Success,
            UpgradeStatus.NotEnoughMoney => StagePurchaseResponsePacket.NotEnoughCash,
            UpgradeStatus.AtMaximum => StagePurchaseResponsePacket.StillActive,
            _ => StagePurchaseResponsePacket.Failed,
        };
        var response = new StagePurchaseResponsePacket(opcode, result, (uint)Math.Clamp(outcome.Gold, 0, uint.MaxValue), (uint)Math.Max(outcome.Cash, 0));
        return _sender.SendAsync(view, response, cancellationToken);
    }

    private async ValueTask HandleStatResetAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!CouponRequestPacket.TryParse(ClientOpcode.CharacterStateClear, payload.Span, out var request) ||
            view.UserId is not int userId || session.ActiveCharacter is not { } character)
        {
            _logger.LogWarning("[{ConnectionId}] StateClear rejected: malformed payload or no active character.", view.ConnectionId);
            return;
        }

        if (request.UseCoupon)
        {
            await _sender.SendAsync(view, new CharacterStateClearResponsePacket(CharacterStateClearResponsePacket.CouponRefused, 0, 0, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        int price = StatResetPrice(character.Level);
        var outcome = await _upgrades.ResetStatsAsync(userId, character.Id, price, cancellationToken).ConfigureAwait(false);
        byte result = outcome.Status switch
        {
            StatResetStatus.Reset => CharacterStateClearResponsePacket.Success,
            StatResetStatus.NotEnoughCash => CharacterStateClearResponsePacket.NotEnoughCash,
            _ => CharacterStateClearResponsePacket.NothingToReset,
        };

        if (outcome.Status == StatResetStatus.Reset)
        {
            character.LevelPoint = (byte)Math.Clamp(outcome.LevelPoints, 0, byte.MaxValue);
            character.Hit1 = character.Hit2 = character.Hit3 = character.Hit4 = character.Chit = 0;
            character.Hp = character.Ap = character.AttackSpeed = character.Speed = character.MaxCp = 0;
            if (session.GameInfo is { } gameInfo)
            {
                gameInfo.PowerLevelPoint = outcome.PowerPoints;
            }
        }

        _logger.LogInformation(
            "[{ConnectionId}] StateClear for '{Character}' (level {Level}, {Price} cash): {Status}; cash {Cash}, level points {LevelPoints}, PowerUser points {PowerPoints}.",
            view.ConnectionId,
            character.Name,
            character.Level,
            price,
            outcome.Status,
            outcome.Cash,
            outcome.LevelPoints,
            outcome.PowerPoints);

        var response = new CharacterStateClearResponsePacket(
            result,
            (uint)Math.Max(outcome.Cash, 0),
            (ushort)Math.Clamp(outcome.LevelPoints, 0, ushort.MaxValue),
            (ushort)Math.Clamp(outcome.PowerPoints, 0, ushort.MaxValue));
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleBuyCharacterSlotAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!CouponRequestPacket.TryParse(ClientOpcode.InventoryBuyCharacterSlot, payload.Span, out var request) || view.UserId is not int userId)
        {
            _logger.LogWarning("[{ConnectionId}] BuyCharacterSlot rejected: malformed payload or not logged in.", view.ConnectionId);
            return;
        }

        UpgradeOutcome outcome = request.UseCoupon
            ? new UpgradeOutcome(UpgradeStatus.AtMaximum, 0, 0, session.GameInfo?.SlotCount ?? 3)
            : await _upgrades.BuyCharacterSlotAsync(userId, CharacterSlotCashPrice, MaxCharacterSlots, cancellationToken).ConfigureAwait(false);
        if (outcome.Status == UpgradeStatus.Bought && session.GameInfo is { } gameInfo)
        {
            gameInfo.SlotCount = outcome.Count;
        }

        _logger.LogInformation("[{ConnectionId}] BuyCharacterSlot: {Status}, slots {Slots}, cash {Cash}.", view.ConnectionId, outcome.Status, outcome.Count, outcome.Cash);
        byte result = outcome.Status == UpgradeStatus.Bought ? AccountUpgradeResponsePacket.Success : CharacterSlotRefused;
        var response = new AccountUpgradeResponsePacket(
            ServerOpcode.InventoryBuyCharacterSlot,
            result,
            (uint)Math.Clamp(outcome.Gold, 0, uint.MaxValue),
            (uint)Math.Max(outcome.Cash, 0),
            outcome.Count);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleBuyBagAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!CouponRequestPacket.TryParse(ClientOpcode.InventoryBuyBag, payload.Span, out var request) || view.UserId is not int userId)
        {
            _logger.LogWarning("[{ConnectionId}] BuyBag rejected: malformed payload or not logged in.", view.ConnectionId);
            return;
        }

        UpgradeOutcome outcome = request.UseCoupon
            ? new UpgradeOutcome(UpgradeStatus.AtMaximum, 0, 0, session.GameInfo?.BagCount ?? 1)
            : await _upgrades.BuyBagAsync(userId, BagCashPrice, MaxBags, cancellationToken).ConfigureAwait(false);
        if (outcome.Status == UpgradeStatus.Bought && session.GameInfo is { } gameInfo)
        {
            gameInfo.BagCount = outcome.Count;
        }

        _logger.LogInformation("[{ConnectionId}] BuyBag: {Status}, bags {Bags}, cash {Cash}.", view.ConnectionId, outcome.Status, outcome.Count, outcome.Cash);
        await SendUpgradeAsync(view, ServerOpcode.InventoryBuyBag, outcome, AccountUpgradeResponsePacket.BagNotEnoughCash, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleBuyPotionSlotAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (view.UserId is not int userId || session.ActiveCharacter is not { } character)
        {
            _logger.LogWarning("[{ConnectionId}] BuyPotionSlot rejected: no active character.", view.ConnectionId);
            return;
        }

        // Prices come from the shop catalog rows of the slot items (legacy 0x40A860).
        var prices = new Dictionary<byte, (bool PayWithGold, int Price)>();
        for (byte slots = BasePotionSlots; slots < MaxPotionSlots; slots++)
        {
            if (await _inventory.GetItemTemplateAsync(FirstPotionSlotItemId + slots - BasePotionSlots, cancellationToken).ConfigureAwait(false) is { } item)
            {
                prices[slots] = item.GoldPrice > 0 ? (true, item.GoldPrice) : (false, item.CashPrice);
            }
        }

        var outcome = await _upgrades
            .BuyPotionSlotAsync(userId, character.Id, slots => prices.TryGetValue(slots, out var price) ? price : null, cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Status == UpgradeStatus.Bought)
        {
            character.PotionSlotCount = outcome.Count;
        }

        _logger.LogInformation("[{ConnectionId}] BuyPotionSlot for '{Character}': {Status}, slots {Slots}, gold {Gold}, cash {Cash}.", view.ConnectionId, character.Name, outcome.Status, outcome.Count, outcome.Gold, outcome.Cash);
        await SendUpgradeAsync(view, ServerOpcode.InventoryBuyPotionSlot, outcome, AccountUpgradeResponsePacket.PotionSlotNotEnoughMoney, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask SendUpgradeAsync(IWorldSessionView view, ServerOpcode opcode, UpgradeOutcome outcome, byte notEnoughMoney, CancellationToken cancellationToken)
    {
        byte result = outcome.Status switch
        {
            UpgradeStatus.Bought => AccountUpgradeResponsePacket.Success,
            UpgradeStatus.NotEnoughMoney => notEnoughMoney,
            _ => AccountUpgradeResponsePacket.AtMaximum,
        };
        var response = new AccountUpgradeResponsePacket(
            opcode,
            result,
            (uint)Math.Clamp(outcome.Gold, 0, uint.MaxValue),
            (uint)Math.Max(outcome.Cash, 0),
            outcome.Count);
        return _sender.SendAsync(view, response, cancellationToken);
    }
}
