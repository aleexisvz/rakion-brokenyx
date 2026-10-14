using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;

namespace Rakion.Broker.Hosting;

/// <summary>
/// Mini HTTP server emulating Softnyx AutoFetch (fetch.php) protocol for NyxLauncher.
/// Responds to GET /fetch/fetch.php or /fetch.php with version and notice URLs.
/// Uses standard TcpListener to bind cleanly to 0.0.0.0 or any IP without Windows HttpListener (HTTP.sys) ACL restrictions.
/// </summary>
public sealed class AutoFetchServer : IDisposable, IAsyncDisposable
{
    private readonly IOptions<BrokerOptions> _options;
    private readonly ILogger<AutoFetchServer> _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public AutoFetchServer(
        IOptions<BrokerOptions> options,
        ILogger<AutoFetchServer> logger)
    {
        _options = options;
        _logger = logger;
    }

    public void Start()
    {
        var autoFetchOpts = _options.Value.AutoFetch;
        if (!autoFetchOpts.Enabled)
        {
            _logger.LogInformation("AutoFetch server is disabled in configuration.");
            return;
        }

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return;
        }

        IPAddress ip = IPAddress.TryParse(autoFetchOpts.ListenIp, out var parsedIp) ? parsedIp : IPAddress.Any;
        _listener = new TcpListener(ip, autoFetchOpts.Port);
        _listener.Start();

        _cts = new CancellationTokenSource();
        _logger.LogInformation("AutoFetch HTTP server started on {EndPoint}", _listener.LocalEndpoint);

        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => ProcessClientAsync(client, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting AutoFetch client connection.");
            }
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        {
            try
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(2048);
                int bytesRead;
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 2048), timeoutCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    // keep buffer for parsing
                }

                if (bytesRead <= 0)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return;
                }

                string requestText = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                ArrayPool<byte>.Shared.Return(buffer);

                // Parse HTTP request line, e.g. "GET /fetch/fetch.php?11001&258 HTTP/1.1"
                int firstLineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
                if (firstLineEnd < 0)
                {
                    firstLineEnd = requestText.IndexOf('\n');
                }

                string requestLine = firstLineEnd >= 0 ? requestText[..firstLineEnd] : requestText;
                string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                string method = parts.Length > 0 ? parts[0] : "GET";
                string urlPath = parts.Length > 1 ? parts[1] : "/";

                // Strip query string if present
                int queryIdx = urlPath.IndexOf('?');
                string path = queryIdx >= 0 ? urlPath[..queryIdx] : urlPath;
                path = path.TrimEnd('/');

                _logger.LogInformation("AutoFetch HTTP {Method} {Url}", method, urlPath);

                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] responseBytes = "HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();
                    await stream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (path.EndsWith("fetch.php", StringComparison.OrdinalIgnoreCase))
                {
                    var opts = _options.Value.AutoFetch;
                    int targetVersion = opts.TargetVersion;

                    // Parse query string, e.g. "?11001&258" or "?400&1026"
                    if (queryIdx >= 0 && queryIdx < urlPath.Length - 1)
                    {
                        string query = urlPath[(queryIdx + 1)..];
                        string[] queryParts = query.Split('&');
                        if (queryParts.Length >= 2 && int.TryParse(queryParts[1], out int clientVer))
                        {
                            targetVersion = clientVer;
                        }
                        else if (queryParts.Length >= 1)
                        {
                            if (queryParts[0] == "11001") targetVersion = 258;
                            else if (queryParts[0] == "400") targetVersion = 1026;
                        }
                    }

                    // Softnyx flat format:
                    //   +<NoticeUrl>
                    //   =<PatchUrl>
                    //   ~<Count>;<TotalSize>;<VerLimit>
                    string responseBody = $"+{opts.NoticeUrl}\r\n={opts.PatchUrl}\r\n~0;0;{targetVersion}\r\n";
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(responseBody);

                    string header = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);

                    await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    byte[] bodyBytes = "OK"u8.ToArray();
                    string header = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);

                    await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception handling AutoFetch HTTP client.");
            }
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _isRunning, 0) == 0)
        {
            return;
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_listener is not null)
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Suppress shutdown exceptions
            }

            _listener = null;
        }

        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Expected cancellation
            }

            _listenerTask = null;
        }

        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
