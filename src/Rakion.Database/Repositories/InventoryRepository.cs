using System.Data.Common;
using Dapper;
using Rakion.Database.Connection;
using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Dapper implementation of item template catalog and player inventory management.
/// </summary>
public sealed class InventoryRepository : IInventoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public InventoryRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<ItemEntity?> GetItemTemplateAsync(int itemId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, type_id AS Type, class_id AS Class, required_level AS RequiredLevel, 
                   shop_type AS Shop, gold_price AS GoldPrice, cash_price AS CashPrice, 
                   hit1 AS Hit1, hit2 AS Hit2, hit3 AS Hit3, hit4 AS Hit4, 
                   chit AS Chit, ap AS Ap, hp AS Hp, max_cp AS MaxCp, power AS Power
            FROM item_templates
            WHERE id = @Id
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<ItemEntity>(new CommandDefinition(sql, new { Id = itemId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UserItemEntity>> GetUserItemsAsync(int userId, int? characterId = null, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        string sql = characterId.HasValue
            ? """
              SELECT id AS Id, user_id AS UserId, character_id AS CharacterId, item_id AS ItemId, 
                     serial_number AS SerialNumber, serial_type AS SerialType, level AS Level, 
                     limit_time_minutes AS LimitTimeMinutes, slot_index AS SlotIndex, exp AS Exp
              FROM user_items
              WHERE user_id = @UserId AND character_id = @CharacterId
              ORDER BY slot_index ASC;
              """
            : """
              SELECT id AS Id, user_id AS UserId, character_id AS CharacterId, item_id AS ItemId, 
                     serial_number AS SerialNumber, serial_type AS SerialType, level AS Level, 
                     limit_time_minutes AS LimitTimeMinutes, slot_index AS SlotIndex, exp AS Exp
              FROM user_items
              WHERE user_id = @UserId
              ORDER BY slot_index ASC;
              """;

        var list = await connection.QueryAsync<UserItemEntity>(new CommandDefinition(sql, new { UserId = userId, CharacterId = characterId }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<UserItemEntity> AddUserItemAsync(UserItemEntity item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            INSERT INTO user_items (user_id, character_id, item_id, serial_number, serial_type, level, limit_time_minutes, slot_index, exp)
            VALUES (@UserId, @CharacterId, @ItemId, @SerialNumber, @SerialType, @Level, @LimitTimeMinutes, @SlotIndex, @Exp);
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, item, cancellationToken: ct));
        item.Id = await GetLastInsertedIdAsync(connection, null, ct);
        return item;
    }

    public async Task<bool> RemoveUserItemAsync(int userItemId, int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            DELETE FROM user_items
            WHERE id = @Id AND user_id = @UserId;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = userItemId, UserId = userId }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> UpdateSlotAsync(int userItemId, int userId, byte newSlotIndex, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE user_items
            SET slot_index = @SlotIndex
            WHERE id = @Id AND user_id = @UserId;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = userItemId, UserId = userId, SlotIndex = newSlotIndex }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> UpdateLevelAndExpAsync(int userItemId, byte newLevel, long newExp, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE user_items
            SET level = @Level, exp = @Exp
            WHERE id = @Id;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = userItemId, Level = newLevel, Exp = newExp }, cancellationToken: ct));
        return affected > 0;
    }

    private static async Task<int> GetLastInsertedIdAsync(DbConnection conn, DbTransaction? tx, CancellationToken ct)
    {
        string query = conn.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "SELECT last_insert_rowid();"
            : "SELECT LAST_INSERT_ID();";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(query, transaction: tx, cancellationToken: ct));
    }
}

