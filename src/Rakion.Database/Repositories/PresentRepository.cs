using Dapper;
using Rakion.Database.Connection;

namespace Rakion.Database.Repositories;

/// <summary>
/// Present box on the pending_presents table (legacy pendingpresents: "SELECT id, present_id FROM pendingpresents
/// WHERE user_id=%u ORDER BY id LIMIT 1", "DELETE FROM pendingpresents WHERE id=%u", Observed in RakionWorldServ.exe).
/// The table is created on first use, like buddy_groups.
/// </summary>
public sealed class PresentRepository : IPresentRepository
{
    private const string CreateTable = """
        CREATE TABLE IF NOT EXISTS pending_presents (
            id INTEGER PRIMARY KEY AUTO_INCREMENT, item_id INT NOT NULL, user_id INT NOT NULL,
            added_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP);
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public PresentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> SendAsync(int userId, int itemId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Schema(connection), cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO pending_presents (item_id, user_id) VALUES (@ItemId, @UserId);", new { UserId = userId, ItemId = itemId }, cancellationToken: ct));
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT MAX(id) FROM pending_presents WHERE user_id = @UserId;", new { UserId = userId }, cancellationToken: ct));
    }

    public async Task<PendingPresent?> PeekAsync(int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Schema(connection), cancellationToken: ct));
        var row = await connection.QuerySingleOrDefaultAsync<PresentRow>(new CommandDefinition(
            "SELECT id AS Id, item_id AS ItemId FROM pending_presents WHERE user_id = @UserId ORDER BY id LIMIT 1;",
            new { UserId = userId },
            cancellationToken: ct));
        return row is null ? null : new PendingPresent(row.Id, row.ItemId);
    }

    public async Task<PendingPresent?> GetAsync(int userId, int pendingId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Schema(connection), cancellationToken: ct));
        var row = await connection.QuerySingleOrDefaultAsync<PresentRow>(new CommandDefinition(
            "SELECT id AS Id, item_id AS ItemId FROM pending_presents WHERE id = @Id AND user_id = @UserId;",
            new { UserId = userId, Id = pendingId },
            cancellationToken: ct));
        return row is null ? null : new PendingPresent(row.Id, row.ItemId);
    }

    public async Task<PresentAcceptStatus> AcceptAsync(int userId, int pendingId, byte boxSlot, long exp, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Schema(connection), cancellationToken: ct));
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            var args = new { UserId = userId, Id = pendingId, Slot = boxSlot, Exp = exp };
            int? itemId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT item_id FROM pending_presents WHERE id = @Id AND user_id = @UserId;", args, tx, cancellationToken: ct));
            if (itemId is null)
            {
                await tx.RollbackAsync(ct);
                return PresentAcceptStatus.NotFound;
            }

            bool taken = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM user_items WHERE user_id = @UserId AND character_id = 0 AND slot_index = @Slot;", args, tx, cancellationToken: ct)) > 0;
            if (taken)
            {
                await tx.RollbackAsync(ct);
                return PresentAcceptStatus.SlotTaken;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO user_items (user_id, character_id, item_id, slot_index, level, exp) VALUES (@UserId, 0, @ItemId, @Slot, 0, @Exp);",
                new { UserId = userId, ItemId = itemId.Value, Slot = boxSlot, Exp = exp },
                tx,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM pending_presents WHERE id = @Id;", args, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return PresentAcceptStatus.Accepted;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> DisposeAsync(int userId, int pendingId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Schema(connection), cancellationToken: ct));
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pending_presents WHERE id = @Id AND user_id = @UserId;", new { UserId = userId, Id = pendingId }, cancellationToken: ct)) > 0;
    }

    // SQLite (tests) spells auto increment differently.
    private static string Schema(System.Data.Common.DbConnection connection) =>
        connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? CreateTable.Replace("INTEGER PRIMARY KEY AUTO_INCREMENT", "INTEGER PRIMARY KEY AUTOINCREMENT", StringComparison.Ordinal)
            : CreateTable;

    private sealed class PresentRow
    {
        public int Id { get; set; }

        public int ItemId { get; set; }
    }
}
