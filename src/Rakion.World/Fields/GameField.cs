using System.Collections.Concurrent;
using Rakion.Core.Packets;
using Rakion.World.Session;

namespace Rakion.World.Fields;

/// <summary>
/// In-memory representation of an active game room (field) inside a channel.
/// Evidence: Observed in engine.dll room layout and RakionWorldServ.exe 0x4075A0, 0x407910.
/// Slots 0..9 represent Team 0 (Red), slots 10..19 represent Team 1 (Blue).
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

    private readonly FieldPlayer?[] _slots = new FieldPlayer?[20];
    private readonly bool[] _isSlotClosed = new bool[20];
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
        MaxPlayers = Math.Clamp(maxPlayers, (byte)2, (byte)20);
        RoundTime = roundTime;
        Rounds = rounds;
        SubMode = subMode;
        LevelLimit = levelLimit;
        IsInProgress = false;

        // Initialize default closed slots outside the requested MaxPlayers capacity
        int halfCapacity = Math.Clamp(MaxPlayers / 2, 1, 10);
        for (int i = halfCapacity; i < 10; i++)
        {
            _isSlotClosed[i] = true;
        }

        for (int i = 10 + halfCapacity; i < 20; i++)
        {
            _isSlotClosed[i] = true;
        }

        HostUserId = (uint)(hostSession.User?.Id ?? 0);
        HostLevel = (ushort)(hostSession.ActiveCharacter?.Level ?? 1);
        HostCharacterName = hostSession.ActiveCharacter?.Name ?? string.Empty;

        // Host is always in Slot 0 (Team 0 / Red)
        var hostPlayer = new FieldPlayer(hostSession, slotIndex: 0, team: 0, isMaster: true);
        _slots[0] = hostPlayer;
        _playersBySession[hostSession.Connection.ConnectionId] = hostPlayer;
    }

    public byte PlayerCount => (byte)_playersBySession.Count;

    public IReadOnlyCollection<FieldPlayer> Players => _playersBySession.Values.ToList();

    public bool IsSlotClosed(byte slotIndex) => slotIndex < _isSlotClosed.Length && _isSlotClosed[slotIndex];

    public bool TryGetPlayer(Guid connectionId, out FieldPlayer? player) =>
        _playersBySession.TryGetValue(connectionId, out player);

    public bool TryChangeSlotStatus(FieldPlayer requester, byte slotIndex, byte requestedStatus, out byte newStatus)
    {
        lock (_lock)
        {
            newStatus = requestedStatus;
            if (!requester.IsMaster || slotIndex >= _slots.Length)
            {
                return false;
            }

            // Cannot close a slot that currently has a player
            if (_slots[slotIndex] is not null)
            {
                return false;
            }

            if (requestedStatus == 0)
            {
                // Request to close slot
                _isSlotClosed[slotIndex] = true;
                newStatus = 0;
                return true;
            }
            else
            {
                // Request to open slot
                _isSlotClosed[slotIndex] = false;
                newStatus = 1;
                return true;
            }
        }
    }

    public bool TryChangeTeam(FieldPlayer player, out byte oldSlot, out byte newSlot, out byte result)
    {
        lock (_lock)
        {
            oldSlot = player.SlotIndex;
            newSlot = oldSlot;

            int searchStart = oldSlot < 10 ? 10 : 0;
            int searchEnd = oldSlot < 10 ? 20 : 10;

            int targetSlot = -1;
            for (int i = searchStart; i < searchEnd; i++)
            {
                if (_slots[i] is null && !_isSlotClosed[i])
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot < 0)
            {
                result = 2; // Team full / no available slot
                return false;
            }

            newSlot = (byte)targetSlot;
            _slots[oldSlot] = null;
            _slots[newSlot] = player;
            player.SlotIndex = newSlot;
            player.Team = (byte)(newSlot < 10 ? 0 : 1);
            result = 0; // Success
            return true;
        }
    }

    public bool TryChangeRule(
        FieldPlayer requester,
        string name,
        string password,
        byte mode,
        byte map,
        byte maxPlayers,
        ushort roundTime,
        byte rounds,
        byte subMode,
        byte levelLimit)
    {
        lock (_lock)
        {
            if (!requester.IsMaster)
            {
                return false;
            }

            Name = name;
            Password = password;
            Mode = mode;
            Map = map;
            MaxPlayers = Math.Clamp(maxPlayers, (byte)2, (byte)20);
            RoundTime = (byte)roundTime;
            Rounds = rounds;
            SubMode = subMode;
            LevelLimit = levelLimit;
            return true;
        }
    }

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

            // Count current players per team to balance placement
            int countTeam0 = 0;
            int countTeam1 = 0;
            for (int i = 0; i < 10; i++)
            {
                if (_slots[i] is not null)
                {
                    countTeam0++;
                }
            }

            for (int i = 10; i < 20; i++)
            {
                if (_slots[i] is not null)
                {
                    countTeam1++;
                }
            }

            // Prefer team with fewer players (or Team 1 if equal, since host is in Team 0)
            int firstStart = countTeam0 > countTeam1 ? 10 : 0;
            int firstEnd = countTeam0 > countTeam1 ? 20 : 10;
            int secondStart = countTeam0 > countTeam1 ? 0 : 10;
            int secondEnd = countTeam0 > countTeam1 ? 10 : 20;

            int availableSlot = -1;
            for (int i = firstStart; i < firstEnd; i++)
            {
                if (_slots[i] is null && !_isSlotClosed[i])
                {
                    availableSlot = i;
                    break;
                }
            }

            if (availableSlot < 0)
            {
                for (int i = secondStart; i < secondEnd; i++)
                {
                    if (_slots[i] is null && !_isSlotClosed[i])
                    {
                        availableSlot = i;
                        break;
                    }
                }
            }

            if (availableSlot < 0)
            {
                failureReason = 3; // Room full
                return false;
            }

            byte team = (byte)(availableSlot < 10 ? 0 : 1);
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
