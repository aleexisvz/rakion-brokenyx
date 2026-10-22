using Microsoft.Extensions.Logging;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Utilities;
using Rakion.Database.Repositories;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Features.Economy;

/// <summary>
/// Lane A PowerUser card purchase ("PU Card", 0x0034), following the legacy rules
/// (RakionWorldServ.exe 0x40B2C0 and DBCommandInventoryBuyPowerUser).
/// </summary>
public sealed class PowerUserModule : IWorldOpcodeModule
{
    // Observed constants of the legacy server.
    public const int NewCardCashPrice = 8000;       // 0x1F40 at 0x40B38F
    public const int ExtendCardCashPrice = 6000;    // 0x1770 at 0x40B378
    public const long CardDurationMinutes = 30 * 1440;   // "+(30*1440)" in the SQL
    public const long MaxAheadMinutes = 0x3F480;    // 180 days, checked at 0x415EE5
    public const int PointsPerCard = 5;             // "add eax, 5" at 0x415F65

    private readonly ISessionSender _sender;
    private readonly IAccountBalanceRepository _balanceRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PowerUserModule> _logger;

    public PowerUserModule(
        ISessionSender sender,
        IAccountBalanceRepository balanceRepository,
        TimeProvider timeProvider,
        ILogger<PowerUserModule> logger)
    {
        _sender = sender;
        _balanceRepository = balanceRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Register(WorldOpcodeRegistry registry)
    {
        registry.Map(ClientOpcode.InventoryBuyPowerUser, HandleBuyPowerUserAsync);
    }

    private async ValueTask HandleBuyPowerUserAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        IWorldSessionView view = session;
        if (!InventoryBuyPowerUserRequestPacket.TryParse(payload.Span, out var request))
        {
            // The legacy server disconnects on a first byte other than 0 or 1 (error 0x45).
            _logger.LogWarning("[{ConnectionId}] BuyPowerUser rejected: malformed payload ({Length} bytes).", view.ConnectionId, payload.Length);
            return;
        }

        if (view.UserId is not int userId || view.CharacterId is null)
        {
            _logger.LogWarning("[{ConnectionId}] BuyPowerUser rejected: no authenticated user with an active character.", view.ConnectionId);
            return;
        }

        InventoryBuyPowerUserResponsePacket response;
        if (request.UsesCoupon != 0)
        {
            _logger.LogInformation("[{ConnectionId}] BuyPowerUser: coupons are not supported yet.", view.ConnectionId);
            long gold = await _balanceRepository.GetGoldAsync(userId, cancellationToken).ConfigureAwait(false);
            int cash = await _balanceRepository.GetCashAsync(userId, cancellationToken).ConfigureAwait(false);
            response = new InventoryBuyPowerUserResponsePacket(InventoryBuyPowerUserResponsePacket.ResultCouponInvalid, Clamp(gold), Clamp(cash), 0, 0);
        }
        else
        {
            int price = request.Extend ? ExtendCardCashPrice : NewCardCashPrice;
            long now = LegacyGameClock.ToMinutes(_timeProvider.GetLocalNow().DateTime);
            var outcome = await _balanceRepository
                .BuyPowerUserAsync(userId, price, request.Extend, now, CardDurationMinutes, MaxAheadMinutes, PointsPerCard, cancellationToken)
                .ConfigureAwait(false);

            byte result = outcome.Status switch
            {
                PowerUserPurchaseStatus.Bought => InventoryBuyPowerUserResponsePacket.ResultSuccess,
                PowerUserPurchaseStatus.LimitReached => InventoryBuyPowerUserResponsePacket.ResultLimitReached,
                _ => InventoryBuyPowerUserResponsePacket.ResultNotEnoughCash,
            };

            _logger.LogInformation(
                "[{ConnectionId}] User {UserId} BuyPowerUser ({Kind}, {Price} cash): {Status}; expiry {PowerTime}, PowerUser points {Points}, cash {Cash}.",
                view.ConnectionId,
                userId,
                request.Extend ? "extend" : "new",
                price,
                outcome.Status,
                outcome.PowerTime,
                outcome.PowerLevelPoints,
                outcome.Cash);

            response = new InventoryBuyPowerUserResponsePacket(
                result,
                Clamp(outcome.Gold),
                Clamp(outcome.Cash),
                Clamp(outcome.PowerTime),
                (ushort)Math.Clamp(outcome.PowerLevelPoints, 0, ushort.MaxValue));
        }

        await _sender.SendAsync(view, response, cancellationToken).ConfigureAwait(false);
    }

    private static uint Clamp(long value) => (uint)Math.Clamp(value, 0, uint.MaxValue);
}
