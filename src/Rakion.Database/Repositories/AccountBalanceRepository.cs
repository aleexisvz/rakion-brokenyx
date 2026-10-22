using Dapper;
using Rakion.Database.Connection;

namespace Rakion.Database.Repositories;

/// <summary>
/// Dapper implementation of transactional gold and cash balance management.
/// Uses atomic conditional updates (e.g. `WHERE gold >= @Amount`) to prevent concurrency race conditions
/// and duplication exploits under high load.
/// </summary>
public sealed class AccountBalanceRepository : IAccountBalanceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AccountBalanceRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<long> GetGoldAsync(int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT gold
            FROM user_game_info
            WHERE user_id = @UserId
            LIMIT 1;
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
    }

    public async Task<int> GetCashAsync(int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT cash_amount
            FROM cash_balances
            WHERE user_id = @UserId
            LIMIT 1;
            """;

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
    }

    public async Task<bool> TryDeductGoldAsync(int userId, long amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Deduction amount must be strictly positive.");
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE user_game_info
            SET gold = gold - @Amount
            WHERE user_id = @UserId AND gold >= @Amount;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId, Amount = amount }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> AddGoldAsync(int userId, long amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit amount must be strictly positive.");
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE user_game_info
            SET gold = gold + @Amount
            WHERE user_id = @UserId;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId, Amount = amount }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> TryDeductCashAsync(int userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Deduction amount must be strictly positive.");
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE cash_balances
            SET cash_amount = cash_amount - @Amount, updated_at = @UpdatedAt
            WHERE user_id = @UserId AND cash_amount >= @Amount;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, Amount = amount, UpdatedAt = DateTime.UtcNow },
            cancellationToken: ct));

        return affected > 0;
    }

    public async Task<bool> AddCashAsync(int userId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Deposit amount must be strictly positive.");
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE cash_balances
            SET cash_amount = cash_amount + @Amount, updated_at = @UpdatedAt
            WHERE user_id = @UserId;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, Amount = amount, UpdatedAt = DateTime.UtcNow },
            cancellationToken: ct));

        return affected > 0;
    }

    public async Task<PowerUserPurchaseOutcome> BuyPowerUserAsync(
        int userId,
        int cashPrice,
        bool extend,
        long nowMinutes,
        long durationMinutes,
        long maxAheadMinutes,
        int pointsGained,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cashPrice);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            var state = await connection.QuerySingleAsync<PowerUserRow>(new CommandDefinition(
                """
                SELECT g.gold AS Gold, g.power_time AS PowerTime, g.power_level_point AS PowerLevelPoints,
                       COALESCE((SELECT c.cash_amount FROM cash_balances c WHERE c.user_id = g.user_id), 0) AS Cash
                FROM user_game_info g
                WHERE g.user_id = @UserId;
                """,
                new { UserId = userId },
                tx,
                cancellationToken: ct));

            if (state.PowerTime > nowMinutes + maxAheadMinutes)
            {
                await tx.RollbackAsync(ct);
                return new PowerUserPurchaseOutcome(PowerUserPurchaseStatus.LimitReached, state.Gold, state.Cash, state.PowerTime, state.PowerLevelPoints);
            }

            int charged = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE cash_balances SET cash_amount = cash_amount - @Price, updated_at = @Now WHERE user_id = @UserId AND cash_amount >= @Price;",
                new { UserId = userId, Price = cashPrice, Now = DateTime.UtcNow },
                tx,
                cancellationToken: ct));
            if (charged == 0)
            {
                await tx.RollbackAsync(ct);
                return new PowerUserPurchaseOutcome(PowerUserPurchaseStatus.NotEnoughCash, state.Gold, state.Cash, state.PowerTime, state.PowerLevelPoints);
            }

            // Extending an expired card starts from now, so the new expiry is never in the past.
            long powerTime = (extend ? Math.Max(state.PowerTime, nowMinutes) : nowMinutes) + durationMinutes;
            int points = state.PowerLevelPoints + pointsGained;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_game_info SET power_time = @PowerTime, power_level_point = @Points WHERE user_id = @UserId;",
                new { UserId = userId, PowerTime = powerTime, Points = points },
                tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new PowerUserPurchaseOutcome(PowerUserPurchaseStatus.Bought, state.Gold, state.Cash - cashPrice, powerTime, points);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private sealed class PowerUserRow
    {
        public long Gold { get; set; }
        public int Cash { get; set; }
        public long PowerTime { get; set; }
        public int PowerLevelPoints { get; set; }
    }
}
