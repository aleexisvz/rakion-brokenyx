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

    private readonly Lock _listIds = new();
    private readonly bool[] _listIdUsed = new bool[256];
    private int _lastListId;

    /// <summary>
    /// Adds the session with a free user-list id. The id is a byte on the wire (0x1E entries, 0x20 removal) and only
    /// has to be unique inside the channel, as in the original server where a channel holds at most 255 players.
    /// Ids are handed out round-robin so a freed id is not reused at once: a late 0x20 for a player who left must not
    /// remove a newcomer that got the same id. False when all 255 ids are taken.
    /// </summary>
    public bool TryAdd(WorldSession session)
    {
        lock (_listIds)
        {
            for (int step = 1; step <= 255; step++)
            {
                int id = (_lastListId + step - 1) % 255 + 1;
                if (!_listIdUsed[id])
                {
                    _listIdUsed[id] = true;
                    _lastListId = id;
                    session.ChannelListId = (byte)id;
                    Sessions[session.Connection.ConnectionId] = session;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Removes the session and frees its id. The session keeps the id so the others can still be told to drop it.</summary>
    public bool Remove(WorldSession session)
    {
        lock (_listIds)
        {
            if (!Sessions.TryRemove(session.Connection.ConnectionId, out _))
            {
                return false;
            }

            _listIdUsed[session.ChannelListId] = false;
            return true;
        }
    }

    public LobbyChannel(byte index, string name, ChannelType type, byte maxLevel, byte maxPlayers)
    {
        Index = index;
        Name = name;
        Type = type;
        MaxLevel = maxLevel;
        MaxPlayers = maxPlayers;
    }
}
