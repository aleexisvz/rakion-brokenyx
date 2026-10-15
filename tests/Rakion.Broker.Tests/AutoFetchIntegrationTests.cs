using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;
using Rakion.Broker.Hosting;

namespace Rakion.Broker.Tests;

[TestClass]
public sealed class AutoFetchIntegrationTests
{
    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [TestMethod]
    public async Task AutoFetch_FetchPhpRequest_ReturnsSoftnyxFlatFormat()
    {
        int port = GetAvailablePort();
        var options = Options.Create(new BrokerOptions
        {
            AutoFetch = new AutoFetchOptions
            {
                Enabled = true,
                ListenIp = "127.0.0.1",
                Port = port,
                NoticeUrl = "http://localhost/notice.html",
                PatchUrl = "http://localhost/patch/",
                TargetVersion = 1026
            }
        });

        await using var server = new AutoFetchServer(options, NullLogger<AutoFetchServer>.Instance);
        server.Start();
        Assert.IsTrue(server.IsRunning);

        using var client = new HttpClient();
        string url = $"http://127.0.0.1:{port}/fetch/fetch.php?11001&258";
        var response = await client.GetAsync(new Uri(url));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();

        // Softnyx protocol format:
        //   +<NoticeUrl>
        //   =<PatchUrl>
        //   ~0;0;<TargetVersion>
        string[] lines = body.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(3, lines);
        Assert.AreEqual("+http://localhost/notice.html", lines[0]);
        Assert.AreEqual("=http://localhost/patch/", lines[1]);
        Assert.AreEqual("~0;0;258", lines[2]);
    }

    [TestMethod]
    public async Task AutoFetch_RootPath_ReturnsOk()
    {
        int port = GetAvailablePort();
        var options = Options.Create(new BrokerOptions
        {
            AutoFetch = new AutoFetchOptions
            {
                Enabled = true,
                ListenIp = "127.0.0.1",
                Port = port
            }
        });

        await using var server = new AutoFetchServer(options, NullLogger<AutoFetchServer>.Instance);
        server.Start();

        using var client = new HttpClient();
        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("OK", body);
    }

    [TestMethod]
    public async Task AutoFetch_NonGetMethod_ReturnsMethodNotAllowed()
    {
        int port = GetAvailablePort();
        var options = Options.Create(new BrokerOptions
        {
            AutoFetch = new AutoFetchOptions
            {
                Enabled = true,
                ListenIp = "127.0.0.1",
                Port = port
            }
        });

        await using var server = new AutoFetchServer(options, NullLogger<AutoFetchServer>.Instance);
        server.Start();

        using var client = new HttpClient();
        var response = await client.PostAsync(new Uri($"http://127.0.0.1:{port}/fetch/fetch.php"), new StringContent("test"));

        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [TestMethod]
    public void AutoFetch_DisabledInOptions_DoesNotListen()
    {
        var options = Options.Create(new BrokerOptions
        {
            AutoFetch = new AutoFetchOptions
            {
                Enabled = false
            }
        });

        using var server = new AutoFetchServer(options, NullLogger<AutoFetchServer>.Instance);
        server.Start();

        Assert.IsFalse(server.IsRunning);
    }
}
