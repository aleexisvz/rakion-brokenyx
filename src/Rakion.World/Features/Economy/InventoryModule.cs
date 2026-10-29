using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Lane A inventory opcodes (roadmap 4.1). Bound to World through <see cref="IWorldOpcodeModule"/>
/// and reads session state only through <see cref="IWorldSessionView"/>.
/// </summary>
public sealed class InventoryModule : IWorldOpcodeModule
{
    private readonly ISessionSender _sender;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InventoryModule> _logger;

    public InventoryModule(
        ISessionSender sender,
        IInventoryRepository inventoryRepository,
        TimeProvider timeProvider,
        ILogger<InventoryModule> logger)
    {
        _sender = sender;
        _inventoryRepository = inventoryRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.InventoryEnter, HandleInventoryEnterAsync);
        registry.Map(ClientOpcode.InventoryLeave, HandleInventoryLeaveAsync);
        registry.Map(ClientOpcode.InventoryMove, HandleInventoryMoveAsync);
        registry.Map(ClientOpcode.FieldGamePotion, HandleFieldGamePotionAsync);
        registry.Map(ClientOpcode.InventoryStackPotion, HandleStackPotionAsync);
    }

    /// <summary>
    /// Stacks two potions of the account box (0x73). Legacy rules (RakionWorldServ.exe 0x421AC1 and 0x40C140, Observed):
    /// both slots below 120, both items potions (12000..12999) with the same catalog hit4 (entry +0x12; that it is the
    /// potion kind is Inferred); the quantities add up without a cap.
    /// </summary>
    private async ValueTask HandleStackPotionAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!InventoryStackPotionRequestPacket.TryParse(payload.Span, out var request) || view.UserId is not int userId ||
            request.SourceSlot >= AccountBoxBlock.SlotCount || request.TargetSlot >= AccountBoxBlock.SlotCount)
        {
            _logger.LogWarning("[{ConnectionId}] StackPotion rejected: malformed or not logged in.", view.ConnectionId);
            return;
        }

        bool stacked = await CanStackAsync(userId, request, cancellationToken).ConfigureAwait(false) &&
            await _inventoryRepository.StackBoxItemsAsync(userId, request.SourceSlot, request.TargetSlot, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{ConnectionId}] StackPotion box {Source} -> {Target}: {Stacked}.", view.ConnectionId, request.SourceSlot, request.TargetSlot, stacked);
        var response = new InventoryStackPotionResponsePacket(stacked ? InventoryStackPotionResponsePacket.Stacked : InventoryStackPotionResponsePacket.Refused);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> CanStackAsync(int userId, InventoryStackPotionRequestPacket request, CancellationToken cancellationToken)
    {
        var (source, target) = await _inventoryRepository.GetBoxItemIdsAsync(userId, request.SourceSlot, request.TargetSlot, cancellationToken).ConfigureAwait(false);
        if (source is not (>= 12000 and <= 12999) || target is not (>= 12000 and <= 12999))
        {
            return false;
        }

        var sourceItem = await _inventoryRepository.GetItemTemplateAsync(source.Value, cancellationToken).ConfigureAwait(false);
        var targetItem = await _inventoryRepository.GetItemTemplateAsync(target.Value, cancellationToken).ConfigureAwait(false);
        return sourceItem is not null && targetItem is not null && sourceItem.Hit4 == targetItem.Hit4;
    }

    /// <summary>
    /// Potions drunk in a match (0x6E). Only potions (12000..12999) during a match; the legacy server logs a refused
    /// use as a cheat (0x41EB20 code 0xD2) and here it is only logged. The legacy one-use-per-slot flag outside Stage
    /// (0x40E608) is not applied: when it is cleared is Unknown.
    /// </summary>
    private async ValueTask HandleFieldGamePotionAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!FieldGamePotionRequestPacket.TryParse(payload.Span, out var request) || view.UserId is not int userId ||
            !session.IsInField || request.ItemId is < 12000 or > 12999 || request.BoxSlot >= AccountBoxBlock.SlotCount)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGamePotion rejected: malformed, not in a room or not a potion.", view.ConnectionId);
            return;
        }

        long? left = await _inventoryRepository.ConsumePotionAsync(userId, request.BoxSlot, request.ItemId, cancellationToken).ConfigureAwait(false);
        if (left is null)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGamePotion refused: box slot {Slot} has no potion {ItemId} left.", view.ConnectionId, request.BoxSlot, request.ItemId);
            return;
        }

        _logger.LogInformation("[{ConnectionId}] Potion {ItemId} drunk from box slot {Slot}: {Left} left.", view.ConnectionId, request.ItemId, request.BoxSlot, left);
    }

    private async ValueTask HandleInventoryEnterAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        InventoryEnterRequestPacket.TryParse(payload.Span, out _);

        // The legacy server checks that the character belongs to the account and otherwise sends no
        // 0x002C reply (it reports error code 0xB8 instead, whose semantics are Unknown). Here character
        // select already enforces ownership, so a missing character is the only rejection.
        if (view.UserId is null || view.CharacterId is null)
        {
            _logger.LogWarning(
                "[{ConnectionId}] InventoryEnter rejected: no authenticated user with an active character.",
                view.ConnectionId);
            return;
        }

        uint serverTimeMinutes = LegacyGameClock.ToMinutes(_timeProvider.GetLocalNow().DateTime);
        var response = new InventoryEnterResponsePacket(InventoryEnterResponsePacket.ResultSuccess, serverTimeMinutes);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[{ConnectionId}] InventoryEnter for '{CharacterName}': accepted (server time {ServerTimeMinutes} min).",
            view.ConnectionId,
            view.CharacterName,
            serverTimeMinutes);
    }

    private async ValueTask HandleInventoryLeaveAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        InventoryLeaveRequestPacket.TryParse(payload.Span, out _);

        // The legacy server also persists inventory state on leave; here every change is saved when it
        // happens (buy, sell, move), so leaving only needs the acknowledgement the client waits for.
        var response = new InventoryLeaveResponsePacket(InventoryLeaveResponsePacket.ResultSuccess);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("[{ConnectionId}] InventoryLeave acknowledged.", view.ConnectionId);
    }

    private async ValueTask HandleInventoryMoveAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!InventoryMoveRequestPacket.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[{ConnectionId}] InventoryMove rejected: malformed payload ({Length} bytes).", view.ConnectionId, payload.Length);
            return;
        }

        if (view.UserId is not int userId || view.CharacterId is not int characterId)
        {
            _logger.LogWarning("[{ConnectionId}] InventoryMove rejected: no authenticated user with an active character.", view.ConnectionId);
            return;
        }

        byte result;
        if (!request.IsInRange)
        {
            // The legacy server disconnects here (error 0x3E); answering keeps the client from hanging.
            _logger.LogWarning("[{ConnectionId}] InventoryMove rejected: slot out of range ({Request}).", view.ConnectionId, request);
            result = InventoryMoveResponsePacket.ResultNotAllowed;
        }
        else
        {
            // UNKNOWN: the legacy server checks whether the item may go into an equipment slot
            // (RakionWorldServ.exe 0x40BC10, result 4); its rules are not observed yet, so the client's own
            // drag-and-drop restrictions are relied on for now.
            bool moved = await _inventoryRepository
                .SwapSlotsAsync(
                    userId,
                    OwnerOf(request.SourceLocation, characterId),
                    request.SourceSlot,
                    OwnerOf(request.DestinationLocation, characterId),
                    request.DestinationSlot,
                    cancellationToken)
                .ConfigureAwait(false);
            result = moved ? InventoryMoveResponsePacket.ResultSuccess : InventoryMoveResponsePacket.ResultSourceEmpty;
        }

        var items = await _inventoryRepository.GetUserItemsAsync(userId, characterId: null, cancellationToken).ConfigureAwait(false);
        var response = new InventoryMoveResponsePacket(
            result,
            StateOf(items, request.SourceLocation, request.SourceSlot, characterId),
            StateOf(items, request.DestinationLocation, request.DestinationSlot, characterId));
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[{ConnectionId}] InventoryMove {SourceSlot} -> {DestinationSlot}: result {Result}.",
            view.ConnectionId,
            request.SourceSlot,
            request.DestinationSlot,
            result);
    }

    // Box rows belong to character 0; equipment rows to the character currently selected.
    private static int OwnerOf(InventoryLocation location, int characterId) =>
        location == InventoryLocation.AccountBox ? 0 : characterId;

    private static InventorySlotState StateOf(
        IReadOnlyList<Database.Entities.UserItemEntity> items,
        InventoryLocation location,
        byte slot,
        int characterId)
    {
        int owner = OwnerOf(location, characterId);
        foreach (var item in items)
        {
            if (item.CharacterId == owner && item.SlotIndex == slot && item.ItemId is > 0 and <= ushort.MaxValue)
            {
                return new InventorySlotState(location, slot, (ushort)item.ItemId, item.Level, (uint)Math.Clamp(item.Exp, 0, uint.MaxValue));
            }
        }

        return new InventorySlotState(location, slot, 0, 0, 0);
    }
}

