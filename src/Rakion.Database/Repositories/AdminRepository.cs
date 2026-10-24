using Dapper;
using Rakion.Database.Connection;

namespace Rakion.Database.Repositories;

public sealed class AdminRepository : IAdminRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AdminRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> BanAsync(int userId, DateTime? untilUtc, string reason, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        int updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_game_info SET is_banned = @Permanent, ban_date = @Until, ban_reason = @Reason WHERE user_id = @UserId;",
            new { Permanent = untilUtc is null, Until = untilUtc, Reason = reason.Length > 64 ? reason[..64] : reason, UserId = userId },
            cancellationToken: ct));
        return updated > 0;
    }

    public async Task<bool> UnbanAsync(int userId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        int updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE user_game_info SET is_banned = 0, ban_date = NULL, ban_reason = NULL WHERE user_id = @UserId;",
            new { UserId = userId },
            cancellationToken: ct));
        return updated > 0;
    }

    public async Task<byte?> GiveItemAsync(int userId, int itemId, int boxSlots, long exp = 0, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            int known = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM item_templates WHERE id = @ItemId;", new { ItemId = itemId }, tx, cancellationToken: ct));
            var used = (await connection.QueryAsync<int>(new CommandDefinition(
                    "SELECT slot_index FROM user_items WHERE user_id = @UserId AND character_id = 0;", new { UserId = userId }, tx, cancellationToken: ct)))
                .ToHashSet();
            int slot = Enumerable.Range(0, boxSlots).FirstOrDefault(s => !used.Contains(s), -1);
            if (known == 0 || slot < 0)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO user_items (user_id, character_id, item_id, level, limit_time_minutes, slot_index, exp) VALUES (@UserId, 0, @ItemId, 1, 0, @Slot, @Exp);",
                new { UserId = userId, ItemId = itemId, Slot = slot, Exp = exp },
                tx,
                cancellationToken: ct));
            await tx.CommitAsync(ct);
            return (byte)slot;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
