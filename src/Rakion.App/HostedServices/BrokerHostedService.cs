using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rakion.Broker.Hosting;

namespace Rakion.App.HostedServices;

/// <summary>
/// Hosted service managing the lifecycle of the Rakion Broker TCP server (port 40706)
/// and the Softnyx AutoFetch HTTP server.
/// </summary>
public sealed class BrokerHostedService : IHostedService
{
    private readonly BrokerServer _brokerServer;
    private readonly AutoFetchServer _autoFetchServer;
    private readonly ILogger<BrokerHostedService> _logger;

    public BrokerHostedService(
        BrokerServer brokerServer,
        AutoFetchServer autoFetchServer,
        ILogger<BrokerHostedService> logger)
    {
        _brokerServer = brokerServer;
        _autoFetchServer = autoFetchServer;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Rakion Broker (TCP) and AutoFetch (HTTP) services...");
        _brokerServer.Start();
        _autoFetchServer.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Rakion Broker and AutoFetch services...");
        await _brokerServer.StopAsync().ConfigureAwait(false);
        await _autoFetchServer.StopAsync().ConfigureAwait(false);
    }
}
