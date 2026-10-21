using Rakion.Core.Enums;

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

    /// <summary>
    /// Configured lobby channels. Defaults to Beginner 01, Free 01, and Clan 01.
    /// </summary>
    public List<ChannelConfig> Channels { get; set; } = new()
    {
        new() { Index = 0, Name = "Beginner 01", Type = ChannelType.Beginner, MaxLevel = 15, MaxPlayers = 100 },
        new() { Index = 1, Name = "Free 01", Type = ChannelType.Free, MaxLevel = 99, MaxPlayers = 100 },
        new() { Index = 2, Name = "Clan 01", Type = ChannelType.Clan, MaxLevel = 99, MaxPlayers = 100 }
    };
}

