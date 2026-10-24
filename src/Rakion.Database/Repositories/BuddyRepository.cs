using Dapper;
using Rakion.Database.Connection;

namespace Rakion.Database.Repositories;

public sealed class BuddyRepository : IBuddyRepository
{
    private const string DisplayNameSql = """
        COALESCE(NULLIF(g.buddy_name, ''),
                 (SELECT c.name FROM characters c WHERE c.user_id = u.id ORDER BY c.slot_index LIMIT 1),
                 u.username)
        """;

    private readonly IDbConnectionFactory _connectionFactory;

    public BuddyRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<BuddyAccount?> GetAccountAsync(string account, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            $"SELECT u.id AS UserId, u.username AS Account, {DisplayNameSql} AS DisplayName FROM users u LEFT JOIN user_game_info g ON g.user_id = u.id WHERE u.username = @Account;",
            new { Account = account },
            cancellationToken: ct));
        return row is null ? null : new BuddyAccount(row.UserId, row.Account, row.DisplayName);
    }

    public async Task<IReadOnlyList<BuddyRelation>> GetFriendsAsync(string account, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await connection.QueryAsync<RelationRow>(new CommandDefinition(
            $"""
            SELECT u.username AS Account, {DisplayNameSql} AS DisplayName, b.category AS GroupName
            FROM buddies b
            JOIN users u ON u.username = b.buddy_user_id
            LEFT JOIN user_game_info g ON g.user_id = u.id
            WHERE b.user_id = @Account
            ORDER BY u.username;
            """,
            new { Account = account },
            cancellationToken: ct));
        return rows.Select(r => new BuddyRelation(r.Account, r.DisplayName, r.GroupName)).ToList();
    }

    public async Task<bool> AddFriendAsync(string owner, string friend, CancellationToken ct = default)
    {
        if (string.Equals(owner, friend, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            int exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM users WHERE username = @Friend;", new { Friend = friend }, tx, cancellationToken: ct));
            int already = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM buddies WHERE user_id = @Owner AND buddy_user_id = @Friend;", new { Owner = owner, Friend = friend }, tx, cancellationToken: ct));
            if (exists == 0 || already > 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            const string insert = "INSERT INTO buddies (user_id, category, buddy_user_id) VALUES (@Owner, '', @Friend);";
            await connection.ExecuteAsync(new CommandDefinition(insert, new { Owner = owner, Friend = friend }, tx, cancellationToken: ct));
            int reverse = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM buddies WHERE user_id = @Friend AND buddy_user_id = @Owner;", new { Owner = owner, Friend = friend }, tx, cancellationToken: ct));
            if (reverse == 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(insert, new { Owner = friend, Friend = owner }, tx, cancellationToken: ct));
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

    public async Task<bool> RemoveFriendAsync(string owner, string friend, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        int removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM buddies WHERE (user_id = @Owner AND buddy_user_id = @Friend) OR (user_id = @Friend AND buddy_user_id = @Owner);",
            new { Owner = owner, Friend = friend },
            cancellationToken: ct));
        return removed > 0;
    }

    public async Task SetDisplayNameAsync(int userId, string displayName, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_game_info SET buddy_name = @Name WHERE user_id = @UserId;",
            new { Name = displayName.Length > 16 ? displayName[..16] : displayName, UserId = userId },
            cancellationToken: ct));
    }

    private sealed class AccountRow
    {
        public int UserId { get; set; }

        public string Account { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class RelationRow
    {
        public string Account { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string GroupName { get; set; } = string.Empty;
    }
}
