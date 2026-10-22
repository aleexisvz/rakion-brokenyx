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

