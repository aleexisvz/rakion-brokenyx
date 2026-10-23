using Rakion.Core.Packets;

namespace Rakion.World.Fields;

/// <summary>
/// Match flow of a room: loading, rounds, scoring and the end of the match, following the legacy room
/// (RakionWorldServ.exe 0x408440 loaded, 0x4087D0 death, 0x409A00 tick; Observed). Mode numbers are the legacy ones
/// (FieldRules.Mode): 2 solo deathmatch and 3 team deathmatch score kills, 4 ends a round when a team leader dies, 0
/// and 1 only relay deaths (UNKNOWN: their stage logic).
/// </summary>
public sealed partial class GameField
{
    /// <summary>Pause between a round end and the next round (0x3A98 ms, Observed).</summary>
    public static readonly TimeSpan RoundEndPause = TimeSpan.FromSeconds(15);

    private enum RoundState
    {
        None,
        WaitingForLoad,
        Running,
        Ended,
    }

    private RoundState _roundState;
    private DateTimeOffset _deadline;
    private byte _redLeader = FieldGameRoundStartPacket.NoLeader;
    private byte _blueLeader = FieldGameRoundStartPacket.NoLeader;
    private byte _redScore;
    private byte _blueScore;
    private byte _redRoundWins;
    private byte _blueRoundWins;
    private byte _lastWinner;

    public byte Round { get; private set; }

    public IReadOnlyList<FieldPlayer> LoadedPlayers
    {
        get
        {
            lock (_lock)
            {
                return _playersBySession.Values.Where(p => p.IsLoaded).ToList();
            }
        }
    }

    /// <summary>
    /// A member finished loading the map. Returns the round start to send and whether it goes to every loaded member
    /// (the last one loaded, first round) or only to this member (joining a running round); null when nothing is sent.
    /// </summary>
    public (FieldGameRoundStartPacket Packet, bool ToAllLoaded)? MarkLoaded(FieldPlayer player, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!IsInProgress || !player.IsInGame || player.IsLoaded)
            {
                return null;
            }

            player.IsLoaded = true;
            if (_roundState is RoundState.Running or RoundState.Ended)
            {
                return (BuildRoundStart(now), false);
            }

            if (_playersBySession.Values.Any(p => p.IsInGame && !p.IsLoaded))
            {
                return null;
            }

            Round = 1;
            BeginRound(now);
            return (BuildRoundStart(now), true);
        }
    }

    /// <summary>
    /// The member in <paramref name="victim"/> died. Returns the death broadcast and, when it ended the round, the round
    /// end; null when the death is ignored (no running round, victim not loaded).
    /// </summary>
    public (FieldGameDiePacket Death, FieldGameRoundEndPacket? RoundEnd)? RecordDeath(FieldPlayer victim, byte cause, byte killerSlot, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_roundState != RoundState.Running || !victim.IsLoaded)
            {
                return null;
            }

            var killer = killerSlot < _slots.Length ? _slots[killerSlot] : null;
            byte points = cause == FieldGameDieRequestPacket.DoublePoints ? (byte)2 : (byte)1;
            byte valueA;
            byte valueB;
            byte? roundWinner = null;

            switch (Rules.Mode)
            {
                case 2:
                    // Solo deathmatch: the killer's own count; a self-inflicted death takes one away (0x4088E1).
                    if (killer is null || !killer.IsLoaded)
                    {
                        return null;
                    }

                    killer.Kills = cause == FieldGameDieRequestPacket.SelfInflicted
                        ? (byte)Math.Max(killer.Kills - 1, 0)
                        : (byte)Math.Min(killer.Kills + points, byte.MaxValue);
                    valueA = victim.Kills;
                    valueB = killer.Kills;
                    if (killer.Kills >= Rules.Goal)
                    {
                        roundWinner = _lastWinner;
                    }

                    break;

                case 3:
                    // Team deathmatch: a self-inflicted or double-point death scores for the victim's opponents,
                    // any other for the killer's team (0x408A2D).
                    bool redScores = cause is FieldGameDieRequestPacket.SelfInflicted or FieldGameDieRequestPacket.DoublePoints
                        ? victim.SlotIndex >= 10
                        : killerSlot < 10;
                    if (redScores)
                    {
                        _redScore = (byte)Math.Min(_redScore + points, byte.MaxValue);
                    }
                    else
                    {
                        _blueScore = (byte)Math.Min(_blueScore + points, byte.MaxValue);
                    }

                    valueA = _redScore;
                    valueB = _blueScore;
                    if (_redScore >= Rules.Goal)
                    {
                        roundWinner = FieldGameRoundEndPacket.RedWins;
                    }
                    else if (_blueScore >= Rules.Goal)
                    {
                        roundWinner = FieldGameRoundEndPacket.BlueWins;
                    }

                    break;

                case 4:
                    // Leader wars: the round ends when a team leader dies (0x408ECF / 0x405400).
                    valueA = victim.Kills;
                    valueB = killer?.Kills ?? 0;
                    if (victim.SlotIndex == _redLeader)
                    {
                        roundWinner = FieldGameRoundEndPacket.BlueWins;
                    }
                    else if (victim.SlotIndex == _blueLeader)
                    {
                        roundWinner = FieldGameRoundEndPacket.RedWins;
                    }

                    break;

                default:
                    valueA = killerSlot;
                    valueB = killerSlot;
                    break;
            }

            var death = new FieldGameDiePacket(victim.SlotIndex, cause, killerSlot, valueA, valueB);
            var roundEnd = roundWinner is { } winner ? EndRound(winner, endedByGoal: true, now) : null;
            return (death, roundEnd);
        }
    }

    /// <summary>What the match timer produced (at most one per tick).</summary>
    public sealed record TickResult(
        FieldGameRoundEndPacket? RoundEnd = null,
        FieldGameNextRoundPacket? NextRound = null,
        byte? GameEnd = null);

    /// <summary>
    /// Advances the timers (legacy tick 0x409A00): a running round times out into a round end decided by score, and
    /// after the round end pause the next round starts, or the match ends after the last round or when a side is empty.
    /// </summary>
    public TickResult? Tick(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!IsInProgress || now < _deadline)
            {
                return null;
            }

            if (_roundState == RoundState.Running)
            {
                // Inferred: only team deathmatch compares scores; the other modes keep counters that are not tracked.
                byte winner = Rules.Mode switch
                {
                    3 when _redScore > _blueScore => FieldGameRoundEndPacket.RedWins,
                    3 when _blueScore > _redScore => FieldGameRoundEndPacket.BlueWins,
                    2 => _lastWinner,
                    _ => FieldGameRoundEndPacket.Draw,
                };
                return new TickResult(RoundEnd: EndRound(winner, endedByGoal: false, now));
            }

            if (_roundState != RoundState.Ended)
            {
                return null;
            }

            byte rounds = Math.Max(Rules.Option, (byte)1);
            if (Round >= rounds)
            {
                return new TickResult(GameEnd: FieldGameEndBroadcastPacket.Finished);
            }

            int red = _playersBySession.Values.Count(p => p.IsInGame && p.SlotIndex < 10);
            int blue = _playersBySession.Values.Count(p => p.IsInGame && p.SlotIndex >= 10);
            if (Rules.Mode is 1 or 3 or 4 && (red == 0 || blue == 0))
            {
                return new TickResult(GameEnd: FieldGameEndBroadcastPacket.TeamEmpty);
            }

            if (Rules.Mode == 2 && red + blue < 2)
            {
                return new TickResult(GameEnd: FieldGameEndBroadcastPacket.NotEnoughPlayers);
            }

            Round++;
            BeginRound(now);
            return new TickResult(NextRound: new FieldGameNextRoundPacket(Round, _redLeader, _blueLeader));
        }
    }

    private void ResetMatch()
    {
        Round = 0;
        _roundState = RoundState.WaitingForLoad;
        _deadline = DateTimeOffset.MaxValue;
        _redRoundWins = _blueRoundWins = 0;
        _lastWinner = FieldGameRoundEndPacket.BlueWins;
    }

    private void BeginRound(DateTimeOffset now)
    {
        _roundState = RoundState.Running;
        _deadline = now.AddSeconds(Rules.TimeLimit + 3);
        _redScore = _blueScore = 0;
        foreach (var player in _playersBySession.Values)
        {
            // Inferred: solo kills count per round, like the team scores the legacy tick resets.
            player.Kills = 0;
        }

        _redLeader = _blueLeader = FieldGameRoundStartPacket.NoLeader;
        if (Rules.Mode == 4)
        {
            // The highest-level loaded member of each team (0x4086D9 / 0x409C40, Observed).
            var loaded = _playersBySession.Values.Where(p => p.IsLoaded).ToList();
            _redLeader = LeaderOf(loaded.Where(p => p.SlotIndex < 10));
            _blueLeader = LeaderOf(loaded.Where(p => p.SlotIndex >= 10));
        }
    }

    private static byte LeaderOf(IEnumerable<FieldPlayer> team) =>
        team.OrderByDescending(p => p.Session.ActiveCharacter?.Level ?? 0)
            .Select(p => p.SlotIndex)
            .DefaultIfEmpty(FieldGameRoundStartPacket.NoLeader)
            .First();

    private FieldGameRoundEndPacket EndRound(byte winner, bool endedByGoal, DateTimeOffset now)
    {
        if (winner == FieldGameRoundEndPacket.RedWins)
        {
            _redRoundWins++;
        }
        else if (winner == FieldGameRoundEndPacket.BlueWins && Rules.Mode != 2)
        {
            _blueRoundWins++;
        }

        _lastWinner = winner;
        _roundState = RoundState.Ended;
        _deadline = now + RoundEndPause;
        return new FieldGameRoundEndPacket(endedByGoal ? (byte)1 : (byte)0, winner, _redRoundWins, _blueRoundWins);
    }

    private FieldGameRoundStartPacket BuildRoundStart(DateTimeOffset now)
    {
        double secondsLeft = _roundState == RoundState.Running ? Math.Clamp((_deadline - now).TotalSeconds, 0, ushort.MaxValue) : 0;
        return new FieldGameRoundStartPacket(Round, (ushort)secondsLeft, _redRoundWins, _blueRoundWins, _redLeader, _blueLeader);
    }
}
