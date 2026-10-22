using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
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
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ILogger<InventoryModule> _logger;

    public InventoryModule(IInventoryRepository inventoryRepository, ILogger<InventoryModule> logger)
    {
        _inventoryRepository = inventoryRepository;
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

        if (view.UserId is not int userId || view.CharacterId is null)
        {
            _logger.LogWarning(
                "[{ConnectionId}] InventoryEnter rejected: no authenticated user with an active character.",
                view.ConnectionId);
            return;
        }

        // Inferred: inventory is account-wide (CharacterId 0 = shared), so every row of the user is loaded.
        var items = await _inventoryRepository.GetUserItemsAsync(userId, characterId: null, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[{ConnectionId}] InventoryEnter for '{CharacterName}': {ItemCount} items loaded.",
            view.ConnectionId,
            view.CharacterName,
            items.Count);

        // UNKNOWN: layout of the server's inventory list response to 0x002C has not been observed yet,
        // so no reply is sent. It must be read from the client before it is implemented.
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
