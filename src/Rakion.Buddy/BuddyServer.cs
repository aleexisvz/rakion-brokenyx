using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Network.Tcp;
using Rakion.Network.Udp;

namespace Rakion.Buddy;

/// <summary>Messenger server: TCP and UDP on the same port (8500 by default).</summary>
public sealed class BuddyServer : IAsyncDisposable
{
    private readonly IOptions<BuddyOptions> _options;
    private readonly BuddyPacketHandler _handler;
    private readonly ILogger<BuddyServer> _logger;
    private TcpServer? _tcp;
    private UdpServer? _udp;

    public BuddyServer(IOptions<BuddyOptions> options, BuddyPacketHandler handler, ILogger<BuddyServer> logger)
    {
        _options = options;
        _handler = handler;
        _logger = logger;
    }

    public EndPoint? LocalEndPoint => _tcp?.LocalEndPoint;

    public void Start()
    {
        var options = _options.Value;
        if (!options.Enabled || _tcp is not null)
        {
            return;
        }

        IPAddress ip = IPAddress.TryParse(options.ListenIp, out var parsed) ? parsed : IPAddress.Any;
        _tcp = new TcpServer(new IPEndPoint(ip, options.Port), _handler, _logger);
        _tcp.Start();
        var tcpEndPoint = (IPEndPoint)_tcp.LocalEndPoint!;
        _udp = new UdpServer(new IPEndPoint(ip, tcpEndPoint.Port), _handler, _logger);
        _udp.Start();
        _logger.LogInformation("Rakion Messenger (Buddy) server started on {EndPoint} (TCP and UDP).", tcpEndPoint);
    }

    public async Task StopAsync()
    {
        if (_tcp is not null)
        {
            await _tcp.StopAsync().ConfigureAwait(false);
            await _tcp.DisposeAsync().ConfigureAwait(false);
            _tcp = null;
        }

        if (_udp is not null)
        {
            await _udp.DisposeAsync().ConfigureAwait(false);
            _udp = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
