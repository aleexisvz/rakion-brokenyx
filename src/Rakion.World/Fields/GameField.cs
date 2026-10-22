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

    /// <summary>Rules with the legacy field meanings; the properties above keep the packet-record names.</summary>
    public FieldRules Rules { get; set; } = FieldRules.Default;

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

    /// <summary>
    /// Wire state of every slot (see <see cref="FieldSlotInfo"/>) with its player. Inferred: the host shows as
    /// waiting rather than ready, as the legacy start check never asks the host to be ready.
    /// </summary>
    public IReadOnlyList<(byte State, FieldPlayer? Player)> SnapshotSlots()
    {
        lock (_lock)
        {
            var slots = new (byte, FieldPlayer?)[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                slots[i] = _slots[i] is { } player
                    ? (SlotStateOf(player), player)
                    : (_isSlotClosed[i] ? FieldSlotInfo.Closed : FieldSlotInfo.Empty, null);
            }

            return slots;
        }
    }

    public static byte SlotStateOf(FieldPlayer player) =>
        player.IsInGame ? FieldSlotInfo.InGame
        : player.IsReady && !player.IsMaster ? FieldSlotInfo.Ready
        : FieldSlotInfo.Waiting;

    public byte MasterSlot => _playersBySession.Values.FirstOrDefault(p => p.IsMaster)?.SlotIndex ?? 0;

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

    /// <summary>
    /// Host starts the match, following the legacy checks (RakionWorldServ.exe 0x4079D0, Observed): modes 1, 3 and 4
    /// need equal team sizes, mode 2 needs two players, and every player other than the host must be ready.
    /// Returns null when the request is ignored (not the host, or the match is already running).
    /// </summary>
    public byte? TryStartGame(FieldPlayer requester)
    {
        lock (_lock)
        {
            if (!requester.IsMaster || IsInProgress)
            {
                return null;
            }

            var players = _playersBySession.Values.ToList();
            int red = players.Count(p => p.SlotIndex < 10);
            int blue = players.Count - red;

            // UNKNOWN: the legacy server skips these checks when a per-slot flag of the host is 1, and compares a
            // second pair of per-team counters (result 2); neither is reproduced.
            bool teamsOk = Rules.Mode switch
            {
                1 or 3 or 4 => red == blue,
                2 => players.Count >= 2,
                _ => true,
            };
            if (!teamsOk)
            {
                return FieldGameStartResponsePacket.TeamsNotBalanced;
            }

            if (players.Any(p => !p.IsMaster && !p.IsReady))
            {
                return FieldGameStartResponsePacket.NotAllReady;
            }

            IsInProgress = true;
            foreach (var player in players)
            {
                player.IsInGame = true;
            }

            return FieldGameStartResponsePacket.Started;
        }
    }

    /// <summary>
    /// A room member joins the running match (RakionWorldServ.exe 0x407C70, Observed).
    /// UNKNOWN: the legacy team-size check for late joiners (result 2) is not reproduced.
    /// </summary>
    public byte TryEnterGame(FieldPlayer player)
    {
        lock (_lock)
        {
            if (!IsInProgress)
            {
                return FieldGameEnterResponsePacket.NotRunning;
            }

            player.IsInGame = true;
            return FieldGameEnterResponsePacket.Entered;
        }
    }

    /// <summary>
    /// A player leaves the running match back to the waiting room (RakionWorldServ.exe 0x407E00, Observed: the slot
    /// goes back to "not ready"). Returns true when nobody is left in the match, which ends it.
    /// </summary>
    public bool ExitGame(FieldPlayer player)
    {
        lock (_lock)
        {
            player.IsInGame = false;
            player.IsReady = player.IsMaster;
            return IsInProgress && !_playersBySession.Values.Any(p => p.IsInGame);
        }
    }

    /// <summary>Ends the match: the room goes back to waiting and every player to "not ready" (RakionWorldServ.exe 0x407BE0, Observed).</summary>
    public void EndGame()
    {
        lock (_lock)
        {
            IsInProgress = false;
            foreach (var player in _playersBySession.Values)
            {
                player.IsInGame = false;
                player.IsReady = player.IsMaster;
            }
        }
    }

    public bool TryAddPlayer(WorldSession session, string password, out FieldPlayer? newPlayer, out byte failureReason)
    {
        lock (_lock)
        {
            newPlayer = null;

            // Legacy order (RakionWorldServ.exe 0x406F40): running match, level range, free slot, password.
            // MaxPlayers is not a capacity (it holds the mode byte, see FieldRules); open slots decide.
            if (_playersBySession.ContainsKey(session.Connection.ConnectionId))
            {
                failureReason = FieldPlayerEnterPacket.RoomUnavailable;
                return false;
            }

            if (IsInProgress)
            {
                failureReason = FieldPlayerEnterPacket.InProgress;
                return false;
            }

            if (Rules.Mode is >= 1 and <= 4 && !Rules.AllowsLevel(session.ActiveCharacter?.Level ?? 0))
            {
                failureReason = FieldPlayerEnterPacket.LevelOutOfRange;
                return false;
            }

            if (HasPassword && !string.Equals(Password, password, StringComparison.Ordinal))
            {
                failureReason = FieldPlayerEnterPacket.WrongPassword;
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
                failureReason = FieldPlayerEnterPacket.RoomFull;
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
        byte openSlots;
        lock (_lock)
        {
            openSlots = (byte)_isSlotClosed.Count(closed => !closed);
        }

        return new FieldListEntry(
            RoomNumber: Number,
            HasPassword: HasPassword,
            IsInProgress: IsInProgress,
            Kind: Rules.Kind,
            Mode: Rules.Mode,
            MinLevel: Rules.MinLevel,
            MaxLevel: Rules.MaxLevel,
            RuleExtra: Rules.Extra,
            Unknown: 0,
            Option: Rules.Option,
            CurrentPlayers: PlayerCount,
            MaxPlayers: openSlots,
            RoomName: Name);
    }
}
