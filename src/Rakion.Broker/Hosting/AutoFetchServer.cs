using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;

namespace Rakion.Broker.Hosting;

/// <summary>
/// Mini HTTP server emulating Softnyx AutoFetch (fetch.php) protocol for NyxLauncher.
/// Responds to GET /fetch/fetch.php or /fetch.php with version and notice URLs.
/// </summary>
public sealed class AutoFetchServer : IDisposable, IAsyncDisposable
{
    private readonly IOptions<BrokerOptions> _options;
    private readonly ILogger<AutoFetchServer> _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public bool IsRunning => _listener?.IsListening ?? false;

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

        if (_listener is not null)
        {
            return;
        }

        _listener = new HttpListener();
        string prefix = $"http://{autoFetchOpts.ListenIp}:{autoFetchOpts.Port}/";
        _listener.Prefixes.Add(prefix);

        _cts = new CancellationTokenSource();
        _listener.Start();
        _logger.LogInformation("AutoFetch HTTP server started on {Prefix}", prefix);

        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => ProcessRequestAsync(context), CancellationToken.None);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing incoming HTTP AutoFetch request.");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try
        {
            string path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            _logger.LogInformation("AutoFetch HTTP {Method} {Path}?{Query}", request.HttpMethod, path, request.Url?.Query);

            if (request.HttpMethod != "GET")
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            // Handle fetch.php endpoint (e.g. /fetch/fetch.php or /fetch.php)
            if (path.EndsWith("fetch.php", StringComparison.OrdinalIgnoreCase))
            {
                var opts = _options.Value.AutoFetch;
                // Softnyx flat format:
                //   +<NoticeUrl>
                //   =<FileUrl>
                //   ~<Count>;<TotalSize>;<VerLimit>
                string responseBody = $"+{opts.NoticeUrl}\r\n={opts.PatchUrl}\r\n~0;0;{opts.TargetVersion}\r\n";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(responseBody);

                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = "text/plain; charset=utf-8";
                response.ContentLength64 = bodyBytes.Length;
                await response.OutputStream.WriteAsync(bodyBytes).ConfigureAwait(false);
            }
            else
            {
                // Health check or default 200 OK
                byte[] okBytes = "OK"u8.ToArray();
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = "text/plain";
                response.ContentLength64 = okBytes.Length;
                await response.OutputStream.WriteAsync(okBytes).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AutoFetch HTTP response.");
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
        }
        finally
        {
            response.Close();
        }
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_listener is not null)
        {
            try
            {
                _listener.Stop();
                _listener.Close();
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
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException)
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
