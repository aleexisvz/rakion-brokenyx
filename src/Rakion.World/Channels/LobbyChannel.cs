using System.Collections.Concurrent;
using Rakion.Core.Enums;
using Rakion.World.Session;

namespace Rakion.World.Channels;

/// <summary>
/// In-memory representation of an active lobby channel with connected player sessions.
/// </summary>
public sealed class LobbyChannel
{
    public byte Index { get; }
    public string Name { get; }
    public ChannelType Type { get; }
    public byte MaxLevel { get; }
    public byte MaxPlayers { get; }
    public ConcurrentDictionary<Guid, WorldSession> Sessions { get; } = new();
    public Rakion.World.Fields.FieldManager FieldManager { get; } = new();

    public byte CurrentPlayers => (byte)Math.Min(Sessions.Count, 255);

    public LobbyChannel(byte index, string name, ChannelType type, byte maxLevel, byte maxPlayers)
    {
        Index = index;
        Name = name;
        Type = type;
        MaxLevel = maxLevel;
        MaxPlayers = maxPlayers;
    }
}
