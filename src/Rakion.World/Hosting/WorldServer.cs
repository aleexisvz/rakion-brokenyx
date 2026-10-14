using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Network.Tcp;
using Rakion.World.Configuration;
using Rakion.World.Handlers;

namespace Rakion.World.Hosting;

/// <summary>
/// TCP Listener server running on port 40708 managing live game sessions.
/// </summary>
public sealed class WorldServer : IAsyncDisposable
{
    private readonly IOptions<WorldOptions> _options;
    private readonly WorldPacketHandler _handler;
    private readonly ILogger<WorldServer> _logger;
    private TcpServer? _tcpServer;

    public EndPoint? LocalEndPoint => _tcpServer?.LocalEndPoint;
    public int ActiveConnectionsCount => _tcpServer?.ActiveConnectionsCount ?? 0;
    public bool IsRunning => _tcpServer != null;

    public WorldServer(
        IOptions<WorldOptions> options,
        WorldPacketHandler handler,
        ILogger<WorldServer> logger)
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
        IPAddress ip = IPAddress.TryParse(opts.ListenIp, out var parsedIp) ? parsedIp : IPAddress.Any;
        var endPoint = new IPEndPoint(ip, opts.Port);

        _tcpServer = new TcpServer(endPoint, _handler, _logger);
        _tcpServer.Start();

        _logger.LogInformation(
            "Rakion World Server started on {EndPoint} (ServerId: {Id}, Name: '{Name}').",
            _tcpServer.LocalEndPoint,
            opts.ServerId,
            opts.ServerName);
    }

    public async Task StopAsync()
    {
        if (_tcpServer is not null)
        {
            _logger.LogInformation("Stopping Rakion World Server...");
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
