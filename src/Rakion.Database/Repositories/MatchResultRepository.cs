using System.Data.Common;
using Dapper;
using Rakion.Database.Connection;
using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Match rewards following the legacy server (RakionWorldServ.exe 0x40D300, Observed): experience is cumulative,
/// the character levels up while its total exceeds <c>class_level_info</c> for its class and current level, each
/// level gives 3 level points, and at level 99 the total is capped.
/// </summary>
public sealed class MatchResultRepository : IMatchResultRepository
{
    public const int MaxLevel = 99;
    public const int LevelPointsPerLevel = 3;

    private readonly IDbConnectionFactory _connectionFactory;

    public MatchResultRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> UpsertClassLevelsAsync(IEnumerable<ClassLevelEntity> levels, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        bool sqlite = IsSqlite(connection);

        // Databases created before this table existed get it here, so importing is enough to enable rewards.
        string create = sqlite
            ? "CREATE TABLE IF NOT EXISTS class_level_info (class_id INTEGER NOT NULL, level INTEGER NOT NULL, exp INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (class_id, level));"
            : """
              CREATE TABLE IF NOT EXISTS `class_level_info` (
                  `class_id` TINYINT UNSIGNED NOT NULL,
                  `level` TINYINT UNSIGNED NOT NULL,
                  `exp` INT NOT NULL DEFAULT 0,
                  PRIMARY KEY (`class_id`, `level`)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
              """;
        await connection.ExecuteAsync(new CommandDefinition(create, cancellationToken: ct));

        string upsert = "INSERT INTO class_level_info (class_id, level, exp) VALUES (@ClassId, @Level, @Exp) " +
            (sqlite ? "ON CONFLICT(class_id, level) DO UPDATE SET exp = excluded.exp;" : "ON DUPLICATE KEY UPDATE exp = VALUES(exp);");

        await using var tx = await connection.BeginTransactionAsync(ct);
        int written = 0;
        foreach (var level in levels)
        {
            await connection.ExecuteAsync(new CommandDefinition(upsert, level, tx, cancellationToken: ct));
            written++;
        }

        await tx.CommitAsync(ct);
        return written;
    }

    public async Task<RoundRewardOutcome?> ApplyRoundRewardAsync(int userId, int characterId, int exp, int gold, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            var character = await connection.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
                "SELECT class_id AS ClassId, level AS Level, exp AS Exp, level_point AS LevelPoint FROM characters WHERE id = @CharacterId AND user_id = @UserId;",
                new { CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            if (character is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var thresholds = (await connection.QueryAsync<LevelRow>(new CommandDefinition(
                    "SELECT level AS Level, exp AS Exp FROM class_level_info WHERE class_id = @ClassId;",
                    new { character.ClassId },
                    tx,
                    cancellationToken: ct)))
                .ToDictionary(row => row.Level, row => row.Exp);

            var result = AddExperience(character.Level, character.Exp, character.LevelPoint, exp, thresholds);

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE characters SET level = @Level, exp = @Exp, level_point = @LevelPoint WHERE id = @CharacterId AND user_id = @UserId;",
                new { result.Level, result.Exp, result.LevelPoint, CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_game_info SET gold = gold + @Gold WHERE user_id = @UserId;",
                new { Gold = gold, UserId = userId },
                tx,
                cancellationToken: ct));
            long totalGold = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT gold FROM user_game_info WHERE user_id = @UserId;",
                new { UserId = userId },
                tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            return result with { Gold = totalGold };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>The legacy level-up loop (0x40D300). Without a row for the current level the character stays put.</summary>
    public static RoundRewardOutcome AddExperience(byte level, int exp, int levelPoint, int added, IReadOnlyDictionary<byte, int> thresholds)
    {
        long total = Math.Min((long)exp + Math.Max(added, 0), int.MaxValue);
        bool leveledUp = false;
        if (level >= MaxLevel)
        {
            // The legacy server caps the total at the level 99 row (it reads class 0's row; Observed).
            if (thresholds.TryGetValue(MaxLevel, out int cap))
            {
                total = Math.Min(total, cap);
            }
        }
        else
        {
            while (level < MaxLevel && thresholds.TryGetValue(level, out int needed) && total > needed)
            {
                level++;
                levelPoint += LevelPointsPerLevel;
                leveledUp = true;
            }
        }

        return new RoundRewardOutcome(level, (int)total, Math.Min(levelPoint, byte.MaxValue), leveledUp, 0);
    }

    private static bool IsSqlite(DbConnection connection) =>
        connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

    private sealed class LevelRow
    {
        public byte Level { get; set; }

        public int Exp { get; set; }
    }

    private sealed class CharacterRow
    {
        public byte ClassId { get; set; }

        public byte Level { get; set; }

        public int Exp { get; set; }

        public int LevelPoint { get; set; }
    }
}
