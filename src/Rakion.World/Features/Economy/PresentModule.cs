using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Present box (roadmap 4.6): peek the oldest present (0x6B), take it into an empty account box slot (0x6C) or throw it
/// away (0x6D). Presents are queued by GMs (/present) or, later, the website. The random present the legacy cash shop
/// added to purchases (CWorld::GetRandomPresentIndex) is not done: its table is Unknown.
/// </summary>
public sealed class PresentModule : IWorldOpcodeModule
{
    private readonly ISessionSender _sender;
    private readonly IPresentRepository _presents;
    private readonly IInventoryRepository _inventory;
    private readonly ILogger<PresentModule> _logger;

    public PresentModule(ISessionSender sender, IPresentRepository presents, IInventoryRepository inventory, ILogger<PresentModule> logger)
    {
        _sender = sender;
        _presents = presents;
        _inventory = inventory;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.PresentPeek, HandlePeekAsync);
        registry.Map(ClientOpcode.PresentAccept, HandleAcceptAsync);
        registry.Map(ClientOpcode.PresentDispose, HandleDisposeAsync);
    }

    private async ValueTask HandlePeekAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (view.UserId is not int userId)
        {
            return;
        }

        var present = await _presents.PeekAsync(userId, cancellationToken).ConfigureAwait(false);
        var response = present is null
            ? new PresentPeekResponsePacket(PresentPeekResponsePacket.Empty)
            : new PresentPeekResponsePacket(PresentPeekResponsePacket.Found, (uint)present.Id, (uint)present.ItemId);
        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleAcceptAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!PresentAcceptRequestPacket.TryParse(payload.Span, out var request) || view.UserId is not int userId ||
            request.BoxSlot >= AccountBoxBlock.SlotCount)
        {
            _logger.LogWarning("[{ConnectionId}] PresentAccept rejected: malformed, not logged in or bad slot.", view.ConnectionId);
            return;
        }

        // Potions arrive with their pack size, like a shop purchase (catalog hit2 kept in exp).
        var present = await _presents.GetAsync(userId, (int)request.PendingId, cancellationToken).ConfigureAwait(false);
        long exp = 0;
        if (present is { ItemId: >= 12000 and <= 12999 } &&
            await _inventory.GetItemTemplateAsync(present.ItemId, cancellationToken).ConfigureAwait(false) is { } template)
        {
            exp = template.Hit2;
        }

        var status = await _presents.AcceptAsync(userId, (int)request.PendingId, (byte)request.BoxSlot, exp, cancellationToken).ConfigureAwait(false);
        byte result = status switch
        {
            PresentAcceptStatus.Accepted => PresentAcceptResponsePacket.Accepted,
            PresentAcceptStatus.SlotTaken => PresentAcceptResponsePacket.SlotTaken,
            _ => PresentAcceptResponsePacket.NotFound,
        };
        _logger.LogInformation("[{ConnectionId}] PresentAccept {PendingId} into box slot {Slot}: {Status}.", view.ConnectionId, request.PendingId, request.BoxSlot, status);
        await _sender.SendAsync(view, new PresentAcceptResponsePacket(result), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleDisposeAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!PresentDisposeRequestPacket.TryParse(payload.Span, out var request) || view.UserId is not int userId)
        {
            return;
        }

        bool disposed = await _presents.DisposeAsync(userId, (int)request.PendingId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{ConnectionId}] PresentDispose {PendingId}: {Disposed}.", view.ConnectionId, request.PendingId, disposed);
        await _sender.SendAsync(view, new PresentDisposeResponsePacket(disposed ? (byte)0 : (byte)1), cancellationToken).ConfigureAwait(false);
    }
}
