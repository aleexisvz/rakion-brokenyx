using System.Data.Common;
using Dapper;
using Rakion.Database.Connection;

namespace Rakion.Database.Repositories;

/// <summary>
/// Tables match_point_log (legacy LogFieldGamePointList) and abuser_check (legacy LogFieldGameAbuserCheck), created on
/// first use like buddy_groups. The check is the legacy RankUpdate.exe query (Observed): totals per character since a
/// date, kept when SUM(win) &gt;= wins OR SUM(exp) &gt;= exp OR SUM(gold) &gt;= gold.
/// </summary>
public sealed class AbuserRepository : IAbuserRepository
{
    private const string CreateTables = """
        CREATE TABLE IF NOT EXISTS match_point_log (
            host_character_id INT NOT NULL, host_character_name VARCHAR(16) NOT NULL,
            character_id INT NOT NULL, character_name VARCHAR(16) NOT NULL, power_user TINYINT NOT NULL,
            mode TINYINT NOT NULL, max_rounds TINYINT NOT NULL, round TINYINT NOT NULL,
            win SMALLINT NOT NULL, loss SMALLINT NOT NULL, exp BIGINT NOT NULL, gold BIGINT NOT NULL,
            created_at DATETIME NOT NULL);
        CREATE TABLE IF NOT EXISTS abuser_check (
            character_id INT NOT NULL, character_name VARCHAR(16) NOT NULL, power_user TINYINT NOT NULL,
            wins BIGINT NOT NULL, losses BIGINT NOT NULL, exp BIGINT NOT NULL, gold BIGINT NOT NULL,
            checked_at DATETIME NOT NULL);
        """;

    private const string FlagColumns = """
        character_id AS CharacterId, character_name AS CharacterName, power_user AS PowerUser, wins AS Wins,
        losses AS Losses, exp AS Exp, gold AS Gold, checked_at AS CheckedAt
        """;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public AbuserRepository(IDbConnectionFactory connectionFactory, TimeProvider? timeProvider = null)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task LogMatchPointAsync(MatchPointLogEntry entry, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO match_point_log (host_character_id, host_character_name, character_id, character_name, power_user,
                mode, max_rounds, round, win, loss, exp, gold, created_at)
            VALUES (@HostCharacterId, @HostCharacterName, @CharacterId, @CharacterName, @PowerUser,
                @Mode, @MaxRounds, @Round, @Win, @Loss, @Exp, @Gold, @CreatedAt);
            """,
            new
            {
                entry.HostCharacterId,
                entry.HostCharacterName,
                entry.CharacterId,
                entry.CharacterName,
                PowerUser = entry.PowerUser ? 1 : 0,
                entry.Mode,
                entry.MaxRounds,
                entry.Round,
                Win = entry.Win ? 1 : 0,
                Loss = entry.Loss ? 1 : 0,
                entry.Exp,
                entry.Gold,
                CreatedAt = Now(),
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AbuserFlag>> RunCheckAsync(DateTime since, int winSum, long expSum, long goldSum, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var checkedAt = Now();
        var flags = (await connection.QueryAsync<FlagRow>(new CommandDefinition(
            """
            SELECT character_id AS CharacterId, MAX(character_name) AS CharacterName, MAX(power_user) AS PowerUser,
                SUM(win) AS Wins, SUM(loss) AS Losses, SUM(exp) AS Exp, SUM(gold) AS Gold
            FROM match_point_log WHERE created_at > @Since
            GROUP BY character_id
            HAVING SUM(win) >= @WinSum OR SUM(exp) >= @ExpSum OR SUM(gold) >= @GoldSum
            ORDER BY SUM(exp) DESC;
            """,
            new { Since = since, WinSum = winSum, ExpSum = expSum, GoldSum = goldSum },
            tx,
            cancellationToken: ct))).ToList();
        foreach (var flag in flags)
        {
            flag.CheckedAt = checkedAt;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO abuser_check (character_id, character_name, power_user, wins, losses, exp, gold, checked_at)
                VALUES (@CharacterId, @CharacterName, @PowerUser, @Wins, @Losses, @Exp, @Gold, @CheckedAt);
                """,
                flag,
                tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return flags.Select(ToFlag).ToList();
    }

    public async Task<IReadOnlyList<AbuserFlag>> GetRecentFlagsAsync(int count, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        var flags = await connection.QueryAsync<FlagRow>(new CommandDefinition(
            $"SELECT {FlagColumns} FROM abuser_check ORDER BY checked_at DESC, exp DESC LIMIT @Count;",
            new { Count = Math.Max(count, 0) },
            cancellationToken: ct));
        return flags.Select(ToFlag).ToList();
    }

    private static AbuserFlag ToFlag(FlagRow r) => new(r.CharacterId, r.CharacterName, r.PowerUser != 0, r.Wins, r.Losses, r.Exp, r.Gold, r.CheckedAt);

    // Whole seconds, so the rows of one check can be read back by their timestamp on every database.
    private DateTime Now()
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), now.Kind);
    }

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = await _connectionFactory.CreateConnectionAsync(ct);
        foreach (string statement in CreateTables.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await connection.ExecuteAsync(new CommandDefinition(statement + ";", cancellationToken: ct));
        }

        return connection;
    }

    private sealed class FlagRow
    {
        public int CharacterId { get; set; }

        public string CharacterName { get; set; } = string.Empty;

        public long PowerUser { get; set; }

        public long Wins { get; set; }

        public long Losses { get; set; }

        public long Exp { get; set; }

        public long Gold { get; set; }

        public DateTime CheckedAt { get; set; }
    }
}
