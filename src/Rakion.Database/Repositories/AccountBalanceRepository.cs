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
}

