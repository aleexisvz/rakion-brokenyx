using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InventoryModule> _logger;

    public InventoryModule(ISessionSender sender, TimeProvider timeProvider, ILogger<InventoryModule> logger)
    {
        _sender = sender;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.InventoryEnter, HandleInventoryEnterAsync);
        registry.Map(ClientOpcode.InventoryLeave, HandleInventoryLeaveAsync);
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

    private ValueTask HandleInventoryLeaveAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        _logger.LogDebug("[{ConnectionId}] InventoryLeave received.", view.ConnectionId);
        return ValueTask.CompletedTask;
    }
}
