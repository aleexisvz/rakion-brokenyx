using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;
using Rakion.Broker.Handlers;
using Rakion.Network.Tcp;

namespace Rakion.Broker.Hosting;

/// <summary>
/// Hosted TCP Broker server listening on port 40706 for Rakion client login and server list dispatch.
/// </summary>
public sealed class BrokerServer : IAsyncDisposable
{
    private readonly IOptions<BrokerOptions> _options;
    private readonly BrokerPacketHandler _handler;
    private readonly ILogger<BrokerServer> _logger;
    private TcpServer? _tcpServer;

    public EndPoint? LocalEndPoint => _tcpServer?.LocalEndPoint;
    public int ActiveConnectionsCount => _tcpServer?.ActiveConnectionsCount ?? 0;
    public bool IsRunning => _tcpServer != null;

    public BrokerServer(
        IOptions<BrokerOptions> options,
        BrokerPacketHandler handler,
        ILogger<BrokerServer> logger)
    {
        _options = options;
        _handler = handler;
        _logger = logger;
    }

    public void Start()
    {
        if (_tcpServer is not null)
        {
            return;
        }

        var opts = _options.Value;
        IPAddress ip = IPAddress.TryParse(opts.ListenIp, out var parsed) ? parsed : IPAddress.Any;
        var endPoint = new IPEndPoint(ip, opts.Port);

        _tcpServer = new TcpServer(endPoint, _handler, _logger);
        _tcpServer.Start();
        _logger.LogInformation("Rakion BrokerServer started on {EndPoint}.", _tcpServer.LocalEndPoint);
    }

    public async Task StopAsync()
    {
        if (_tcpServer is not null)
        {
            _logger.LogInformation("Stopping Rakion BrokerServer...");
            await _tcpServer.StopAsync().ConfigureAwait(false);
            await _tcpServer.DisposeAsync().ConfigureAwait(false);
            _tcpServer = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
