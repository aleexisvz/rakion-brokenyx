using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Network.Tcp;
using Rakion.Network.Udp;
using Rakion.World.Configuration;
using Rakion.World.Handlers;

namespace Rakion.World.Hosting;

/// <summary>
/// World Server managing TCP game sessions (port 40708) and dual UDP listeners (ports 40708 & 40709) for NAT hole punching.
/// </summary>
public sealed class WorldServer : IAsyncDisposable
{
    private readonly IOptions<WorldOptions> _options;
    private readonly WorldPacketHandler _handler;
    private readonly WorldUdpPacketHandler _udpHandler;
    private readonly ILogger<WorldServer> _logger;

    private TcpServer? _tcpServer;
    private UdpServer? _udpServer1;
    private UdpServer? _udpServer2;

    public EndPoint? LocalEndPoint => _tcpServer?.LocalEndPoint;
    public EndPoint? LocalUdpEndPoint1 => _udpServer1?.LocalEndPoint;
    public EndPoint? LocalUdpEndPoint2 => _udpServer2?.LocalEndPoint;
    public int ActiveConnectionsCount => _tcpServer?.ActiveConnectionsCount ?? 0;
    public bool IsRunning => _tcpServer != null;

    public WorldServer(
        IOptions<WorldOptions> options,
        WorldPacketHandler handler,
        WorldUdpPacketHandler udpHandler,
        ILogger<WorldServer> logger)
    {
        _options = options;
        _handler = handler;
        _udpHandler = udpHandler;
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

        // 1. TCP Server (Port 40708)
        var tcpEndPoint = new IPEndPoint(ip, opts.Port);
        _tcpServer = new TcpServer(tcpEndPoint, _handler, _logger);
        _tcpServer.Start();

        // 2. Dual UDP Listeners (Port 40708 & Port 40709)
        var udpEndPoint1 = new IPEndPoint(ip, opts.UdpPort1);
        _udpServer1 = new UdpServer(udpEndPoint1, _udpHandler, _logger);
        _udpServer1.Start();

        var udpEndPoint2 = new IPEndPoint(ip, opts.UdpPort2);
        _udpServer2 = new UdpServer(udpEndPoint2, _udpHandler, _logger);
        _udpServer2.Start();

        _logger.LogInformation(
            "Rakion World Server started on TCP {TcpEp}, UDP1 {UdpEp1}, UDP2 {UdpEp2} (ServerId: {Id}, Name: '{Name}').",
            _tcpServer.LocalEndPoint,
            _udpServer1.LocalEndPoint,
            _udpServer2.LocalEndPoint,
            opts.ServerId,
            opts.ServerName);
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping Rakion World Server...");

        if (_tcpServer is not null)
        {
            await _tcpServer.StopAsync().ConfigureAwait(false);
            await _tcpServer.DisposeAsync().ConfigureAwait(false);
            _tcpServer = null;
        }

        if (_udpServer1 is not null)
        {
            await _udpServer1.DisposeAsync().ConfigureAwait(false);
            _udpServer1 = null;
        }

        if (_udpServer2 is not null)
        {
            await _udpServer2.DisposeAsync().ConfigureAwait(false);
            _udpServer2 = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

