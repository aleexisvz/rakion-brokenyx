using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Round start sent to members that finished loading: [u8 Round][u16 SecondsLeft][u8 RedScore][u8 BlueScore]
/// [u8 RedLeaderSlot][u8 BlueLeaderSlot].
/// Evidence: Observed in RakionWorldServ.exe 0x408440 (9-byte send from room + 0x2BC, the round timer, + 0x2C0,
/// + 0x2C1, + 0x122, + 0x123) and engine.dll 0x36193DE0 (reader). Score and leader meanings Inferred: the leaders are
/// computed only in mode 4 (highest level per team) and are 20 (no slot) otherwise.
/// </summary>
public sealed record FieldGameRoundStartPacket(
    byte Round,
    ushort SecondsLeft,
    byte RedScore,
    byte BlueScore,
    byte RedLeaderSlot,
    byte BlueLeaderSlot) : IPacket
{
    public const byte NoLeader = 20;

    public ushort Opcode => (ushort)ServerOpcode.FieldGameRoundStart;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Round);
        writer.WriteUInt16(SecondsLeft);
        writer.WriteByte(RedScore);
        writer.WriteByte(BlueScore);
        writer.WriteByte(RedLeaderSlot);
        writer.WriteByte(BlueLeaderSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameRoundStartPacket packet)
    {
        packet = null!;
        if (span.Length < 7)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new FieldGameRoundStartPacket(
            reader.ReadByte(), reader.ReadUInt16(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        return true;
    }
}

/// <summary>
/// The sender died: [u8 Cause][u8 KillerSlot]. Cause is at most 8 and the killer slot below 20.
/// Evidence: Observed in RakionWorldServ.exe 0x424A20 (bounds checks) and engine.dll SendFieldGameDiePlayer.
/// Cause meanings Inferred from the scoring: 1 takes a point away in solo deathmatch (self-inflicted), 8 is worth two.
/// </summary>
public sealed record FieldGameDieRequestPacket(byte Cause, byte KillerSlot) : IPacket
{
    public const byte MaxCause = 8;
    public const byte SelfInflicted = 1;
    public const byte DoublePoints = 8;

    public ushort Opcode => (ushort)ClientOpcode.FieldGameDie;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Cause);
        writer.WriteByte(KillerSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameDieRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 2 || span[0] > MaxCause || span[1] >= 20)
        {
            return false;
        }

        packet = new FieldGameDieRequestPacket(span[0], span[1]);
        return true;
    }
}

/// <summary>
/// Death broadcast: [u8 Victim][u8 Cause][u8 Killer][u8 ValueA][u8 ValueB]. The values depend on the mode: solo
/// deathmatch sends the victim's and the killer's kills, team modes the red and blue team scores.
/// Evidence: Observed in RakionWorldServ.exe 0x4087D0 (writer, per-mode values).
/// </summary>
public sealed record FieldGameDiePacket(byte VictimSlot, byte Cause, byte KillerSlot, byte ValueA, byte ValueB) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldGameDie;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(VictimSlot);
        writer.WriteByte(Cause);
        writer.WriteByte(KillerSlot);
        writer.WriteByte(ValueA);
        writer.WriteByte(ValueB);
    }
}

/// <summary>
/// Round end: [u8 EndedByGoal][u8 Winner][u8 RedRoundWins][u8 BlueRoundWins]. Winner is 1 for red, 0 for blue and
/// 2 for a draw; EndedByGoal is 1 when the kill goal was reached and 0 when the time ran out.
/// Evidence: Observed in RakionWorldServ.exe 0x408E3E and 0x409FA7 (room + 0x2BD, + 0x2BF, + 0x2C0, + 0x2C1) and
/// engine.dll 0x36193E80 (reads four bytes). Winner values Observed in the code paths; meanings Inferred.
/// </summary>
public sealed record FieldGameRoundEndPacket(byte EndedByGoal, byte Winner, byte RedRoundWins, byte BlueRoundWins) : IPacket
{
    public const byte BlueWins = 0;
    public const byte RedWins = 1;
    public const byte Draw = 2;

    /// <summary>EndedByGoal values: time ran out, kill goal reached, a golem died (0x405E56, Observed).</summary>
    public const byte EndedByTime = 0;
    public const byte EndedByKills = 1;
    public const byte EndedByGolem = 2;

    public ushort Opcode => (ushort)ServerOpcode.FieldGameRoundEnd;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(EndedByGoal);
        writer.WriteByte(Winner);
        writer.WriteByte(RedRoundWins);
        writer.WriteByte(BlueRoundWins);
    }
}

/// <summary>
/// Next round: [u8 Round][u8 RedLeaderSlot][u8 BlueLeaderSlot].
/// Evidence: Observed in RakionWorldServ.exe 0x409CA7 (5-byte send) and engine.dll 0x36193E40 (reads three bytes).
/// </summary>
public sealed record FieldGameNextRoundPacket(byte Round, byte RedLeaderSlot, byte BlueLeaderSlot) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldGameNextRound;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Round);
        writer.WriteByte(RedLeaderSlot);
        writer.WriteByte(BlueLeaderSlot);
    }
}

/// <summary>
/// Golem War golem health reported by a client: [u16 RedGolem][u16 BlueGolem]. A golem at 0 ends the round.
/// Evidence: Observed in engine.dll SendFieldGameMasterGolem (0x36192440) and RakionWorldServ.exe 0x424980 / 0x405D70.
/// </summary>
public sealed record FieldGameGolemHpRequestPacket(ushort RedGolem, ushort BlueGolem) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldGameMasterGolem;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(RedGolem);
        writer.WriteUInt16(BlueGolem);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameGolemHpRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 4)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new FieldGameGolemHpRequestPacket(reader.ReadUInt16(), reader.ReadUInt16());
        return true;
    }
}

/// <summary>
/// Leader health reported in leader wars: [u8 LeaderSlot][u16 Health]; compared when the round times out.
/// Evidence: Observed in engine.dll SendFieldGameMasterBossHP (0x361924A0) and RakionWorldServ.exe 0x405EF0.
/// </summary>
public sealed record FieldGameLeaderHpRequestPacket(byte LeaderSlot, ushort Health) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldGameMasterBossHp;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(LeaderSlot);
        writer.WriteUInt16(Health);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameLeaderHpRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 3)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new FieldGameLeaderHpRequestPacket(reader.ReadByte(), reader.ReadUInt16());
        return true;
    }
}

/// <summary>
/// End-of-round report: [u32 Exp][u32 Gold][u8 Result][u32 x3 CreatureExp][u16 Value]. The client computes the
/// experience and gold; the legacy server only caps and stores them.
/// Evidence: offsets Observed in RakionWorldServ.exe 0x424C50 (exp, gold, byte), 0x424DC2 (three u32 at +9) and
/// 0x424E80 (u16 at +21). Meanings Inferred: the byte is the round result, the three values the experience of the
/// three equipped creatures (UNKNOWN until the creature flow is implemented).
/// </summary>
public sealed record FieldGamePointRequestPacket(uint Exp, uint Gold, byte Result, uint CreatureExp1, uint CreatureExp2, uint CreatureExp3, ushort Value) : IPacket
{
    public const int Size = 23;

    public ushort Opcode => (ushort)ClientOpcode.FieldGamePoint;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt32(Exp);
        writer.WriteUInt32(Gold);
        writer.WriteByte(Result);
        writer.WriteUInt32(CreatureExp1);
        writer.WriteUInt32(CreatureExp2);
        writer.WriteUInt32(CreatureExp3);
        writer.WriteUInt16(Value);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGamePointRequestPacket packet)
    {
        packet = null!;
        if (span.Length < Size)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new FieldGamePointRequestPacket(
            reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt16());
        return true;
    }
}

/// <summary>
/// Level up sent to the player: [u8 Level][u16 LevelPoints].
/// Evidence: Observed in RakionWorldServ.exe 0x424D8A (5-byte send after the level-up check 0x40D300) and engine.dll
/// 0x36194100 (reads u8 + u16).
/// </summary>
public sealed record CharacterLevelUpPacket(byte Level, ushort LevelPoints) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.CharacterLevelUp;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Level);
        writer.WriteUInt16(LevelPoints);
    }
}

/// <summary>
/// A room member's character and creature levels, broadcast to the room when either changed after a round:
/// [u8 Slot][u8 Level][u8 Creature1][u8 Creature2][u8 Creature3].
/// Evidence: Observed in RakionWorldServ.exe 0x424E1F (built after 0x40D300 / 0x40B940 report a change, sent with
/// 0x4061F0) and engine.dll 0x36194130 (reads five bytes). That the first byte is the slot is Derived (it is the
/// slot found by 0x40B7D0); that the last three are the equipment slots 10..12 creatures is Derived from 0x40B940.
/// </summary>
public sealed record FieldPlayerLevelPacket(byte Slot, byte Level, byte Creature1, byte Creature2, byte Creature3) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldPlayerLevel;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Slot);
        writer.WriteByte(Level);
        writer.WriteByte(Creature1);
        writer.WriteByte(Creature2);
        writer.WriteByte(Creature3);
    }
}

/// <summary>
/// In-game player data a client sends for every other member (0x4B) or, with a leading target slot, for one member
/// (0x4C): [u8 Target]? [u16 Length][data]. The server never reads the data.
/// Evidence: Observed in engine.dll SendFieldGameAddPlayer (0x36192350) / SendFieldGameAddPlayerReply (0x361923C0)
/// and RakionWorldServ.exe 0x4247B0 / 0x424880 (length limit 200, target slot below 20).
/// </summary>
public sealed record FieldGameAddPlayerRequestPacket(byte? TargetSlot, ReadOnlyMemory<byte> Data) : IPacket
{
    public const int MaxDataLength = 200;

    public ushort Opcode => (ushort)(TargetSlot is null ? ClientOpcode.FieldGameAddPlayer : ClientOpcode.FieldGameAddPlayerReply);

    public void Serialize(PacketWriter writer)
    {
        if (TargetSlot is { } target)
        {
            writer.WriteByte(target);
        }

        writer.WriteUInt16((ushort)Data.Length);
        writer.WriteBytes(Data.Span);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, bool hasTarget, out FieldGameAddPlayerRequestPacket packet)
    {
        packet = null!;
        int header = hasTarget ? 3 : 2;
        if (span.Length < header)
        {
            return false;
        }

        byte? target = hasTarget ? span[0] : null;
        int length = span[header - 2] | (span[header - 1] << 8);
        if (length > MaxDataLength || span.Length < header + length || target >= 20)
        {
            return false;
        }

        packet = new FieldGameAddPlayerRequestPacket(target, span.Slice(header, length).ToArray());
        return true;
    }
}

/// <summary>
/// In-game player data relayed to a member: [u8 SenderSlot][u16 Length][data].
/// Evidence: Observed in RakionWorldServ.exe 0x405C00 and 0x405CC0 (both send opcode 0x4B, 5 + length bytes).
/// </summary>
public sealed record FieldGameAddPlayerPacket(byte SenderSlot, ReadOnlyMemory<byte> Data) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldGameAddPlayer;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SenderSlot);
        writer.WriteUInt16((ushort)Data.Length);
        writer.WriteBytes(Data.Span);
    }
}
