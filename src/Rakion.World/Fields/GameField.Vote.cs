using Rakion.Core.Packets;

namespace Rakion.World.Fields;

/// <summary>
/// Kick votes during a match (RakionWorldServ.exe 0x40A420 open, 0x4098E0 vote, 0x409810 count, 0x4090B0 result;
/// Observed). A vote lasts 60 s and ends early once every loaded player but the target voted; the target is kicked
/// when the yes votes beat the no votes and yes + no reach half of the loaded players.
/// </summary>
public sealed partial class GameField
{
    public static readonly TimeSpan VoteDuration = TimeSpan.FromSeconds(60);

    private const int MinVotePlayers = 3;

    private bool _voteActive;
    private byte _voteTarget;
    private DateTimeOffset _voteDeadline;

    /// <summary>What a finished vote produced: the result for the loaded players and the slot to kick, if any.</summary>
    public sealed record VoteOutcome(FieldGameVoteResultPacket Result, IReadOnlyList<FieldPlayer> Recipients, byte? KickSlot);

    /// <summary>
    /// Opens a vote against <paramref name="targetSlot"/>. Returns a refusal code, or null with the players to
    /// notify (every loaded player but the opener and the target).
    /// </summary>
    public (byte? Refusal, IReadOnlyList<FieldPlayer> Notify) OpenVote(FieldPlayer opener, byte targetSlot, DateTimeOffset now)
    {
        lock (_lock)
        {
            var loaded = _playersBySession.Values.Where(p => p.IsLoaded).ToList();
            byte? refusal = _voteActive ? FieldGameVoteResultPacket.VoteRunning
                : opener.VoteChoice != 0 ? FieldGameVoteResultPacket.AlreadyVoted
                : targetSlot == opener.SlotIndex ? FieldGameVoteResultPacket.OwnVote
                : targetSlot == MasterSlot ? FieldGameVoteResultPacket.TargetIsMaster
                : loaded.Count < MinVotePlayers || !loaded.Any(p => p.SlotIndex == targetSlot) ? FieldGameVoteResultPacket.NotEnoughPlayers
                : null;
            if (refusal is not null || !opener.IsLoaded)
            {
                return (refusal ?? FieldGameVoteResultPacket.NotEnoughPlayers, []);
            }

            _voteActive = true;
            _voteTarget = targetSlot;
            _voteDeadline = now + VoteDuration;
            opener.VoteChoice = FieldGameVoteRequestPacket.Yes;
            return (null, loaded.Where(p => p != opener && p.SlotIndex != targetSlot).ToList());
        }
    }

    /// <summary>Records a vote; returns a refusal code, or null and possibly the finished vote.</summary>
    public (byte? Refusal, VoteOutcome? Outcome) CastVote(FieldPlayer voter, byte choice, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_voteActive)
            {
                return (FieldGameVoteResultPacket.NoVote, null);
            }

            if (voter.VoteChoice != 0)
            {
                return (FieldGameVoteResultPacket.AlreadyVoted, null);
            }

            if (voter.SlotIndex == _voteTarget)
            {
                return (FieldGameVoteResultPacket.OwnVote, null);
            }

            voter.VoteChoice = choice;
            return (null, EvaluateVote(now, cancelled: false));
        }
    }

    /// <summary>Ends the vote when its time is up (called by the room timer).</summary>
    public VoteOutcome? TickVote(DateTimeOffset now)
    {
        lock (_lock)
        {
            return _voteActive ? EvaluateVote(now, cancelled: false) : null;
        }
    }

    /// <summary>Cancels the vote when its target leaves (legacy 0x409230).</summary>
    public VoteOutcome? CancelVoteFor(FieldPlayer leaver, DateTimeOffset now)
    {
        lock (_lock)
        {
            return _voteActive && leaver.SlotIndex == _voteTarget ? EvaluateVote(now, cancelled: true) : null;
        }
    }

    private VoteOutcome? EvaluateVote(DateTimeOffset now, bool cancelled)
    {
        var loaded = _playersBySession.Values.Where(p => p.IsLoaded).ToList();
        int yes = loaded.Count(p => p.VoteChoice == FieldGameVoteRequestPacket.Yes);
        int no = loaded.Count(p => p.VoteChoice == FieldGameVoteRequestPacket.No);
        int abstain = loaded.Count(p => p.VoteChoice == FieldGameVoteRequestPacket.Abstain);
        if (!cancelled && now < _voteDeadline && loaded.Count - 1 > yes + no + abstain)
        {
            return null;
        }

        bool kick = !cancelled && (yes + no) * 2 >= loaded.Count && yes > no && loaded.Any(p => p.SlotIndex == _voteTarget);
        var result = new FieldGameVoteResultPacket(
            FieldGameVoteResultPacket.Finished,
            cancelled,
            (byte)loaded.Count,
            (byte)yes,
            (byte)no,
            (byte)abstain,
            _voteTarget);
        var recipients = loaded.Where(p => !(cancelled && p.SlotIndex == _voteTarget)).ToList();
        byte target = _voteTarget;

        _voteActive = false;
        foreach (var player in _playersBySession.Values)
        {
            player.VoteChoice = 0;
        }

        return new VoteOutcome(result, recipients, kick ? target : null);
    }
}
