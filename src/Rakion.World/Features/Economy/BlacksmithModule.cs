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
/// Lane A blacksmith (roadmap 4.5): reinforces a box item with a stone, following the legacy checks
/// (RakionWorldServ.exe 0x40C310). The odds are configurable placeholders (see <see cref="EnchantOdds"/>).
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

    private readonly ISessionSender _sender;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly Func<int, EnchantOdds> _oddsForLevel;
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
            level => EnchantOdds.DefaultFor(level).WithDowngradeScale(options.Value.Rewards.EnchantDowngradeScale),
            Random.Shared.Next,
            logger)
    {
    }

    public BlacksmithModule(
        ISessionSender sender,
        IInventoryRepository inventoryRepository,
        Func<int, EnchantOdds> oddsForLevel,
        Func<int, int> roll,
        ILogger<BlacksmithModule> logger)
    {
        _sender = sender;
        _inventoryRepository = inventoryRepository;
        _oddsForLevel = oddsForLevel;
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

        byte? refusal = null;
        if (target is null || target.ItemId >= FirstNonEquipmentItemId || target.Level >= MaxLevel)
        {
            refusal = EnchantReinforceResponsePacket.NotReinforceable;
        }
        else if (stone is null || stone.ItemId is < FirstStoneId or > LastStoneId || request.MaterialSlots.Count > 0)
        {
            // UNKNOWN: effect of the helper materials (ids 14001..14003) on the odds; refused rather than consumed.
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

        var odds = _oddsForLevel(target!.Level);
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
            .ReinforceInBoxAsync(userId, request.TargetSlot, target.ItemId, request.StoneSlot, stone!.ItemId, delta, cancellationToken)
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
}
