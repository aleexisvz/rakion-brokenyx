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
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);

        var character = await GetCharacterByIdAsync(characterId, ct).ConfigureAwait(false);
        if (character is null || !character.IsUsed)
        {
            return false;
        }

        // A deleteKey is valid if:
        // 1. It matches the stored DeleteKey exactly, OR
        // 2. The client supplied empty/whitespace and the character has the default "sirmaster" key, OR
        // 3. The client supplied "sirmaster" (the master bypass key), OR
        // 4. The stored DeleteKey itself is empty/null/whitespace.
        bool keyMatches = string.Equals(character.DeleteKey, deleteKey, StringComparison.Ordinal)
            || (string.IsNullOrWhiteSpace(deleteKey) && string.Equals(character.DeleteKey, "sirmaster", StringComparison.OrdinalIgnoreCase))
            || string.Equals(deleteKey, "sirmaster", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(character.DeleteKey);

        if (!keyMatches)
        {
            return false;
        }

        const string sql = """
            UPDATE characters
            SET is_used = 0, change_time = @ChangeTime
            WHERE id = @Id AND is_used = 1;
            """;

        int affected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = characterId, ChangeTime = DateTime.UtcNow },
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

    private sealed class StatRow
    {
        public int LevelPoint { get; set; }
        public int Value { get; set; }
    }

    // Fixed column per stat; never built from client input.
    private static readonly IReadOnlyDictionary<CharacterStat, string> StatColumns = new Dictionary<CharacterStat, string>
    {
        [CharacterStat.Hit1] = "hit1",
        [CharacterStat.Hit2] = "hit2",
        [CharacterStat.Hit3] = "hit3",
        [CharacterStat.Hit4] = "hit4",
        [CharacterStat.Chit] = "chit",
        [CharacterStat.Hp] = "hp",
        [CharacterStat.Ap] = "ap",
        [CharacterStat.AttackSpeed] = "attack_speed",
        [CharacterStat.Speed] = "speed",
        [CharacterStat.MaxCp] = "max_cp",
    };

    public async Task<StatAllocationOutcome> AllocateStatPointAsync(
        int userId,
        int characterId,
        CharacterStat stat,
        int maxPerStat,
        CancellationToken ct = default)
    {
        if (!StatColumns.TryGetValue(stat, out string? column))
        {
            throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown stat.");
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<StatRow>(new CommandDefinition(
                $"SELECT level_point AS LevelPoint, {column} AS Value FROM characters WHERE id = @Id AND user_id = @UserId;",
                new { Id = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            int powerPoints = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE((SELECT power_level_point FROM user_game_info WHERE user_id = @UserId), 0);",
                new { UserId = userId },
                tx,
                cancellationToken: ct));

            if (row is null)
            {
                await tx.RollbackAsync(ct);
                return new StatAllocationOutcome(StatAllocationStatus.CharacterNotFound, 0, powerPoints, 0);
            }

            int levelPoints = row.LevelPoint;
            int value = row.Value;
            StatAllocationStatus? refusal = levelPoints <= 0 && powerPoints <= 0
                ? StatAllocationStatus.NoPoints
                : value >= maxPerStat ? StatAllocationStatus.AtMaximum : null;
            if (refusal is not null)
            {
                await tx.RollbackAsync(ct);
                return new StatAllocationOutcome(refusal.Value, levelPoints, powerPoints, value);
            }

            if (levelPoints > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE characters SET level_point = level_point - 1, {column} = {column} + 1 WHERE id = @Id AND user_id = @UserId;",
                    new { Id = characterId, UserId = userId },
                    tx,
                    cancellationToken: ct));
                levelPoints--;
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE user_game_info SET power_level_point = power_level_point - 1 WHERE user_id = @UserId;",
                    new { UserId = userId },
                    tx,
                    cancellationToken: ct));
                await connection.ExecuteAsync(new CommandDefinition(
                    $"UPDATE characters SET {column} = {column} + 1 WHERE id = @Id AND user_id = @UserId;",
                    new { Id = characterId, UserId = userId },
                    tx,
                    cancellationToken: ct));
                powerPoints--;
            }

            await tx.CommitAsync(ct);
            return new StatAllocationOutcome(StatAllocationStatus.Allocated, levelPoints, powerPoints, value + 1);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
