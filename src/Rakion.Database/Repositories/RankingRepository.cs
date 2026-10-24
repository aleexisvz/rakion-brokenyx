using Dapper;
using Rakion.Database.Connection;
using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

public sealed class RankingRepository : IRankingRepository
{
    private const string CreateSnapshots = """
        CREATE TABLE IF NOT EXISTS ranking_total (
            character_id INT NOT NULL, `rank` INT NOT NULL, last_rank INT NOT NULL, class_rank INT NOT NULL,
            country INT NOT NULL, username VARCHAR(16) NOT NULL, name VARCHAR(16) NOT NULL, level INT NOT NULL,
            class_id INT NOT NULL, grade INT NOT NULL, exp INT NOT NULL, win INT NOT NULL, lose INT NOT NULL, draw INT NOT NULL,
            PRIMARY KEY (character_id));
        CREATE TABLE IF NOT EXISTS ranking_class (
            character_id INT NOT NULL, class_id INT NOT NULL, `rank` INT NOT NULL, last_rank INT NOT NULL,
            country INT NOT NULL, username VARCHAR(16) NOT NULL, name VARCHAR(16) NOT NULL, level INT NOT NULL,
            grade INT NOT NULL, exp INT NOT NULL, win INT NOT NULL, lose INT NOT NULL, draw INT NOT NULL,
            PRIMARY KEY (character_id));
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public RankingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RankingCandidate>> GetCandidatesAsync(DateTime activeSince, IReadOnlyCollection<int> excludedAuth, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await connection.QueryAsync<CandidateRow>(new CommandDefinition(
            """
            SELECT c.id AS CharacterId, g.username AS Username, c.name AS Name, c.level AS Level, c.class_id AS ClassId,
                   c.exp AS Exp, c.win_count AS Win, c.loss_count AS Lose, c.draw_count AS Draw,
                   c.total_rank AS PreviousTotalRank, c.class_rank AS PreviousClassRank, g.country AS Country
            FROM characters c
            JOIN user_game_info g ON g.user_id = c.user_id
            WHERE g.last_connect > @ActiveSince AND c.auth NOT IN @Excluded
            ORDER BY c.exp DESC, c.id;
            """,
            new { ActiveSince = activeSince, Excluded = excludedAuth.ToArray() },
            cancellationToken: ct));
        return rows.Select(r => new RankingCandidate(
                r.CharacterId, r.Username, r.Name, r.Level, r.ClassId, r.Exp, r.Win, r.Lose, r.Draw, r.PreviousTotalRank, r.PreviousClassRank, r.Country))
            .ToList();
    }

    public async Task SaveAsync(IReadOnlyList<RankingEntry> entries, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(CreateSnapshots, cancellationToken: ct));

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM ranking_total;", transaction: tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM ranking_class;", transaction: tx, cancellationToken: ct));
            foreach (var entry in entries)
            {
                var c = entry.Candidate;
                var args = new
                {
                    c.CharacterId,
                    entry.TotalRank,
                    entry.ClassRank,
                    Grade = (int)entry.Grade,
                    c.PreviousTotalRank,
                    c.PreviousClassRank,
                    c.Country,
                    c.Username,
                    c.Name,
                    Level = (int)c.Level,
                    ClassId = (int)c.ClassId,
                    c.Exp,
                    c.Win,
                    c.Lose,
                    c.Draw,
                };
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE characters SET rank_grade = @Grade, total_rank = @TotalRank, class_rank = @ClassRank WHERE id = @CharacterId;",
                    args,
                    tx,
                    cancellationToken: ct));
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO ranking_total (character_id, `rank`, last_rank, class_rank, country, username, name, level, class_id, grade, exp, win, lose, draw)
                    VALUES (@CharacterId, @TotalRank, @PreviousTotalRank, @PreviousClassRank, @Country, @Username, @Name, @Level, @ClassId, @Grade, @Exp, @Win, @Lose, @Draw);
                    """,
                    args,
                    tx,
                    cancellationToken: ct));
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO ranking_class (character_id, class_id, `rank`, last_rank, country, username, name, level, grade, exp, win, lose, draw)
                    VALUES (@CharacterId, @ClassId, @ClassRank, @PreviousClassRank, @Country, @Username, @Name, @Level, @Grade, @Exp, @Win, @Lose, @Draw);
                    """,
                    args,
                    tx,
                    cancellationToken: ct));
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private sealed class CandidateRow
    {
        public int CharacterId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public byte Level { get; set; }

        public byte ClassId { get; set; }

        public int Exp { get; set; }

        public int Win { get; set; }

        public int Lose { get; set; }

        public int Draw { get; set; }

        public int PreviousTotalRank { get; set; }

        public int PreviousClassRank { get; set; }

        public int Country { get; set; }
    }
}
