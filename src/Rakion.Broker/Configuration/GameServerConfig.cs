namespace Rakion.Broker.Configuration;

/// <summary>
/// Defines the endpoint and capacity settings for an announced game / world server.
/// Corresponds to entries in GameServers.ini.
/// </summary>
public sealed class GameServerConfig
{
    public ushort Id { get; set; } = 1;
    public string Name { get; set; } = "WorldServer 1";
    public string Ip { get; set; } = "127.0.0.1";
    public ushort Port { get; set; } = 40708;
    public ushort MaxSlots { get; set; } = 1000;
    public ushort MaxSalas { get; set; } = 2000;
    public bool IsOnline { get; set; } = true;
}
