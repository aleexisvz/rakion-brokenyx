namespace Rakion.World.Configuration;

/// <summary>
/// Configuration options for the Rakion World Server (TCP port 40708).
/// </summary>
public sealed class WorldOptions
{
    public const string SectionName = "World";

    public string ListenIp { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 40708;
    public int UdpPort1 { get; set; } = 40708;
    public int UdpPort2 { get; set; } = 40709;
    public ushort ServerId { get; set; } = 1;
    public string ServerName { get; set; } = "WorldServer 1";
    public ushort MaxUsers { get; set; } = 500;
}

