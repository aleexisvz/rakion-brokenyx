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

    public Task<bool> SwapBoxSlotsAsync(int userId, byte sourceSlot, byte destinationSlot, CancellationToken ct = default) =>
        SwapSlotsAsync(userId, 0, sourceSlot, 0, destinationSlot, ct);

    public async Task<bool> SwapSlotsAsync(
        int userId,
        int sourceCharacterId,
        byte sourceSlot,
        int destinationCharacterId,
        byte destinationSlot,
        CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            const string selectSql = """
                SELECT id
                FROM user_items
                WHERE user_id = @UserId AND character_id = @CharacterId AND slot_index = @Slot
                LIMIT 1;
                """;

            int? source = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                selectSql,
                new { UserId = userId, CharacterId = sourceCharacterId, Slot = sourceSlot },
                tx,
                cancellationToken: ct));
            if (source is null)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            int? destination = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                selectSql,
                new { UserId = userId, CharacterId = destinationCharacterId, Slot = destinationSlot },
                tx,
                cancellationToken: ct));

            const string moveSql = "UPDATE user_items SET character_id = @CharacterId, slot_index = @Slot WHERE id = @Id AND user_id = @UserId;";
            await connection.ExecuteAsync(new CommandDefinition(
                moveSql,
                new { CharacterId = destinationCharacterId, Slot = destinationSlot, Id = source.Value, UserId = userId },
                tx,
                cancellationToken: ct));
            if (destination is not null && destination != source)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    moveSql,
                    new { CharacterId = sourceCharacterId, Slot = sourceSlot, Id = destination.Value, UserId = userId },
                    tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<int> UpsertItemTemplatesAsync(IReadOnlyList<ItemEntity> templates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(templates);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        // MySQL and SQLite spell upserts differently; REPLACE is avoided because it deletes rows that
        // user_items may reference.
        string conflict = IsSqlite(connection)
            ? """
              ON CONFLICT(id) DO UPDATE SET type_id = excluded.type_id, class_id = excluded.class_id,
                required_level = excluded.required_level, shop_type = excluded.shop_type,
                gold_price = excluded.gold_price, cash_price = excluded.cash_price, hit1 = excluded.hit1,
                hit2 = excluded.hit2, hit3 = excluded.hit3, hit4 = excluded.hit4, chit = excluded.chit,
                ap = excluded.ap, hp = excluded.hp, max_cp = excluded.max_cp, power = excluded.power;
              """
            : """
              ON DUPLICATE KEY UPDATE type_id = VALUES(type_id), class_id = VALUES(class_id),
                required_level = VALUES(required_level), shop_type = VALUES(shop_type),
                gold_price = VALUES(gold_price), cash_price = VALUES(cash_price), hit1 = VALUES(hit1),
                hit2 = VALUES(hit2), hit3 = VALUES(hit3), hit4 = VALUES(hit4), chit = VALUES(chit),
                ap = VALUES(ap), hp = VALUES(hp), max_cp = VALUES(max_cp), power = VALUES(power);
              """;

        string sql = """
            INSERT INTO item_templates (id, type_id, class_id, required_level, shop_type, gold_price, cash_price,
                                        hit1, hit2, hit3, hit4, chit, ap, hp, max_cp, power)
            VALUES (@Id, @Type, @Class, @RequiredLevel, @Shop, @GoldPrice, @CashPrice,
                    @Hit1, @Hit2, @Hit3, @Hit4, @Chit, @Ap, @Hp, @MaxCp, @Power)
            """ + conflict;

        try
        {
            foreach (var template in templates)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        template.Id,
                        template.Type,
                        Class = (byte)template.Class,
                        template.RequiredLevel,
                        template.Shop,
                        template.GoldPrice,
                        template.CashPrice,
                        template.Hit1,
                        template.Hit2,
                        template.Hit3,
                        template.Hit4,
                        template.Chit,
                        template.Ap,
                        template.Hp,
                        template.MaxCp,
                        template.Power,
                    },
                    tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
            return templates.Count;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PurchaseOutcome?> PurchaseIntoBoxAsync(
        int userId,
        int itemId,
        bool payWithGold,
        int price,
        byte slot,
        byte level,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            int occupied = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM user_items WHERE user_id = @UserId AND character_id = 0 AND slot_index = @Slot;",
                new { UserId = userId, Slot = slot },
                tx,
                cancellationToken: ct));
            if (occupied > 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            string chargeSql = payWithGold
                ? "UPDATE user_game_info SET gold = gold - @Price WHERE user_id = @UserId AND gold >= @Price;"
                : "UPDATE cash_balances SET cash_amount = cash_amount - @Price, updated_at = @Now WHERE user_id = @UserId AND cash_amount >= @Price;";
            int charged = await connection.ExecuteAsync(new CommandDefinition(
                chargeSql,
                new { UserId = userId, Price = price, Now = DateTime.UtcNow },
                tx,
                cancellationToken: ct));
            if (charged == 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO user_items (user_id, character_id, item_id, level, limit_time_minutes, slot_index, exp)
                VALUES (@UserId, 0, @ItemId, @Level, 0, @Slot, 0);
                """,
                new { UserId = userId, ItemId = itemId, Level = level, Slot = slot },
                tx,
                cancellationToken: ct));
            int rowId = await GetLastInsertedIdAsync(connection, tx, ct);

            long gold = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT gold FROM user_game_info WHERE user_id = @UserId;", new { UserId = userId }, tx, cancellationToken: ct));
            int cash = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE((SELECT cash_amount FROM cash_balances WHERE user_id = @UserId), 0);", new { UserId = userId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new PurchaseOutcome(rowId, gold, cash);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<SaleOutcome?> SellFromBoxAsync(int userId, byte slot, int itemId, long goldGained, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(goldGained);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            int? rowId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                SELECT id FROM user_items
                WHERE user_id = @UserId AND character_id = 0 AND slot_index = @Slot AND item_id = @ItemId
                LIMIT 1;
                """,
                new { UserId = userId, Slot = slot, ItemId = itemId },
                tx,
                cancellationToken: ct));
            if (rowId is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM user_items WHERE id = @Id AND user_id = @UserId;",
                new { Id = rowId.Value, UserId = userId },
                tx,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_game_info SET gold = gold + @Gold WHERE user_id = @UserId;",
                new { UserId = userId, Gold = goldGained },
                tx,
                cancellationToken: ct));

            long gold = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT gold FROM user_game_info WHERE user_id = @UserId;", new { UserId = userId }, tx, cancellationToken: ct));
            int cash = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE((SELECT cash_amount FROM cash_balances WHERE user_id = @UserId), 0);", new { UserId = userId }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new SaleOutcome(rowId.Value, gold, cash);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static bool IsSqlite(DbConnection connection) =>
        connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

    private static async Task<int> GetLastInsertedIdAsync(DbConnection conn, DbTransaction? tx, CancellationToken ct)
    {
        string query = conn.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "SELECT last_insert_rowid();"
            : "SELECT LAST_INSERT_ID();";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(query, transaction: tx, cancellationToken: ct));
    }
}

