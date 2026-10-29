using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rakion.App.HostedServices;
using Rakion.Broker.Configuration;
using Rakion.Buddy;
using Rakion.Broker.Handlers;
using Rakion.Broker.Hosting;
using Rakion.Broker.Services;
using Rakion.Core.Utilities;
using Rakion.Database.Connection;
using Rakion.Database.Repositories;
using Rakion.Database.Security;
using Rakion.Database.Entities;
using Rakion.Database.Import;
using Rakion.Maintenance;
using Rakion.Maintenance.Ranking;
using Rakion.World.Channels;
using Rakion.World.Chat;
using Rakion.World.Configuration;
using Rakion.World.Features.Admin;
using Rakion.World.Features.Economy;
using Rakion.World.Handlers;
using Rakion.World.Hosting;
using Rakion.World.Modules;
using Rakion.World.Session;

if (args.Contains("--generate-xfs"))
{
    string targetPath = args.FirstOrDefault(a => a.EndsWith(".xfs", StringComparison.OrdinalIgnoreCase)) ?? "NyxLauncherEnc.xfs";
    byte[] encBytes = XfsPacker.CreateNyxLauncherEnc("127.0.0.1", 40706, "http://127.0.0.1:8080/fetch/fetch.php");
    File.WriteAllBytes(targetPath, encBytes);
    Console.WriteLine($"[+] NyxLauncherEnc.xfs generated successfully at '{targetPath}' ({encBytes.Length} bytes).");
    return;
}

if (args.Contains("--import-items"))
{
    // Loads item templates from the operator's local legacy dump (e.g. audit/DB/rakion_all.sql).
    int index = Array.IndexOf(args, "--import-items");
    string? dumpPath = args.Length > index + 1 && !args[index + 1].StartsWith("--") ? args[index + 1] : null;
    if (dumpPath is null || !File.Exists(dumpPath))
    {
        Console.WriteLine("[!] Usage: --import-items <path to legacy SQL dump containing iteminfo rows>");
        return;
    }

    IConfiguration importConfig = new ConfigurationBuilder()
        .AddJsonFile("config/appsettings.json", optional: true)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("RAKION_")
        .Build();

    string importConnection = importConfig.GetConnectionString("RakionDb")
        ?? importConfig["ConnectionStrings:RakionDb"]
        ?? "Server=localhost;Port=3306;Database=rakion;User=root;Password=root;CharSet=utf8mb4;";

    using var dumpReader = new StreamReader(dumpPath);
    var templates = LegacyItemCatalogParser.Parse(dumpReader);
    var importFactory = new MySqlConnectionFactory(importConnection);
    int imported = await new InventoryRepository(importFactory).UpsertItemTemplatesAsync(templates);
    Console.WriteLine($"[+] Imported {imported} item templates from '{dumpPath}'.");

    // The creature level table (legacy NPCInfo) matches the client; the character level table comes from the client
    // (--import-levels) because the dump's ClassLevelInfo is another game version.
    using var creatureReader = new StreamReader(dumpPath);
    int creatureLevels = await new MatchResultRepository(importFactory).UpsertCreatureLevelsAsync(LegacyCreatureLevelParser.Parse(creatureReader));
    Console.WriteLine($"[+] Imported {creatureLevels} creature level rows from '{dumpPath}'.");
    return;
}

if (args.Contains("--import-levels"))
{
    // Loads the experience table from the operator's client DataSetup.xfs, the totals the client itself shows.
    int index = Array.IndexOf(args, "--import-levels");
    string? xfsPath = args.Length > index + 1 && !args[index + 1].StartsWith("--") ? args[index + 1] : null;
    if (xfsPath is null || !File.Exists(xfsPath))
    {
        Console.WriteLine("[!] Usage: --import-levels <path to the client's DataSetup.xfs>");
        return;
    }

    var levelRows = ClientLevelTableParser.Parse(await File.ReadAllBytesAsync(xfsPath));
    if (levelRows is null)
    {
        Console.WriteLine($"[!] No level table found in '{xfsPath}'.");
        return;
    }

    IConfiguration levelConfig = new ConfigurationBuilder()
        .AddJsonFile("config/appsettings.json", optional: true)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("RAKION_")
        .Build();

    string levelConnection = levelConfig.GetConnectionString("RakionDb")
        ?? levelConfig["ConnectionStrings:RakionDb"]
        ?? "Server=localhost;Port=3306;Database=rakion;User=root;Password=root;CharSet=utf8mb4;";

    int levels = await new MatchResultRepository(new MySqlConnectionFactory(levelConnection)).UpsertClassLevelsAsync(levelRows);
    Console.WriteLine($"[+] Imported {levels} level table rows from '{xfsPath}'.");
    return;
}

if (args.Contains("--create-user"))
{
    int index = Array.IndexOf(args, "--create-user");
    string username = args.Length > index + 1 && !args[index + 1].StartsWith("--") ? args[index + 1] : "admin";
    string password = args.Length > index + 2 && !args[index + 2].StartsWith("--") ? args[index + 2] : "admin";
    bool isAdmin = args.Contains("--admin") || username.Equals("admin", StringComparison.OrdinalIgnoreCase);
    int authority = isAdmin ? 100 : 0;

    IConfiguration config = new ConfigurationBuilder()
        .AddJsonFile("config/appsettings.json", optional: true)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables("RAKION_")
        .Build();

    string connectionString = config.GetConnectionString("RakionDb")
        ?? config["ConnectionStrings:RakionDb"]
        ?? "Server=localhost;Port=3306;Database=rakion;User=root;Password=root;CharSet=utf8mb4;";

    var dbFactory = new MySqlConnectionFactory(connectionString);
    var userRepo = new UserRepository(dbFactory);
    var hasher = new PasswordHasher();

    var existingUser = await userRepo.GetByUsernameAsync(username);
    if (existingUser is not null)
    {
        Console.WriteLine($"[!] User '{username}' already exists (ID: {existingUser.Id}, Authority: {existingUser.Authority}).");
        return;
    }

    var user = new UserEntity
    {
        Username = username,
        PasswordHash = password,
        Email = $"{username}@brokenyx.local",
        Country = 9,
        Authority = authority
    };

    int newId = await userRepo.CreateUserAsync(user, initialGold: 100_000, initialCash: 50_000);
    Console.WriteLine($"[+] User '{username}' created successfully! (ID: {newId}, Authority: {authority}, Role: {(isAdmin ? "Admin/GM" : "Player")}).");
    return;
}

Console.WriteLine("""
======================================================================
  RAKION BROKENYX - CLEAN-ROOM SERVER EMULATOR (.NET 10)
  Unified Server Host:
    - Broker TCP Server:     Port 40706
    - World TCP Server:      Port 40708
    - World UDP Listeners:   Ports 40708 & 40709 (NAT Probe / Ping)
    - AutoFetch HTTP Server: Port 8080 (Softnyx launcher notice & patch)
======================================================================
""");

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Search for configuration in config/appsettings.json or working directory appsettings.json
builder.Configuration
    .AddJsonFile("config/appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables("RAKION_");

// Database configuration
string connString = builder.Configuration.GetConnectionString("RakionDb")
    ?? builder.Configuration["ConnectionStrings:RakionDb"]
    ?? "Server=localhost;Port=3306;Database=rakion;User=root;Password=root;CharSet=utf8mb4;";

builder.Services.AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connString));

// Core services & security
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ISessionTicketService, SessionTicketService>();

// Database repositories
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ICharacterRepository, CharacterRepository>();
builder.Services.AddSingleton<IAccountBalanceRepository, AccountBalanceRepository>();
builder.Services.AddSingleton<IAccountUpgradeRepository, AccountUpgradeRepository>();
builder.Services.AddSingleton<IAdminRepository, AdminRepository>();
builder.Services.AddSingleton<IPresentRepository, PresentRepository>();
builder.Services.AddSingleton<IChatCommandInterceptor, GmChatCommands>();
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection(MaintenanceOptions.SectionName));
builder.Services.AddSingleton<IRankingRepository, RankingRepository>();
builder.Services.AddSingleton<RankingJob>();
builder.Services.AddSingleton<IRankingTrigger, RankingTrigger>();
builder.Services.AddHostedService<RankingScheduler>();
builder.Services.AddSingleton<IInventoryRepository, InventoryRepository>();
builder.Services.AddSingleton<IMatchResultRepository, MatchResultRepository>();

// Configuration options
builder.Services.Configure<BrokerOptions>(builder.Configuration.GetSection(BrokerOptions.SectionName));
builder.Services.Configure<WorldOptions>(builder.Configuration.GetSection(WorldOptions.SectionName));

// Server listeners & packet handlers
builder.Services.AddSingleton<BrokerPacketHandler>();
builder.Services.AddSingleton<BrokerServer>();
builder.Services.AddSingleton<AutoFetchServer>();

builder.Services.AddSingleton<IChannelManager, ChannelManager>();
builder.Services.AddSingleton<ISessionSender, WorldSessionSender>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IWorldOpcodeModule, InventoryModule>();
builder.Services.AddSingleton<IWorldOpcodeModule, PresentModule>();
builder.Services.AddSingleton<IWorldOpcodeModule, ShopModule>();
builder.Services.AddSingleton<IWorldOpcodeModule, StatPointModule>();
builder.Services.AddSingleton<IWorldOpcodeModule, AccountUpgradeModule>();
builder.Services.AddSingleton<IWorldOpcodeModule, PowerUserModule>();
builder.Services.AddSingleton<IWorldOpcodeModule, BlacksmithModule>();
builder.Services.AddSingleton<IAccountInventoryProvider, AccountInventoryProvider>();
builder.Services.AddSingleton<UdpEndpointRegistry>();
builder.Services.AddSingleton<WorldPacketHandler>();
builder.Services.AddSingleton<WorldUdpPacketHandler>();
builder.Services.AddSingleton<WorldServer>();

// Hosted background services
builder.Services.AddHostedService<BrokerHostedService>();
builder.Services.Configure<BuddyOptions>(builder.Configuration.GetSection(BuddyOptions.SectionName));
builder.Services.AddSingleton<IBuddyRepository, BuddyRepository>();
builder.Services.AddSingleton<BuddyPacketHandler>();
builder.Services.AddSingleton<BuddyServer>();
builder.Services.AddHostedService<BuddyHostedService>();
builder.Services.AddHostedService<WorldHostedService>();

IHost host = builder.Build();

// Ensure default administrator account exists
try
{
    using var scope = host.Services.CreateScope();
    var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var existingAdmin = await userRepo.GetByUsernameAsync("admin");
    if (existingAdmin is null)
    {
        var adminUser = new UserEntity
        {
            Username = "admin",
            PasswordHash = "admin",
            Email = "admin@brokenyx.local",
            Country = 9,
            Authority = 100
        };
        int adminId = await userRepo.CreateUserAsync(adminUser, initialGold: 100_000, initialCash: 50_000);
        Console.WriteLine($"[+] Auto-seeded administrator account: Username='admin', Password='admin' (ID: {adminId}, Authority: 100).");
    }
    else if (existingAdmin.PasswordHash.StartsWith("$2", StringComparison.Ordinal) &&
             hasher.VerifyPassword("admin", existingAdmin.PasswordHash, out _))
    {
        await userRepo.UpdatePasswordHashAsync(existingAdmin.Id, "admin");
        Console.WriteLine($"[+] Migrated administrator account to challenge-compatible format.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[!] Database connectivity notice: {ex.Message}");
}

await host.RunAsync();

