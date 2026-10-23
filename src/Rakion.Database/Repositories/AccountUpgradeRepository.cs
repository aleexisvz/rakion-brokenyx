using System.Data.Common;
using Dapper;
using Rakion.Database.Connection;

namespace Rakion.Database.Repositories;

/// <summary>
/// Paid account upgrades (roadmap 4.7 / 4.8), each charge and change in one guarded transaction.
/// </summary>
public sealed class AccountUpgradeRepository : IAccountUpgradeRepository
{
    private const string StatSum = "hit1 + hit2 + hit3 + hit4 + chit + hp + ap + attack_speed + speed + max_cp";

    private readonly IDbConnectionFactory _connectionFactory;

    public AccountUpgradeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<StatResetOutcome> ResetStatsAsync(int userId, int characterId, int cashPrice, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<StatResetRow>(new CommandDefinition(
                $"SELECT level AS Level, level_point AS LevelPoint, {StatSum} AS Spent FROM characters WHERE id = @CharacterId AND user_id = @UserId;",
                new { CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            int powerPoints = await GetPowerPointsAsync(connection, tx, userId, ct);
            int cash = await GetCashAsync(connection, tx, userId, ct);
            if (row is null)
            {
                await tx.RollbackAsync(ct);
                return new StatResetOutcome(StatResetStatus.CharacterNotFound, cash, 0, powerPoints);
            }

            if (row.Spent <= 0)
            {
                await tx.RollbackAsync(ct);
                return new StatResetOutcome(StatResetStatus.NothingToReset, cash, row.LevelPoint, powerPoints);
            }

            int charged = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE cash_balances SET cash_amount = cash_amount - @Price WHERE user_id = @UserId AND cash_amount >= @Price;",
                new { UserId = userId, Price = cashPrice },
                tx,
                cancellationToken: ct));
            if (charged == 0)
            {
                await tx.RollbackAsync(ct);
                return new StatResetOutcome(StatResetStatus.NotEnoughCash, cash, row.LevelPoint, powerPoints);
            }

            // Derived from the legacy agent (0x414043): the character keeps 3 points per level above 1 and the rest
            // of what was spent goes back to the account's PowerUser points.
            int total = row.Spent + row.LevelPoint + powerPoints;
            int levelPoints = Math.Min(Math.Max(row.Level - 1, 0) * 3, total);
            int newPowerPoints = total - levelPoints;
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE characters SET level_point = @LevelPoints, hit1 = 0, hit2 = 0, hit3 = 0, hit4 = 0, chit = 0, hp = 0, ap = 0, attack_speed = 0, speed = 0, max_cp = 0 WHERE id = @CharacterId AND user_id = @UserId;",
                new { LevelPoints = levelPoints, CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_game_info SET power_level_point = @PowerPoints WHERE user_id = @UserId;",
                new { PowerPoints = newPowerPoints, UserId = userId },
                tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new StatResetOutcome(StatResetStatus.Reset, cash - cashPrice, levelPoints, newPowerPoints);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<UpgradeOutcome> BuyBagAsync(int userId, int cashPrice, byte maxBags, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            byte bags = await connection.ExecuteScalarAsync<byte>(new CommandDefinition(
                "SELECT bag_count FROM user_game_info WHERE user_id = @UserId;", new { UserId = userId }, tx, cancellationToken: ct));
            if (bags >= maxBags)
            {
                return await RefuseAsync(connection, tx, userId, UpgradeStatus.AtMaximum, bags, ct);
            }

            if (!await ChargeAsync(connection, tx, userId, payWithGold: false, cashPrice, ct))
            {
                return await RefuseAsync(connection, tx, userId, UpgradeStatus.NotEnoughMoney, bags, ct);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE user_game_info SET bag_count = bag_count + 1 WHERE user_id = @UserId;", new { UserId = userId }, tx, cancellationToken: ct));
            return await CommitAsync(connection, tx, userId, (byte)(bags + 1), ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<UpgradeOutcome> BuyPotionSlotAsync(
        int userId,
        int characterId,
        Func<byte, (bool PayWithGold, int Price)?> priceForCurrentSlots,
        CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            byte? slots = await connection.ExecuteScalarAsync<byte?>(new CommandDefinition(
                "SELECT potion_slot_count FROM characters WHERE id = @CharacterId AND user_id = @UserId;",
                new { CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            if (slots is null)
            {
                return await RefuseAsync(connection, tx, userId, UpgradeStatus.CharacterNotFound, 0, ct);
            }

            if (priceForCurrentSlots(slots.Value) is not { } price)
            {
                return await RefuseAsync(connection, tx, userId, UpgradeStatus.AtMaximum, slots.Value, ct);
            }

            if (!await ChargeAsync(connection, tx, userId, price.PayWithGold, price.Price, ct))
            {
                return await RefuseAsync(connection, tx, userId, UpgradeStatus.NotEnoughMoney, slots.Value, ct);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE characters SET potion_slot_count = potion_slot_count + 1 WHERE id = @CharacterId AND user_id = @UserId;",
                new { CharacterId = characterId, UserId = userId },
                tx,
                cancellationToken: ct));
            return await CommitAsync(connection, tx, userId, (byte)(slots.Value + 1), ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<bool> ChargeAsync(DbConnection connection, DbTransaction tx, int userId, bool payWithGold, int price, CancellationToken ct)
    {
        string sql = payWithGold
            ? "UPDATE user_game_info SET gold = gold - @Price WHERE user_id = @UserId AND gold >= @Price;"
            : "UPDATE cash_balances SET cash_amount = cash_amount - @Price WHERE user_id = @UserId AND cash_amount >= @Price;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { UserId = userId, Price = price }, tx, cancellationToken: ct)) > 0;
    }

    private static async Task<UpgradeOutcome> RefuseAsync(DbConnection connection, DbTransaction tx, int userId, UpgradeStatus status, byte count, CancellationToken ct)
    {
        var (gold, cash) = await GetBalancesAsync(connection, tx, userId, ct);
        await tx.RollbackAsync(ct);
        return new UpgradeOutcome(status, gold, cash, count);
    }

    private static async Task<UpgradeOutcome> CommitAsync(DbConnection connection, DbTransaction tx, int userId, byte count, CancellationToken ct)
    {
        var (gold, cash) = await GetBalancesAsync(connection, tx, userId, ct);
        await tx.CommitAsync(ct);
        return new UpgradeOutcome(UpgradeStatus.Bought, gold, cash, count);
    }

    private static async Task<(long Gold, int Cash)> GetBalancesAsync(DbConnection connection, DbTransaction tx, int userId, CancellationToken ct)
    {
        long gold = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE((SELECT gold FROM user_game_info WHERE user_id = @UserId), 0);", new { UserId = userId }, tx, cancellationToken: ct));
        return (gold, await GetCashAsync(connection, tx, userId, ct));
    }

    private static Task<int> GetCashAsync(DbConnection connection, DbTransaction tx, int userId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE((SELECT cash_amount FROM cash_balances WHERE user_id = @UserId), 0);", new { UserId = userId }, tx, cancellationToken: ct));

    private static Task<int> GetPowerPointsAsync(DbConnection connection, DbTransaction tx, int userId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE((SELECT power_level_point FROM user_game_info WHERE user_id = @UserId), 0);", new { UserId = userId }, tx, cancellationToken: ct));

    private sealed class StatResetRow
    {
        public int Level { get; set; }

        public int LevelPoint { get; set; }

        public int Spent { get; set; }
    }
}
