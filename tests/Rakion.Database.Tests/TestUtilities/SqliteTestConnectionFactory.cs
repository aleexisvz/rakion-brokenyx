using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Rakion.Database.Connection;

namespace Rakion.Database.Tests.TestUtilities;

public sealed class SqliteTestConnectionFactory : IDbConnectionFactory, IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly string _connectionString;

    public SqliteTestConnectionFactory(string dbName)
    {
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
        InitializeSchema(_keepAliveConnection);
    }

    public async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void InitializeSchema(SqliteConnection conn)
    {
        const string schema = """
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                email TEXT NOT NULL DEFAULT '',
                country INTEGER NOT NULL DEFAULT 0,
                no_country_update INTEGER NOT NULL DEFAULT 0,
                authority INTEGER NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NULL
            );

            CREATE TABLE IF NOT EXISTS user_game_info (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL UNIQUE,
                username TEXT NOT NULL,
                gold INTEGER NOT NULL DEFAULT 10000,
                tutorial_completed INTEGER NOT NULL DEFAULT 0,
                stage_level_free INTEGER NOT NULL DEFAULT 0,
                slot_count INTEGER NOT NULL DEFAULT 3,
                bag_count INTEGER NOT NULL DEFAULT 1,
                is_banned INTEGER NOT NULL DEFAULT 0,
                ban_date DATETIME NULL,
                ban_reason TEXT NULL,
                clan_id INTEGER NOT NULL DEFAULT 0,
                clan_point INTEGER NOT NULL DEFAULT 0,
                clan_rank INTEGER NOT NULL DEFAULT 0,
                clan_grade INTEGER NOT NULL DEFAULT 0,
                buddy_name TEXT NULL,
                power_time INTEGER NOT NULL DEFAULT 0,
                power_level_point INTEGER NOT NULL DEFAULT 0,
                power_time_date DATETIME NULL,
                active_character_name TEXT NULL,
                rank_grade INTEGER NOT NULL DEFAULT 0,
                country INTEGER NOT NULL DEFAULT 9,
                last_connect DATETIME NULL,
                created_at DATETIME NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cash_balances (
                user_id INTEGER PRIMARY KEY,
                username TEXT NOT NULL,
                cash_amount INTEGER NOT NULL DEFAULT 0,
                updated_at DATETIME NOT NULL
            );

            CREATE TABLE IF NOT EXISTS characters (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                name TEXT NOT NULL UNIQUE,
                is_used INTEGER NOT NULL DEFAULT 1,
                delete_key TEXT NOT NULL DEFAULT 'sirmaster',
                auth INTEGER NOT NULL DEFAULT 0,
                class_id INTEGER NOT NULL DEFAULT 0,
                level INTEGER NOT NULL DEFAULT 1,
                win_count INTEGER NOT NULL DEFAULT 0,
                loss_count INTEGER NOT NULL DEFAULT 0,
                draw_count INTEGER NOT NULL DEFAULT 0,
                exp INTEGER NOT NULL DEFAULT 0,
                level_point INTEGER NOT NULL DEFAULT 0,
                slot_index INTEGER NOT NULL DEFAULT 0,
                hit1 INTEGER NOT NULL DEFAULT 0,
                hit2 INTEGER NOT NULL DEFAULT 0,
                hit3 INTEGER NOT NULL DEFAULT 0,
                hit4 INTEGER NOT NULL DEFAULT 0,
                chit INTEGER NOT NULL DEFAULT 0,
                hp INTEGER NOT NULL DEFAULT 0,
                ap INTEGER NOT NULL DEFAULT 0,
                attack_speed INTEGER NOT NULL DEFAULT 0,
                speed INTEGER NOT NULL DEFAULT 0,
                max_cp INTEGER NOT NULL DEFAULT 0,
                rank_grade INTEGER NOT NULL DEFAULT 0,
                total_rank INTEGER NOT NULL DEFAULT 0,
                class_rank INTEGER NOT NULL DEFAULT 0,
                potion_slot_count INTEGER NOT NULL DEFAULT 3,
                change_time DATETIME NULL,
                create_time DATETIME NOT NULL
            );

            CREATE TABLE IF NOT EXISTS item_templates (
                id INTEGER PRIMARY KEY,
                type_id INTEGER NOT NULL DEFAULT 0,
                class_id INTEGER NOT NULL DEFAULT 0,
                required_level INTEGER NOT NULL DEFAULT 0,
                shop_type INTEGER NOT NULL DEFAULT 0,
                gold_price INTEGER NOT NULL DEFAULT 0,
                cash_price INTEGER NOT NULL DEFAULT 0,
                hit1 INTEGER NOT NULL DEFAULT 0,
                hit2 INTEGER NOT NULL DEFAULT 0,
                hit3 INTEGER NOT NULL DEFAULT 0,
                hit4 INTEGER NOT NULL DEFAULT 0,
                chit INTEGER NOT NULL DEFAULT 0,
                ap INTEGER NOT NULL DEFAULT 0,
                hp INTEGER NOT NULL DEFAULT 0,
                max_cp INTEGER NOT NULL DEFAULT 0,
                power INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS class_level_info (
                class_id INTEGER NOT NULL,
                level INTEGER NOT NULL,
                exp INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (class_id, level)
            );

            CREATE TABLE IF NOT EXISTS user_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                character_id INTEGER NOT NULL DEFAULT 0,
                item_id INTEGER NOT NULL,
                serial_number INTEGER NOT NULL DEFAULT 8000,
                serial_type INTEGER NOT NULL DEFAULT 3,
                level INTEGER NOT NULL DEFAULT 1,
                limit_time_minutes INTEGER NOT NULL DEFAULT 0,
                slot_index INTEGER NOT NULL DEFAULT 1,
                exp INTEGER NOT NULL DEFAULT 0
            );
            """;

        conn.Execute(schema);
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }
}

