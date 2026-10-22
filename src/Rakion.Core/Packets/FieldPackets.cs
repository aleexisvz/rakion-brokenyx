using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Represents a single room (field) descriptor in the channel game list.
/// Evidence: Observed in RakionWorldServ.exe 0x405790 and engine.dll 0x36193900.
/// </summary>
public sealed record FieldListEntry(
    ushort RoomNumber,
    bool HasPassword,
    bool IsInProgress,
    byte Mode,
    byte Map,
    byte MaxPlayers,
    byte RoundTime,
    byte Rounds,
    byte SubMode,
    byte LevelLimit,
    byte CurrentPlayers,
    byte MaxAllowed,
    uint HostUserId,
    ushort HostLevel,
    string RoomName,
    ushort Extra = 0)
{
    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(RoomNumber);
        writer.WriteByte((byte)(HasPassword ? 1 : 0));
        writer.WriteByte((byte)(IsInProgress ? 1 : 0));
        writer.WriteByte(Mode);
        writer.WriteByte(Map);
        writer.WriteByte(MaxPlayers);
        writer.WriteByte(RoundTime);
        writer.WriteByte(Rounds);
        writer.WriteByte(SubMode);
        writer.WriteByte(LevelLimit);
        writer.WriteByte(CurrentPlayers);
        writer.WriteByte(MaxAllowed);
        writer.WriteUInt32(HostUserId);
        writer.WriteUInt16(HostLevel);
        writer.WriteNullTerminatedString(RoomName);
        writer.WriteUInt16(Extra);
    }

    public static bool TryParse(ref PacketReader reader, out FieldListEntry entry)
    {
        entry = null!;
        // Fixed portion before room name:
        // RoomNumber (2) + HasPassword (1) + IsInProgress (1) + Mode (1) + Map (1) +
        // MaxPlayers (1) + RoundTime (1) + Rounds (1) + SubMode (1) + LevelLimit (1) +
        // CurrentPlayers (1) + MaxAllowed (1) + HostUserId (4) + HostLevel (2) = 18 bytes
        if (reader.Remaining < 18)
        {
            return false;
        }

        ushort roomNumber = reader.ReadUInt16();
        bool hasPassword = reader.ReadByte() != 0;
        bool isInProgress = reader.ReadByte() != 0;
        byte mode = reader.ReadByte();
        byte map = reader.ReadByte();
        byte maxPlayers = reader.ReadByte();
        byte roundTime = reader.ReadByte();
        byte rounds = reader.ReadByte();
        byte subMode = reader.ReadByte();
        byte levelLimit = reader.ReadByte();
        byte currentPlayers = reader.ReadByte();
        byte maxAllowed = reader.ReadByte();
        uint hostUserId = reader.ReadUInt32();
        ushort hostLevel = reader.ReadUInt16();
        string roomName = reader.ReadNullTerminatedString();

        if (reader.Remaining < 2)
        {
            return false;
        }

        ushort extra = reader.ReadUInt16();

        entry = new FieldListEntry(
            roomNumber,
            hasPassword,
            isInProgress,
            mode,
            map,
            maxPlayers,
            roundTime,
            rounds,
            subMode,
            levelLimit,
            currentPlayers,
            maxAllowed,
            hostUserId,
            hostLevel,
            roomName,
            extra);

        return true;
    }
}

/// <summary>
/// Client request to fetch the list of active game rooms (fields) in the channel.
/// Evidence: Observed in engine.dll ?SendFieldList@IScavengerWorldNet@@UAEXEGEEEEEEE@Z (Opcode 0x0036 / 54)
/// and RakionWorldServ.exe opcode jump table 0x42b80c (0x0036 -> 0x42b0ba -> 0x422c90).
/// </summary>
public sealed record FieldListRequestPacket(
    byte PageSize = 10,
    ushort PageIndex = 0,
    byte Filter1 = 0,
    byte Filter2 = 0,
    byte Filter3 = 0,
    byte Filter4 = 0,
    byte Filter5 = 0,
    byte Filter6 = 0,
    byte Filter7 = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.FieldList;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(PageSize);
        writer.WriteUInt16(PageIndex);
        writer.WriteByte(Filter1);
        writer.WriteByte(Filter2);
        writer.WriteByte(Filter3);
        writer.WriteByte(Filter4);
        writer.WriteByte(Filter5);
        writer.WriteByte(Filter6);
        writer.WriteByte(Filter7);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldListRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 10)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte pageSize = reader.ReadByte();
        ushort pageIndex = reader.ReadUInt16();
        byte f1 = reader.ReadByte();
        byte f2 = reader.ReadByte();
        byte f3 = reader.ReadByte();
        byte f4 = reader.ReadByte();
        byte f5 = reader.ReadByte();
        byte f6 = reader.ReadByte();
        byte f7 = reader.ReadByte();

        packet = new FieldListRequestPacket(pageSize, pageIndex, f1, f2, f3, f4, f5, f6, f7);
        return true;
    }
}

/// <summary>
/// Server response providing the list of active game rooms (fields) in the current channel.
/// Evidence: Observed in engine.dll jump table 0x3619786C opcode 54 (0x361975d9 -> 0x36193900)
/// and RakionWorldServ.exe 0x422f14.
/// </summary>
public sealed record FieldListResponsePacket(
    IReadOnlyList<FieldListEntry> Fields) : IPacket
{
    public static readonly FieldListResponsePacket Empty = new(Array.Empty<FieldListEntry>());

    public ushort Opcode => (ushort)RakionOpcode.FieldList;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Fields.Count);
        for (int i = 0; i < Fields.Count; i++)
        {
            Fields[i].Serialize(writer);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldListResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        byte count = reader.ReadByte();
        if (count == 0)
        {
            packet = Empty;
            return true;
        }

        var list = new List<FieldListEntry>(count);
        for (int i = 0; i < count; i++)
        {
            if (!FieldListEntry.TryParse(ref reader, out var entry))
            {
                return false;
            }
            list.Add(entry);
        }

        packet = new FieldListResponsePacket(list);
        return true;
    }
}
