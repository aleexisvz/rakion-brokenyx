using System.Collections.Concurrent;
using Rakion.World.Session;

namespace Rakion.World.Fields;

/// <summary>
/// In-memory manager for rooms across channels.
/// </summary>
public sealed class FieldManager : IFieldManager
{
    private readonly object _syncRoot = new();

    // Map: ChannelIndex -> (RoomNumber -> GameField)
    private readonly ConcurrentDictionary<byte, ConcurrentDictionary<ushort, GameField>> _fieldsByChannel = new();

    // Map: ConnectionId -> (ChannelIndex, RoomNumber)
    private readonly ConcurrentDictionary<Guid, (byte ChannelIndex, ushort RoomNumber)> _sessionLocations = new();

    private ushort _nextRoomNumber = 1;

    public IReadOnlyList<GameField> GetFields(byte channelIndex)
    {
        if (_fieldsByChannel.TryGetValue(channelIndex, out var dict))
        {
            return dict.Values.OrderBy(f => f.Number).ToList();
        }

        return Array.Empty<GameField>();
    }

    public bool TryGetField(byte channelIndex, ushort roomNumber, out GameField? field)
    {
        field = null;
        if (_fieldsByChannel.TryGetValue(channelIndex, out var dict))
        {
            return dict.TryGetValue(roomNumber, out field);
        }

        return false;
    }

    public bool TryGetFieldBySession(Guid connectionId, out GameField? field, out FieldPlayer? player)
    {
        field = null;
        player = null;

        if (_sessionLocations.TryGetValue(connectionId, out var loc))
        {
            if (TryGetField(loc.ChannelIndex, loc.RoomNumber, out field) && field is not null)
            {
                return field.TryGetPlayer(connectionId, out player);
            }
        }

        return false;
    }

    public bool TryCreateField(
        byte channelIndex,
        WorldSession hostSession,
        string name,
        string password,
        byte mode,
        byte map,
        byte maxPlayers,
        byte roundTime,
        byte rounds,
        byte subMode,
        byte levelLimit,
        out GameField? field,
        out byte failureReason)
    {
        lock (_syncRoot)
        {
            field = null;

            if (_sessionLocations.ContainsKey(hostSession.Connection.ConnectionId))
            {
                failureReason = 1; // Already in a room
                return false;
            }

            var channelFields = _fieldsByChannel.GetOrAdd(channelIndex, _ => new ConcurrentDictionary<ushort, GameField>());

            ushort roomNumber = _nextRoomNumber++;
            if (_nextRoomNumber > 60000)
            {
                _nextRoomNumber = 1;
            }

            field = new GameField(
                roomNumber,
                channelIndex,
                name,
                password,
                mode,
                map,
                maxPlayers,
                roundTime,
                rounds,
                subMode,
                levelLimit,
                hostSession);

            channelFields[roomNumber] = field;
            _sessionLocations[hostSession.Connection.ConnectionId] = (channelIndex, roomNumber);
            hostSession.IsInField = true;

            failureReason = 0;
            return true;
        }
    }

    public bool TryJoinField(
        byte channelIndex,
        ushort roomNumber,
        WorldSession session,
        string password,
        out GameField? field,
        out FieldPlayer? player,
        out byte failureReason)
    {
        lock (_syncRoot)
        {
            field = null;
            player = null;

            if (_sessionLocations.ContainsKey(session.Connection.ConnectionId))
            {
                failureReason = 4; // Already in a field
                return false;
            }

            if (!TryGetField(channelIndex, roomNumber, out field) || field is null)
            {
                failureReason = 5; // Room does not exist
                return false;
            }

            if (!field.TryAddPlayer(session, password, out player, out failureReason))
            {
                return false;
            }

            _sessionLocations[session.Connection.ConnectionId] = (channelIndex, roomNumber);
            session.IsInField = true;
            return true;
        }
    }

    public bool TryQuickJoinField(
        byte channelIndex,
        WorldSession session,
        out GameField? field,
        out FieldPlayer? player,
        out byte failureReason)
    {
        lock (_syncRoot)
        {
            field = null;
            player = null;

            if (_sessionLocations.ContainsKey(session.Connection.ConnectionId))
            {
                failureReason = 4;
                return false;
            }

            var availableFields = GetFields(channelIndex)
                .Where(f => !f.HasPassword && !f.IsInProgress && f.PlayerCount < f.MaxPlayers)
                .OrderBy(f => f.PlayerCount)
                .ToList();

            if (availableFields.Count == 0)
            {
                failureReason = 5; // No available room
                return false;
            }

            foreach (var candidate in availableFields)
            {
                if (candidate.TryAddPlayer(session, string.Empty, out player, out failureReason))
                {
                    field = candidate;
                    _sessionLocations[session.Connection.ConnectionId] = (channelIndex, candidate.Number);
                    session.IsInField = true;
                    return true;
                }
            }

            failureReason = 3; // All candidate rooms full
            return false;
        }
    }

    public bool TryLeaveField(
        WorldSession session,
        out GameField? field,
        out FieldPlayer? player,
        out bool roomDestroyed)
    {
        lock (_syncRoot)
        {
            field = null;
            player = null;
            roomDestroyed = false;

            if (!_sessionLocations.TryRemove(session.Connection.ConnectionId, out var loc))
            {
                return false;
            }

            session.IsInField = false;

            if (_fieldsByChannel.TryGetValue(loc.ChannelIndex, out var dict) &&
                dict.TryGetValue(loc.RoomNumber, out field) && field is not null)
            {
                if (field.TryRemovePlayer(session.Connection.ConnectionId, out player, out roomDestroyed))
                {
                    if (roomDestroyed)
                    {
                        dict.TryRemove(loc.RoomNumber, out _);
                    }
                    return true;
                }
            }

            return false;
        }
    }
}
