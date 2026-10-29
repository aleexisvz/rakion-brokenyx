using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Database.Repositories;
using Rakion.World.Configuration;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Lane A blacksmith (roadmap 4.5): reinforces a box item with a stone and up to 3 catalysts, following the legacy
/// checks and odds (RakionWorldServ.exe 0x40C310, see <see cref="EnchantOdds.Legacy"/>).
/// </summary>
public sealed class BlacksmithModule : IWorldOpcodeModule
{
    // Observed limits of the legacy check.
    public const int FirstNonEquipmentItemId = 8000;   // cmp 0x1F40 at 0x40C33C
    public const int MaxLevel = 15;                     // cmp 0xF at 0x40C3D3
    public const int FirstStoneId = 13001;              // 0x32C9..0x32CD
    public const int LastStoneId = 13005;
    public const int LowLevelStoneId = 13001;           // only below +5 (0x40C3CC)
    public const int HighLevelStoneId = 13004;          // only from +10 (0x40C3F5)

    // Catalysts counted by the legacy check (0x40C392..0x40C3AE): anything else as a material is refused with 7.
    public const int DestroyGuardId = 14001;            // "Decrease the probability for item destroyed"
    public const int DownGuardId = 14002;               // "Decrease the probability for item level down"
    public const int CharmId = 14003;                   // "Increase the probability during item upgrade"

    private readonly ISessionSender _sender;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly Func<EnchantAttempt, EnchantOdds> _oddsFor;
    private readonly Func<int, int> _roll;
    private readonly ILogger<BlacksmithModule> _logger;

    public BlacksmithModule(
        ISessionSender sender,
        IInventoryRepository inventoryRepository,
        IOptions<WorldOptions> options,
        ILogger<BlacksmithModule> logger)
        : this(
            sender,
            inventoryRepository,
            attempt => EnchantOdds.Legacy(attempt.Level, attempt.StoneId, attempt.DestroyGuards, attempt.DownGuards, attempt.Charms)
                .WithDowngradeScale(options.Value.Rewards.EnchantDowngradeScale),
            Random.Shared.Next,
            logger)
    {
    }

    public BlacksmithModule(
        ISessionSender sender,
        IInventoryRepository inventoryRepository,
        Func<EnchantAttempt, EnchantOdds> oddsFor,
        Func<int, int> roll,
        ILogger<BlacksmithModule> logger)
    {
        _sender = sender;
        _inventoryRepository = inventoryRepository;
        _oddsFor = oddsFor;
        _roll = roll;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.EnchantReinforce, HandleReinforceAsync);
    }

    private async ValueTask HandleReinforceAsync(WorldSession session, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!EnchantReinforceRequestPacket.TryParse(payload.Span, out var request))
        {
            // The legacy server disconnects when more than 3 materials are sent (error 0xE4).
            _logger.LogWarning("[{ConnectionId}] EnchantReinforce rejected: malformed payload ({Length} bytes).", view.ConnectionId, payload.Length);
            return;
        }

        if (view.UserId is not int userId || view.CharacterId is null)
        {
            _logger.LogWarning("[{ConnectionId}] EnchantReinforce rejected: no authenticated user with an active character.", view.ConnectionId);
            return;
        }

        byte result = await ReinforceAsync(userId, request, view, cancellationToken).ConfigureAwait(false);
        await _sender
            .SendAsync(view, new EnchantReinforceResponsePacket(result, request.TargetSlot, request.StoneSlot, request.MaterialSlots), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<byte> ReinforceAsync(int userId, EnchantReinforceRequestPacket request, IWorldSessionView view, CancellationToken cancellationToken)
    {
        var box = await _inventoryRepository.GetUserItemsAsync(userId, characterId: 0, cancellationToken).ConfigureAwait(false);
        var target = box.FirstOrDefault(i => i.SlotIndex == request.TargetSlot);
        var stone = box.FirstOrDefault(i => i.SlotIndex == request.StoneSlot);
        var materials = Catalysts(box, request);

        byte? refusal = null;
        if (target is null || target.ItemId >= FirstNonEquipmentItemId || target.Level >= MaxLevel)
        {
            refusal = EnchantReinforceResponsePacket.NotReinforceable;
        }
        else if (stone is null || stone.ItemId is < FirstStoneId or > LastStoneId || materials is null)
        {
            refusal = EnchantReinforceResponsePacket.InvalidStoneOrMaterial;
        }
        else if ((stone.ItemId == LowLevelStoneId && target.Level >= 5) || (stone.ItemId == HighLevelStoneId && target.Level < 10))
        {
            refusal = EnchantReinforceResponsePacket.StoneNotForThisLevel;
        }

        if (refusal is not null)
        {
            _logger.LogInformation(
                "[{ConnectionId}] EnchantReinforce slot {Target} with slot {Stone} refused: {Result}.",
                view.ConnectionId,
                request.TargetSlot,
                request.StoneSlot,
                refusal.Value);
            return refusal.Value;
        }

        int Count(int itemId) => materials!.Count(m => m.ItemId == itemId);
        var odds = _oddsFor(new EnchantAttempt(target!.Level, stone!.ItemId, Count(DestroyGuardId), Count(DownGuardId), Count(CharmId)));
        byte outcome = odds.Pick(_roll(Math.Max(odds.Total, 1)));
        int delta = outcome switch
        {
            EnchantReinforceResponsePacket.LevelUp => 1,
            EnchantReinforceResponsePacket.LevelDown1 => -1,
            EnchantReinforceResponsePacket.LevelDown2 => -2,
            EnchantReinforceResponsePacket.LevelDown3 => -3,
            _ => 0,
        };

        byte? newLevel = await _inventoryRepository
            .ReinforceInBoxAsync(userId, request.TargetSlot, target.ItemId, request.StoneSlot, stone.ItemId, delta, cancellationToken, materials)
            .ConfigureAwait(false);
        if (newLevel is null)
        {
            return EnchantReinforceResponsePacket.NotReinforceable;
        }

        _logger.LogInformation(
            "[{ConnectionId}] User {UserId} reinforced item {ItemId} (slot {Target}) with stone {StoneId}: outcome {Outcome}, level {Old} -> {New}.",
            view.ConnectionId,
            userId,
            target.ItemId,
            request.TargetSlot,
            stone.ItemId,
            outcome,
            target.Level,
            newLevel.Value);
        return outcome;
    }

    /// <summary>The catalysts of the request, or null when a material slot is not a catalyst (or repeats a slot).</summary>
    private static List<(byte Slot, int ItemId)>? Catalysts(IReadOnlyList<Rakion.Database.Entities.UserItemEntity> box, EnchantReinforceRequestPacket request)
    {
        var materials = new List<(byte Slot, int ItemId)>();
        foreach (byte slot in request.MaterialSlots)
        {
            var item = box.FirstOrDefault(i => i.SlotIndex == slot);
            if (item is null || item.ItemId is not (DestroyGuardId or DownGuardId or CharmId) ||
                slot == request.TargetSlot || slot == request.StoneSlot || materials.Any(m => m.Slot == slot))
            {
                return null;
            }

            materials.Add((slot, item.ItemId));
        }

        return materials;
    }
}
