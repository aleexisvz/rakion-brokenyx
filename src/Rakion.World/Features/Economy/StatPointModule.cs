using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Lane A stat point allocation (roadmap 4.4), following the legacy rules (RakionWorldServ.exe 0x40B3D0).
/// </summary>
public sealed class StatPointModule : IWorldOpcodeModule
{
    // Inferred: the legacy cap comes from a per-class table not yet observed; the v258 inventory screen shows
    // every stat as "(n/50)".
    public const int MaxPointsPerStat = 50;

    private readonly ISessionSender _sender;
    private readonly ICharacterRepository _characterRepository;
    private readonly ILogger<StatPointModule> _logger;

    public StatPointModule(ISessionSender sender, ICharacterRepository characterRepository, ILogger<StatPointModule> logger)
    {
        _sender = sender;
        _characterRepository = characterRepository;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.InventoryAllocationPoint, HandleAllocationPointAsync);
    }

    private async ValueTask HandleAllocationPointAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!InventoryAllocationPointRequestPacket.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[{ConnectionId}] AllocationPoint rejected: malformed payload ({Length} bytes).", view.ConnectionId, payload.Length);
            return;
        }

        if (view.UserId is not int userId || view.CharacterId is not int characterId)
        {
            _logger.LogWarning("[{ConnectionId}] AllocationPoint rejected: no authenticated user with an active character.", view.ConnectionId);
            return;
        }

        InventoryAllocationPointResponsePacket response;
        if (!Enum.IsDefined(request.Stat))
        {
            _logger.LogWarning("[{ConnectionId}] AllocationPoint rejected: unknown stat {Stat}.", view.ConnectionId, (byte)request.Stat);
            response = new InventoryAllocationPointResponsePacket(InventoryAllocationPointResponsePacket.ResultUnknownStat, 0, 0, request.Stat, 0);
        }
        else
        {
            var outcome = await _characterRepository
                .AllocateStatPointAsync(userId, characterId, request.Stat, MaxPointsPerStat, cancellationToken)
                .ConfigureAwait(false);
            byte result = outcome.Status switch
            {
                StatAllocationStatus.Allocated => InventoryAllocationPointResponsePacket.ResultSuccess,
                StatAllocationStatus.AtMaximum => InventoryAllocationPointResponsePacket.ResultStatAtMaximum,
                _ => InventoryAllocationPointResponsePacket.ResultNoPoints,
            };

            _logger.LogInformation(
                "[{ConnectionId}] AllocationPoint {Stat} for character {CharacterId}: {Status} (value {Value}, level points {LevelPoints}, PowerUser points {PowerPoints}).",
                view.ConnectionId,
                request.Stat,
                characterId,
                outcome.Status,
                outcome.NewValue,
                outcome.LevelPoints,
                outcome.PowerPoints);

            response = new InventoryAllocationPointResponsePacket(
                result,
                (ushort)Math.Clamp(outcome.LevelPoints, 0, ushort.MaxValue),
                (ushort)Math.Clamp(outcome.PowerPoints, 0, ushort.MaxValue),
                request.Stat,
                (ushort)Math.Clamp(outcome.NewValue, 0, ushort.MaxValue));
        }

        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }
}
