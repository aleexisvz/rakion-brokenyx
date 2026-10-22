using System.Data.Common;
using Dapper;
using Rakion.Database.Connection;
using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Dapper implementation of user account persistence.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<UserEntity?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, username AS Username, password_hash AS PasswordHash, 
                   email AS Email, country AS Country, no_country_update AS NoCountryUpdate, 
                   authority AS Authority, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM users
            WHERE username = @Username
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<UserEntity>(new CommandDefinition(sql, new { Username = username }, cancellationToken: ct));
    }

    public async Task<UserEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, username AS Username, password_hash AS PasswordHash, 
                   email AS Email, country AS Country, no_country_update AS NoCountryUpdate, 
                   authority AS Authority, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM users
            WHERE id = @Id
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<UserEntity>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<int> CreateUserAsync(UserEntity user, long initialGold = 10_000, int initialCash = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Username);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            const string insertUserSql = """
                INSERT INTO users (username, password_hash, email, country, no_country_update, authority, created_at)
                VALUES (@Username, @PasswordHash, @Email, @Country, @NoCountryUpdate, @Authority, @CreatedAt);
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                insertUserSql,
                new
                {
                    user.Username,
                    user.PasswordHash,
                    user.Email,
                    user.Country,
                    user.NoCountryUpdate,
                    user.Authority,
                    CreatedAt = DateTime.UtcNow
                },
                transaction: tx,
                cancellationToken: ct));

            int userId = await GetLastInsertedIdAsync(connection, tx, ct);

            const string insertGameInfoSql = """
                INSERT INTO user_game_info (user_id, username, gold, tutorial_completed, stage_level_free, slot_count, bag_count, is_banned, country, created_at)
                VALUES (@UserId, @Username, @Gold, 0, 0, 3, 1, 0, @Country, @CreatedAt);
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                insertGameInfoSql,
                new
                {
                    UserId = userId,
                    user.Username,
                    Gold = initialGold,
                    user.Country,
                    CreatedAt = DateTime.UtcNow
                },
                transaction: tx,
                cancellationToken: ct));

            const string insertCashSql = """
                INSERT INTO cash_balances (user_id, username, cash_amount, updated_at)
                VALUES (@UserId, @Username, @CashAmount, @UpdatedAt);
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                insertCashSql,
                new
                {
                    UserId = userId,
                    user.Username,
                    CashAmount = initialCash,
                    UpdatedAt = DateTime.UtcNow
                },
                transaction: tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            user.Id = userId;
            return userId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task UpdatePasswordHashAsync(int userId, string newHash, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newHash);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE users
            SET password_hash = @PasswordHash, updated_at = @UpdatedAt
            WHERE id = @UserId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, PasswordHash = newHash, UpdatedAt = DateTime.UtcNow },
            cancellationToken: ct));
    }

    public async Task<UserGameInfoEntity?> GetGameInfoByUserIdAsync(int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, user_id AS UserId, username AS Username, gold AS Gold, 
                   tutorial_completed AS TutorialCompleted, stage_level_free AS StageLevelFree, 
                   slot_count AS SlotCount, bag_count AS BagCount, is_banned AS IsBanned, 
                   ban_date AS BanDate, ban_reason AS BanReason, clan_id AS ClanId, 
                   clan_point AS ClanPoint, clan_rank AS ClanRank, clan_grade AS ClanGrade, 
                   buddy_name AS BuddyName, power_time AS PowerTime, power_level_point AS PowerLevelPoint, 
                   power_time_date AS PowerTimeDate, active_character_name AS ActiveCharacterName, 
                   rank_grade AS RankGrade, country AS Country, last_connect AS LastConnect, 
                   created_at AS CreatedAt
            FROM user_game_info
            WHERE user_id = @UserId
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<UserGameInfoEntity>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
    }

    public async Task UpdateLastConnectAsync(int userId, DateTime connectTime, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE user_game_info
            SET last_connect = @LastConnect
            WHERE user_id = @UserId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, LastConnect = connectTime },
            cancellationToken: ct));
    }

    public async Task SetTutorialCompletedAsync(int userId, bool completed = true, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE user_game_info
            SET tutorial_completed = @Completed
            WHERE user_id = @UserId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { UserId = userId, Completed = completed ? 1 : 0 },
            cancellationToken: ct));
    }

    private static async Task<int> GetLastInsertedIdAsync(DbConnection conn, DbTransaction? tx, CancellationToken ct)
    {
        string query = conn.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "SELECT last_insert_rowid();"
            : "SELECT LAST_INSERT_ID();";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(query, transaction: tx, cancellationToken: ct));
    }
}

