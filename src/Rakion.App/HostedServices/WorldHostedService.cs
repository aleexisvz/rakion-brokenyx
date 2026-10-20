using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rakion.World.Hosting;

namespace Rakion.App.HostedServices;

/// <summary>
/// Hosted service managing the lifecycle of the Rakion World TCP server (port 40708).
/// </summary>
public sealed class WorldHostedService : IHostedService
{
    private readonly WorldServer _worldServer;
    private readonly ILogger<WorldHostedService> _logger;

    public WorldHostedService(
        WorldServer worldServer,
        ILogger<WorldHostedService> logger)
    {
        _worldServer = worldServer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Rakion World Server (TCP port 40708)...");
        _worldServer.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Rakion World Server...");
        await _worldServer.StopAsync().ConfigureAwait(false);
    }
}

