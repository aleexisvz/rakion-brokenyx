using Rakion.Core.Enums;

namespace Rakion.World.Configuration;

/// <summary>
/// Configuration for a single lobby channel on the World Server.
/// </summary>
public sealed class ChannelConfig
{
    public byte Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; } = ChannelType.Free;
    public byte MaxLevel { get; set; } = 99;
    public byte MaxPlayers { get; set; } = 100;
}
