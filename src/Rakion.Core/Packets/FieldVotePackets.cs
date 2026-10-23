using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Opens a kick vote during a match: [u8 TargetSlot][string Reason].
/// Evidence: Observed in engine.dll SendFieldGameVoteOpen (0x5D, u8 + string) and RakionWorldServ.exe 0x425A70, which
/// copies the reason as a C string.
/// </summary>
public sealed record FieldGameVoteOpenRequestPacket(byte TargetSlot, string Reason) : IPacket
{
    public const int MaxReasonLength = 64;

    public ushort Opcode => (ushort)ClientOpcode.FieldGameVoteOpen;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(TargetSlot);
        writer.WriteNullTerminatedString(Reason);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameVoteOpenRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte target = reader.ReadByte();
        string reason = reader.ReadNullTerminatedString();
        packet = new FieldGameVoteOpenRequestPacket(target, reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason);
        return true;
    }
}

/// <summary>
/// A vote on the running kick vote: [u8 Choice]; 1 yes, 2 no, 3 abstain (Derived from the counter at
/// RakionWorldServ.exe 0x409848, which counts the three values separately; the opener is stored as 1).
/// Evidence: Observed in engine.dll SendFieldGameVote (0x5E, one byte).
/// </summary>
public sealed record FieldGameVoteRequestPacket(byte Choice) : IPacket
{
    public const byte Yes = 1;
    public const byte No = 2;
    public const byte Abstain = 3;

    public ushort Opcode => (ushort)ClientOpcode.FieldGameVote;

    public void Serialize(PacketWriter writer) => writer.WriteByte(Choice);

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameVoteRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 1 || span[0] is < Yes or > Abstain)
        {
            return false;
        }

        packet = new FieldGameVoteRequestPacket(span[0]);
        return true;
    }
}

/// <summary>
/// Kick vote opened, sent to the other loaded players: [u8 TargetSlot][string Reason].
/// Evidence: Observed in RakionWorldServ.exe 0x40A5B2 (built and sent to every loaded slot but the opener and the
/// target) and engine.dll 0x36194360 (u8 + string).
/// </summary>
public sealed record FieldGameVoteOpenPacket(byte TargetSlot, string Reason) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldGameVoteOpen;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(TargetSlot);
        writer.WriteNullTerminatedString(Reason);
    }
}

/// <summary>
/// Kick vote refusal or result: [u8 Result] and, for 0, [u8 Cancelled][u8 Voters][u8 Yes][u8 No][u8 Abstain][u8 Target].
/// Evidence: Observed in RakionWorldServ.exe 0x4090B0 (result sent to the loaded players) and engine.dll 0x361943D0
/// (reads the six bytes only for result 0). Refusals Observed in 0x40A420 / 0x4098E0: 1 a vote is running, 2 already
/// voted or already opened one, 4 no vote, 5 voting on oneself, 6 ban list full, 7 the target is the room master,
/// 9 fewer than 3 players.
/// </summary>
public sealed record FieldGameVoteResultPacket(
    byte Result,
    bool Cancelled = false,
    byte Voters = 0,
    byte Yes = 0,
    byte No = 0,
    byte Abstain = 0,
    byte TargetSlot = 0) : IPacket
{
    public const byte Finished = 0;
    public const byte VoteRunning = 1;
    public const byte AlreadyVoted = 2;
    public const byte NoVote = 4;
    public const byte OwnVote = 5;
    public const byte TargetIsMaster = 7;
    public const byte NotEnoughPlayers = 9;

    public ushort Opcode => (ushort)ServerOpcode.FieldGameVoteResult;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        if (Result != Finished)
        {
            return;
        }

        writer.WriteByte(Cancelled ? (byte)1 : (byte)0);
        writer.WriteByte(Voters);
        writer.WriteByte(Yes);
        writer.WriteByte(No);
        writer.WriteByte(Abstain);
        writer.WriteByte(TargetSlot);
    }
}
