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

    /// <summary>Creature items: ids 8000..8999 in equipment slots 10..12, at most 100 exp per round (0x40B940).</summary>
    public const int FirstCreatureItemId = 8000;
    public const int LastCreatureItemId = 8999;
    public const byte FirstCreatureSlot = 10;
    public const int CreatureSlotCount = 3;
    public const int MaxCreatureExpPerRound = 100;

    /// <summary>Stages 0..99 (the character record carries 100 rank bytes) and the 120-slot box.</summary>
    public const int StageCount = 100;
    public const int BoxSlotCount = 120;

    private readonly IDbConnectionFactory _connectionFactory;
    private volatile bool _stageTableReady;

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

    public async Task<int> UpsertCreatureLevelsAsync(IEnumerable<CreatureLevelEntity> levels, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        bool sqlite = IsSqlite(connection);

        string create = sqlite
            ? "CREATE TABLE IF NOT EXISTS creature_level_info (npc INTEGER NOT NULL, level INTEGER NOT NULL, exp INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (npc, level));"
            : """
              CREATE TABLE IF NOT EXISTS `creature_level_info` (
                  `npc` TINYINT UNSIGNED NOT NULL,
                  `level` TINYINT UNSIGNED NOT NULL,
                  `exp` INT NOT NULL DEFAULT 0,
                  PRIMARY KEY (`npc`, `level`)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
              """;
        await connection.ExecuteAsync(new CommandDefinition(create, cancellationToken: ct));

        string upsert = "INSERT INTO creature_level_info (npc, level, exp) VALUES (@Npc, @Level, @Exp) " +
            (sqlite ? "ON CONFLICT(npc, level) DO UPDATE SET exp = excluded.exp;" : "ON DUPLICATE KEY UPDATE exp = VALUES(exp);");

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

    public async Task<RoundRewardOutcome?> ApplyRoundRewardAsync(
        int userId,
        int characterId,
        int exp,
        int gold,
        byte result = RoundResult.None,
        IReadOnlyList<int>? creatureExp = null,
        CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            var outcome = await ApplyRewardAsync(connection, tx, userId, characterId, exp, gold, result, creatureExp, ct);
            if (outcome is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            await tx.CommitAsync(ct);
            return outcome;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<StageClearOutcome?> ApplyStageClearAsync(
        int userId,
        int characterId,
        byte stage,
        byte rank,
        IReadOnlyList<ushort> drops,
        int exp,
        int gold,
        IReadOnlyList<int>? creatureExp = null,
        CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await EnsureStageTableAsync(connection, ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            int owned = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM characters WHERE id = @CharacterId AND user_id = @UserId;",
                new { CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            if (owned == 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            // Only a better rank than the stored one pays out (0x40BAE0, Observed).
            var stored = await connection.QuerySingleOrDefaultAsync<StageRow>(new CommandDefinition(
                "SELECT id AS Id, `rank` AS `Rank` FROM user_stage_info WHERE character_id = @CharacterId AND stage = @Stage;",
                new { CharacterId = characterId, Stage = stage },
                tx,
                cancellationToken: ct));
            if (stored is not null && stored.Rank >= rank)
            {
                await tx.RollbackAsync(ct);
                return new StageClearOutcome(false, stored.Rank, [], null);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                stored is null
                    ? "INSERT INTO user_stage_info (character_id, stage, `rank`) VALUES (@CharacterId, @Stage, @Rank);"
                    : "UPDATE user_stage_info SET `rank` = @Rank WHERE id = @Id;",
                new { CharacterId = characterId, Stage = stage, Rank = rank, Id = stored?.Id ?? 0 },
                tx,
                cancellationToken: ct));

            var dropSlots = await AddDropsToBoxAsync(connection, tx, userId, drops, ct);
            var reward = await ApplyRewardAsync(connection, tx, userId, characterId, exp, gold, RoundResult.Draw, creatureExp, ct);

            await tx.CommitAsync(ct);
            return new StageClearOutcome(true, stored?.Rank ?? 0, dropSlots, reward);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<int, byte[]>> GetStageRanksAsync(IReadOnlyCollection<int> characterIds, CancellationToken ct = default)
    {
        var ranks = characterIds.Distinct().ToDictionary(id => id, _ => new byte[StageCount]);
        if (ranks.Count == 0)
        {
            return ranks;
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await EnsureStageTableAsync(connection, ct);
        var rows = await connection.QueryAsync<StageRankRow>(new CommandDefinition(
            "SELECT character_id AS CharacterId, stage AS Stage, `rank` AS `Rank` FROM user_stage_info WHERE character_id IN @Ids;",
            new { Ids = ranks.Keys.ToArray() },
            cancellationToken: ct));
        foreach (var row in rows)
        {
            if (row.Stage < StageCount && ranks.TryGetValue(row.CharacterId, out var list))
            {
                list[row.Stage] = (byte)Math.Clamp(row.Rank, 0, byte.MaxValue);
            }
        }

        return ranks;
    }

    /// <summary>
    /// Puts each drop whose template exists into the lowest free box slot (Inferred placement: the legacy server
    /// forwards the drops to its database agent). Returns the (slot, item) pairs stored.
    /// </summary>
    private static async Task<IReadOnlyList<(byte Slot, ushort ItemId)>> AddDropsToBoxAsync(
        DbConnection connection,
        DbTransaction tx,
        int userId,
        IReadOnlyList<ushort> drops,
        CancellationToken ct)
    {
        var stored = new List<(byte Slot, ushort ItemId)>();
        if (drops.Count == 0)
        {
            return stored;
        }

        var used = (await connection.QueryAsync<int>(new CommandDefinition(
                "SELECT slot_index FROM user_items WHERE user_id = @UserId AND character_id = 0;",
                new { UserId = userId },
                tx,
                cancellationToken: ct)))
            .ToHashSet();
        foreach (ushort itemId in drops)
        {
            int known = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM item_templates WHERE id = @ItemId;", new { ItemId = (int)itemId }, tx, cancellationToken: ct));
            int slot = Enumerable.Range(0, BoxSlotCount).FirstOrDefault(s => !used.Contains(s), -1);
            if (known == 0 || slot < 0)
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO user_items (user_id, character_id, item_id, level, limit_time_minutes, slot_index, exp) VALUES (@UserId, 0, @ItemId, 1, 0, @Slot, 0);",
                new { UserId = userId, ItemId = (int)itemId, Slot = slot },
                tx,
                cancellationToken: ct));
            used.Add(slot);
            stored.Add(((byte)slot, itemId));
        }

        return stored;
    }

    /// <summary>Experience, gold, round result and creature experience inside the caller's transaction.</summary>
    private static async Task<RoundRewardOutcome?> ApplyRewardAsync(
        DbConnection connection,
        DbTransaction tx,
        int userId,
        int characterId,
        int exp,
        int gold,
        byte result,
        IReadOnlyList<int>? creatureExp,
        CancellationToken ct)
    {
        var character = await connection.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
            "SELECT class_id AS ClassId, level AS Level, exp AS Exp, level_point AS LevelPoint, win_count AS Wins, loss_count AS Losses, draw_count AS Draws FROM characters WHERE id = @CharacterId AND user_id = @UserId;",
            new { CharacterId = characterId, UserId = userId },
            tx,
            cancellationToken: ct));
        if (character is null)
        {
            return null;
        }

        var thresholds = (await connection.QueryAsync<LevelRow>(new CommandDefinition(
                "SELECT level AS Level, exp AS Exp FROM class_level_info WHERE class_id = @ClassId;",
                new { character.ClassId },
                tx,
                cancellationToken: ct)))
            .ToDictionary(row => row.Level, row => row.Exp);

        var outcome = AddExperience(character.Level, character.Exp, character.LevelPoint, exp, thresholds);
        int wins = character.Wins + (result == RoundResult.Win ? 1 : 0);
        int losses = character.Losses + (result == RoundResult.Loss ? 1 : 0);
        int draws = character.Draws + (result == RoundResult.Draw ? 1 : 0);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE characters SET level = @Level, exp = @Exp, level_point = @LevelPoint, win_count = @Wins, loss_count = @Losses, draw_count = @Draws WHERE id = @CharacterId AND user_id = @UserId;",
            new { outcome.Level, outcome.Exp, outcome.LevelPoint, Wins = wins, Losses = losses, Draws = draws, CharacterId = characterId, UserId = userId },
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

        var creatures = await AddCreatureExperienceAsync(connection, tx, userId, characterId, creatureExp, ct);
        return outcome with
        {
            Gold = totalGold,
            Wins = wins,
            Losses = losses,
            Draws = draws,
            Creature1Level = creatures.Levels[0],
            Creature2Level = creatures.Levels[1],
            Creature3Level = creatures.Levels[2],
            CreatureLeveledUp = creatures.LeveledUp,
        };
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

    /// <summary>
    /// The legacy creature loop (0x40B940, Observed): each equipped creature with reported experience gets at most 100,
    /// then levels up while its total exceeds the table row for its level. At level 99 the total is capped by the
    /// first creature's row 99, which the legacy server reads for every creature.
    /// </summary>
    private static async Task<(byte[] Levels, bool LeveledUp)> AddCreatureExperienceAsync(
        DbConnection connection,
        DbTransaction tx,
        int userId,
        int characterId,
        IReadOnlyList<int>? creatureExp,
        CancellationToken ct)
    {
        var levels = new byte[CreatureSlotCount];
        var creatures = await connection.QueryAsync<CreatureRow>(new CommandDefinition(
            "SELECT id AS Id, item_id AS ItemId, level AS Level, exp AS Exp, slot_index AS Slot FROM user_items " +
            "WHERE user_id = @UserId AND character_id = @CharacterId AND slot_index BETWEEN @First AND @Last AND item_id BETWEEN @FirstId AND @LastId;",
            new
            {
                UserId = userId,
                CharacterId = characterId,
                First = (int)FirstCreatureSlot,
                Last = FirstCreatureSlot + CreatureSlotCount - 1,
                FirstId = FirstCreatureItemId,
                LastId = LastCreatureItemId,
            },
            tx,
            cancellationToken: ct));

        bool leveledUp = false;
        foreach (var creature in creatures)
        {
            int index = creature.Slot - FirstCreatureSlot;
            int added = creatureExp is not null && index < creatureExp.Count ? Math.Min(creatureExp[index], MaxCreatureExpPerRound) : 0;
            levels[index] = creature.Level;
            if (added <= 0)
            {
                continue;
            }

            var table = (await connection.QueryAsync<LevelRow>(new CommandDefinition(
                    "SELECT level AS Level, exp AS Exp FROM creature_level_info WHERE npc = @Npc;",
                    new { Npc = creature.ItemId - FirstCreatureItemId },
                    tx,
                    cancellationToken: ct)))
                .ToDictionary(row => row.Level, row => row.Exp);
            int? cap = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT exp FROM creature_level_info WHERE npc = 0 AND level = @MaxLevel;",
                new { MaxLevel },
                tx,
                cancellationToken: ct));
            table.Remove(MaxLevel);
            if (cap is { } capExp)
            {
                table[MaxLevel] = capExp;
            }

            var next = AddExperience(creature.Level, (int)Math.Min(creature.Exp, int.MaxValue), 0, added, table);
            levels[index] = next.Level;
            leveledUp |= next.LeveledUp;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_items SET level = @Level, exp = @Exp WHERE id = @Id;",
                new { next.Level, next.Exp, creature.Id },
                tx,
                cancellationToken: ct));
        }

        return (levels, leveledUp);
    }

    /// <summary>Databases created before stage clears existed get the table on first use.</summary>
    private async Task EnsureStageTableAsync(DbConnection connection, CancellationToken ct)
    {
        if (_stageTableReady)
        {
            return;
        }

        string create = IsSqlite(connection)
            ? "CREATE TABLE IF NOT EXISTS user_stage_info (id INTEGER PRIMARY KEY AUTOINCREMENT, character_id INTEGER NOT NULL, stage INTEGER NOT NULL, rank INTEGER NOT NULL DEFAULT 0, UNIQUE (character_id, stage));"
            : """
              CREATE TABLE IF NOT EXISTS `user_stage_info` (
                  `id` INT NOT NULL AUTO_INCREMENT,
                  `character_id` INT NOT NULL,
                  `stage` TINYINT UNSIGNED NOT NULL,
                  `rank` TINYINT UNSIGNED NOT NULL DEFAULT 0,
                  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                  PRIMARY KEY (`id`),
                  UNIQUE KEY `uq_user_stage_info` (`character_id`, `stage`)
              ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
              """;
        await connection.ExecuteAsync(new CommandDefinition(create, cancellationToken: ct));
        _stageTableReady = true;
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

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int Draws { get; set; }
    }

    private sealed class StageRow
    {
        public int Id { get; set; }

        public int Rank { get; set; }
    }

    private sealed class StageRankRow
    {
        public int CharacterId { get; set; }

        public int Stage { get; set; }

        public int Rank { get; set; }
    }

    private sealed class CreatureRow
    {
        public int Id { get; set; }

        public int ItemId { get; set; }

        public byte Level { get; set; }

        public long Exp { get; set; }

        public byte Slot { get; set; }
    }
}
