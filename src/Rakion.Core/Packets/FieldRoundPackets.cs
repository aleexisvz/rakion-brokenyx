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
