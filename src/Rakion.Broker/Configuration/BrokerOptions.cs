namespace Rakion.Broker.Configuration;

/// <summary>
/// Root configuration options for the Rakion.Broker service.
/// </summary>
public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    public string ListenIp { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 40706;
    public List<GameServerConfig> GameServers { get; set; } = [];
    public AutoFetchOptions AutoFetch { get; set; } = new();
}
