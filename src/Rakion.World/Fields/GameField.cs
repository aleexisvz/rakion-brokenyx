using System.Collections.Concurrent;
using Rakion.Core.Packets;
using Rakion.World.Session;

namespace Rakion.World.Fields;

/// <summary>
/// In-memory representation of an active game room (field) inside a channel.
/// </summary>
public sealed class GameField
{
    private readonly object _lock = new();

    public ushort Number { get; }
    public byte ChannelIndex { get; }
    public string Name { get; set; }
    public string Password { get; set; }
    public byte Mode { get; set; }
    public byte Map { get; set; }
    public byte MaxPlayers { get; set; }
    public byte RoundTime { get; set; }
    public byte Rounds { get; set; }
    public byte SubMode { get; set; }
    public byte LevelLimit { get; set; }
    public bool IsInProgress { get; set; }
    public uint HostUserId { get; private set; }
    public ushort HostLevel { get; private set; }
    public string HostCharacterName { get; private set; } = string.Empty;

    public bool HasPassword => !string.IsNullOrEmpty(Password);

    private readonly FieldPlayer?[] _slots;
    private readonly ConcurrentDictionary<Guid, FieldPlayer> _playersBySession = new();

    public GameField(
        ushort number,
        byte channelIndex,
        string name,
        string password,
        byte mode,
        byte map,
        byte maxPlayers,
        byte roundTime,
        byte rounds,
        byte subMode,
        byte levelLimit,
        WorldSession hostSession)
    {
        Number = number;
        ChannelIndex = channelIndex;
        Name = name;
        Password = password;
        Mode = mode;
        Map = map;
        MaxPlayers = Math.Clamp(maxPlayers, (byte)2, (byte)16);
        RoundTime = roundTime;
        Rounds = rounds;
        SubMode = subMode;
        LevelLimit = levelLimit;
        IsInProgress = false;

        _slots = new FieldPlayer?[MaxPlayers];

        HostUserId = (uint)(hostSession.User?.Id ?? 0);
        HostLevel = (ushort)(hostSession.ActiveCharacter?.Level ?? 1);
        HostCharacterName = hostSession.ActiveCharacter?.Name ?? string.Empty;

        var hostPlayer = new FieldPlayer(hostSession, slotIndex: 0, team: 1, isMaster: true);
        _slots[0] = hostPlayer;
        _playersBySession[hostSession.Connection.ConnectionId] = hostPlayer;
    }

    public byte PlayerCount => (byte)_playersBySession.Count;

    public IReadOnlyCollection<FieldPlayer> Players => _playersBySession.Values.ToList();

    public bool TryGetPlayer(Guid connectionId, out FieldPlayer? player) =>
        _playersBySession.TryGetValue(connectionId, out player);

    public bool TryAddPlayer(WorldSession session, string password, out FieldPlayer? newPlayer, out byte failureReason)
    {
        lock (_lock)
        {
            newPlayer = null;

            if (HasPassword && !string.Equals(Password, password, StringComparison.Ordinal))
            {
                failureReason = 1; // Wrong password
                return false;
            }

            if (IsInProgress)
            {
                failureReason = 2; // Match already started
                return false;
            }

            if (PlayerCount >= MaxPlayers)
            {
                failureReason = 3; // Room full
                return false;
            }

            if (_playersBySession.ContainsKey(session.Connection.ConnectionId))
            {
                failureReason = 4; // Already inside
                return false;
            }

            int availableSlot = -1;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] is null)
                {
                    availableSlot = i;
                    break;
                }
            }

            if (availableSlot < 0)
            {
                failureReason = 3; // Room full
                return false;
            }

            byte team = (byte)((availableSlot % 2) + 1);
            var player = new FieldPlayer(session, (byte)availableSlot, team, isMaster: false);
            _slots[availableSlot] = player;
            _playersBySession[session.Connection.ConnectionId] = player;

            failureReason = 0;
            newPlayer = player;
            return true;
        }
    }

    public bool TryRemovePlayer(Guid connectionId, out FieldPlayer? removedPlayer, out bool roomBecameEmpty)
    {
        lock (_lock)
        {
            roomBecameEmpty = false;
            if (!_playersBySession.TryRemove(connectionId, out removedPlayer))
            {
                return false;
            }

            if (removedPlayer.SlotIndex < _slots.Length && _slots[removedPlayer.SlotIndex] == removedPlayer)
            {
                _slots[removedPlayer.SlotIndex] = null;
            }

            if (_playersBySession.IsEmpty)
            {
                roomBecameEmpty = true;
                return true;
            }

            if (removedPlayer.IsMaster)
            {
                FieldPlayer newHost = _playersBySession.Values.First();
                newHost.IsMaster = true;
                newHost.IsReady = true;
                HostUserId = (uint)(newHost.Session.User?.Id ?? 0);
                HostLevel = (ushort)(newHost.Session.ActiveCharacter?.Level ?? 1);
                HostCharacterName = newHost.Session.ActiveCharacter?.Name ?? string.Empty;
            }

            return true;
        }
    }

    public FieldListEntry ToFieldListEntry()
    {
        return new FieldListEntry(
            RoomNumber: Number,
            HasPassword: HasPassword,
            IsInProgress: IsInProgress,
            Mode: Mode,
            Map: Map,
            MaxPlayers: MaxPlayers,
            RoundTime: RoundTime,
            Rounds: Rounds,
            SubMode: SubMode,
            LevelLimit: LevelLimit,
            CurrentPlayers: PlayerCount,
            MaxAllowed: MaxPlayers,
            HostUserId: HostUserId,
            HostLevel: HostLevel,
            RoomName: Name,
            Extra: 0);
    }
}
