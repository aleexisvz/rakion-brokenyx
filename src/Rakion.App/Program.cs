using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rakion.App.HostedServices;
using Rakion.Broker.Configuration;
using Rakion.Broker.Handlers;
using Rakion.Broker.Hosting;
using Rakion.Broker.Services;
using Rakion.Core.Utilities;
using Rakion.Database.Connection;
using Rakion.Database.Repositories;
using Rakion.Database.Security;
using Rakion.World.Configuration;
using Rakion.World.Handlers;
using Rakion.World.Hosting;

if (args.Contains("--generate-xfs"))
{
    string targetPath = args.FirstOrDefault(a => a.EndsWith(".xfs", StringComparison.OrdinalIgnoreCase)) ?? "NyxLauncherEnc.xfs";
    byte[] encBytes = XfsPacker.CreateNyxLauncherEnc("127.0.0.1", 40706, "http://127.0.0.1:8080/fetch/fetch.php");
    File.WriteAllBytes(targetPath, encBytes);
    Console.WriteLine($"[+] NyxLauncherEnc.xfs generated successfully at '{targetPath}' ({encBytes.Length} bytes).");
    return;
}

Console.WriteLine("""
======================================================================
  RAKION BROKENYX - CLEAN-ROOM SERVER EMULATOR (.NET 10)
  Unified Server Host:
    - Broker TCP Server:     Port 40706
    - World TCP Server:      Port 40708
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
builder.Services.AddSingleton<IInventoryRepository, InventoryRepository>();

// Configuration options
builder.Services.Configure<BrokerOptions>(builder.Configuration.GetSection(BrokerOptions.SectionName));
builder.Services.Configure<WorldOptions>(builder.Configuration.GetSection(WorldOptions.SectionName));

// Server listeners & packet handlers
builder.Services.AddSingleton<BrokerPacketHandler>();
builder.Services.AddSingleton<BrokerServer>();
builder.Services.AddSingleton<AutoFetchServer>();

builder.Services.AddSingleton<WorldPacketHandler>();
builder.Services.AddSingleton<WorldServer>();

// Hosted background services
builder.Services.AddHostedService<BrokerHostedService>();
builder.Services.AddHostedService<WorldHostedService>();

IHost host = builder.Build();
await host.RunAsync();

