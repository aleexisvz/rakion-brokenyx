using System.Data.Common;
using Dapper;
using Rakion.Core.Enums;
using Rakion.Database.Connection;
using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Dapper implementation of character persistence and slot validation.
/// </summary>
public sealed class CharacterRepository : ICharacterRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CharacterRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<CharacterEntity>> GetCharactersByUserIdAsync(int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, user_id AS UserId, name AS Name, is_used AS IsUsed, 
                   delete_key AS DeleteKey, auth AS Auth, class_id AS Class, 
                   level AS Level, win_count AS WinCount, loss_count AS LossCount, 
                   draw_count AS DrawCount, exp AS Exp, level_point AS LevelPoint, 
                   slot_index AS SlotIndex, hit1 AS Hit1, hit2 AS Hit2, hit3 AS Hit3, 
                   hit4 AS Hit4, chit AS Chit, hp AS Hp, ap AS Ap, 
                   attack_speed AS AttackSpeed, speed AS Speed, max_cp AS MaxCp, 
                   rank_grade AS RankGrade, total_rank AS TotalRank, class_rank AS ClassRank, 
                   potion_slot_count AS PotionSlotCount, change_time AS ChangeTime, 
                   create_time AS CreateTime
            FROM characters
            WHERE user_id = @UserId AND is_used = 1
            ORDER BY slot_index ASC;
            """;

        var list = await connection.QueryAsync<CharacterEntity>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return list.AsList();
    }

    public async Task<CharacterEntity?> GetCharacterByIdAsync(int characterId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, user_id AS UserId, name AS Name, is_used AS IsUsed, 
                   delete_key AS DeleteKey, auth AS Auth, class_id AS Class, 
                   level AS Level, win_count AS WinCount, loss_count AS LossCount, 
                   draw_count AS DrawCount, exp AS Exp, level_point AS LevelPoint, 
                   slot_index AS SlotIndex, hit1 AS Hit1, hit2 AS Hit2, hit3 AS Hit3, 
                   hit4 AS Hit4, chit AS Chit, hp AS Hp, ap AS Ap, 
                   attack_speed AS AttackSpeed, speed AS Speed, max_cp AS MaxCp, 
                   rank_grade AS RankGrade, total_rank AS TotalRank, class_rank AS ClassRank, 
                   potion_slot_count AS PotionSlotCount, change_time AS ChangeTime, 
                   create_time AS CreateTime
            FROM characters
            WHERE id = @Id AND is_used = 1
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<CharacterEntity>(new CommandDefinition(sql, new { Id = characterId }, cancellationToken: ct));
    }

    public async Task<CharacterEntity?> GetCharacterByNameAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT id AS Id, user_id AS UserId, name AS Name, is_used AS IsUsed, 
                   delete_key AS DeleteKey, auth AS Auth, class_id AS Class, 
                   level AS Level, win_count AS WinCount, loss_count AS LossCount, 
                   draw_count AS DrawCount, exp AS Exp, level_point AS LevelPoint, 
                   slot_index AS SlotIndex, hit1 AS Hit1, hit2 AS Hit2, hit3 AS Hit3, 
                   hit4 AS Hit4, chit AS Chit, hp AS Hp, ap AS Ap, 
                   attack_speed AS AttackSpeed, speed AS Speed, max_cp AS MaxCp, 
                   rank_grade AS RankGrade, total_rank AS TotalRank, class_rank AS ClassRank, 
                   potion_slot_count AS PotionSlotCount, change_time AS ChangeTime, 
                   create_time AS CreateTime
            FROM characters
            WHERE name = @Name AND is_used = 1
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<CharacterEntity>(new CommandDefinition(sql, new { Name = name }, cancellationToken: ct));
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            SELECT COUNT(1)
            FROM characters
            WHERE name = @Name AND is_used = 1;
            """;

        int count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { Name = name }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<CharacterEntity> CreateCharacterAsync(int userId, string name, CharacterClass characterClass, byte slotIndex, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 12)
        {
            throw new ArgumentException("Character name cannot exceed 12 characters.", nameof(name));
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            // Verify slot count limit from user_game_info
            const string getSlotCountSql = """
                SELECT slot_count
                FROM user_game_info
                WHERE user_id = @UserId
                LIMIT 1;
                """;

            int maxSlots = await connection.ExecuteScalarAsync<int>(new CommandDefinition(getSlotCountSql, new { UserId = userId }, transaction: tx, cancellationToken: ct));
            if (maxSlots <= 0)
            {
                maxSlots = 4; // Default fallback
            }

            if (slotIndex >= maxSlots)
            {
                throw new InvalidOperationException($"Requested slot index {slotIndex} exceeds unlocked slot limit of {maxSlots}.");
            }

            // Verify slot index is not already occupied
            const string checkSlotSql = """
                SELECT COUNT(1)
                FROM characters
                WHERE user_id = @UserId AND slot_index = @SlotIndex AND is_used = 1;
                """;

            int occupied = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkSlotSql, new { UserId = userId, SlotIndex = slotIndex }, transaction: tx, cancellationToken: ct));
            if (occupied > 0)
            {
                throw new InvalidOperationException($"Slot index {slotIndex} is already occupied by an active character.");
            }

            // Verify name uniqueness
            const string checkNameSql = """
                SELECT COUNT(1)
                FROM characters
                WHERE name = @Name AND is_used = 1;
                """;

            int nameCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkNameSql, new { Name = name }, transaction: tx, cancellationToken: ct));
            if (nameCount > 0)
            {
                throw new InvalidOperationException($"Character name '{name}' is already taken.");
            }

            var character = new CharacterEntity
            {
                UserId = userId,
                Name = name,
                Class = characterClass,
                SlotIndex = slotIndex,
                Level = 1,
                Exp = 0,
                LevelPoint = 0,
                PotionSlotCount = 3,
                DeleteKey = "sirmaster",
                IsUsed = true,
                CreateTime = DateTime.UtcNow
            };

            const string insertSql = """
                INSERT INTO characters (
                    user_id, name, is_used, delete_key, auth, class_id, level, 
                    win_count, loss_count, draw_count, exp, level_point, slot_index, 
                    hit1, hit2, hit3, hit4, chit, hp, ap, attack_speed, speed, 
                    max_cp, rank_grade, total_rank, class_rank, potion_slot_count, create_time
                ) VALUES (
                    @UserId, @Name, 1, @DeleteKey, @Auth, @Class, @Level, 
                    @WinCount, @LossCount, @DrawCount, @Exp, @LevelPoint, @SlotIndex, 
                    @Hit1, @Hit2, @Hit3, @Hit4, @Chit, @Hp, @Ap, @AttackSpeed, @Speed, 
                    @MaxCp, @RankGrade, @TotalRank, @ClassRank, @PotionSlotCount, @CreateTime
                );
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                insertSql,
                character,
                transaction: tx,
                cancellationToken: ct));

            character.Id = await GetLastInsertedIdAsync(connection, tx, ct);

            await tx.CommitAsync(ct);
            return character;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> DeleteCharacterAsync(int characterId, string deleteKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deleteKey);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """
            UPDATE characters
            SET is_used = 0, change_time = @ChangeTime
            WHERE id = @Id AND delete_key = @DeleteKey AND is_used = 1;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = characterId, DeleteKey = deleteKey, ChangeTime = DateTime.UtcNow },
            cancellationToken: ct));

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

