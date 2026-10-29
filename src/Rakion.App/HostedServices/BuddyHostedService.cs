using Microsoft.Extensions.Hosting;
using Rakion.Buddy;

namespace Rakion.App.HostedServices;

/// <summary>Starts and stops the Messenger (Buddy) server.</summary>
public sealed class BuddyHostedService : IHostedService
{
    private readonly BuddyServer _server;

    public BuddyHostedService(BuddyServer server)
    {
        _server = server;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _server.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => _server.StopAsync();
}
