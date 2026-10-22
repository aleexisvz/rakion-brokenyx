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
    public ushort Opcode => (ushort)ClientOpcode.FieldList;

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

    public ushort Opcode => (ushort)ServerOpcode.FieldList;

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


/// <summary>
/// Client request to create a new game room (field).
/// Evidence: Observed in engine.dll ?SendFieldCreate@IScavengerWorldNet@@UAEXPAD00EEEGEEEE@Z (opcode 0x003B / 59)
/// and RakionWorldServ.exe opcode jump table (0x003B -> 0x42b14a -> 0x423580).
/// </summary>
public sealed record FieldCreateRequestPacket(
    string RoomName,
    string Password,
    string MapName,
    byte Mode,
    byte MaxPlayers,
    byte RoundTime,
    ushort Rounds,
    byte SubMode,
    byte LevelLimit,
    byte Extra1,
    byte Extra2) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldCreate;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(RoomName);
        writer.WriteNullTerminatedString(Password);
        writer.WriteNullTerminatedString(MapName);
        writer.WriteByte(Mode);
        writer.WriteByte(MaxPlayers);
        writer.WriteByte(RoundTime);
        writer.WriteUInt16(Rounds);
        writer.WriteByte(SubMode);
        writer.WriteByte(LevelLimit);
        writer.WriteByte(Extra1);
        writer.WriteByte(Extra2);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldCreateRequestPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string roomName = reader.ReadNullTerminatedString();
        string password = reader.ReadNullTerminatedString();
        string mapName = reader.ReadNullTerminatedString();

        if (reader.Remaining < 9)
        {
            return false;
        }

        byte mode = reader.ReadByte();
        byte maxPlayers = reader.ReadByte();
        byte roundTime = reader.ReadByte();
        ushort rounds = reader.ReadUInt16();
        byte subMode = reader.ReadByte();
        byte levelLimit = reader.ReadByte();
        byte extra1 = reader.ReadByte();
        byte extra2 = reader.ReadByte();

        packet = new FieldCreateRequestPacket(roomName, password, mapName, mode, maxPlayers, roundTime, rounds, subMode, levelLimit, extra1, extra2);
        return true;
    }
}

/// <summary>
/// Server response acknowledging game room creation.
/// Evidence: Observed in engine.dll 0x36193a90 and RakionWorldServ.exe 0x423a82.
/// Structure: [Result: u8][RoomNumber: u16] (Total 3 bytes).
/// </summary>
public sealed record FieldCreateResponsePacket(
    byte ResultCode,
    ushort RoomNumber = 0) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldCreate;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
        writer.WriteUInt16(RoomNumber);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldCreateResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 3)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte resultCode = reader.ReadByte();
        ushort roomNumber = reader.ReadUInt16();

        packet = new FieldCreateResponsePacket(resultCode, roomNumber);
        return true;
    }
}

/// <summary>
/// Client request to quick join any open room.
/// Evidence: Observed in engine.dll ?SendFieldQuickEnter@IScavengerWorldNet@@UAEXXZ (0x0039).
/// </summary>
public sealed record FieldQuickEnterRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldQuickEnter;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldQuickEnterRequestPacket packet)
    {
        packet = new FieldQuickEnterRequestPacket();
        return true;
    }
}

/// <summary>
/// Client request to enter a field / room by room number.
/// Evidence: Observed in engine.dll ?SendFieldEnter@IScavengerWorldNet@@UAEXGPAD@Z (0x0038).
/// </summary>
public sealed record FieldEnterRequestPacket(
    ushort RoomNumber,
    string Password) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(RoomNumber);
        writer.WriteNullTerminatedString(Password);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldEnterRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        var reader = new PacketReader(span);
        ushort roomNumber = reader.ReadUInt16();
        string password = reader.ReadNullTerminatedString();
        packet = new FieldEnterRequestPacket(roomNumber, password);
        return true;
    }
}

/// <summary>
/// Client request to exit a field / room.
/// Evidence: Observed in engine.dll ?SendFieldExit@IScavengerWorldNet@@UAEXXZ (0x003A).
/// </summary>
public sealed record FieldExitRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldExit;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldExitRequestPacket packet)
    {
        packet = new FieldExitRequestPacket();
        return true;
    }
}

/// <summary>
/// Server confirmation / notification when exiting a field / room: [u8 SlotIndex].
/// Evidence: Observed in engine.dll 0x36193a70 and rakion.bin 0x474410 (opcode 0x003A).
/// Client checks: if (payload[0] == GetMySlotNumber()) ExitRoomUI() else ClearPlayerSlot(payload[0]).
/// </summary>
public sealed record FieldExitResponsePacket(
    byte SlotIndex) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldExit;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SlotIndex);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldExitResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte slotIndex = reader.ReadByte();
        packet = new FieldExitResponsePacket(slotIndex);
        return true;
    }
}

/// <summary>
/// Client toggles ready state in room.
/// Evidence: Observed in engine.dll ?SendFieldReady@IScavengerWorldNet@@UAEXE@Z (opcode 0x003D).
/// </summary>
public sealed record FieldReadyRequestPacket(
    byte ReadyState) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldReady;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ReadyState);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldReadyRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte ready = reader.ReadByte();
        packet = new FieldReadyRequestPacket(ready);
        return true;
    }
}

/// <summary>
/// Server broadcast notifying room members of player ready state change.
/// Evidence: Observed in engine.dll 0x36193ae0 (opcode 0x003D, 2 bytes: u8 SlotIndex, u8 ReadyState).
/// </summary>
public sealed record FieldReadyBroadcastPacket(
    byte SlotIndex,
    byte ReadyState) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldReady;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SlotIndex);
        writer.WriteByte(ReadyState);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldReadyBroadcastPacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte slot = reader.ReadByte();
        byte ready = reader.ReadByte();
        packet = new FieldReadyBroadcastPacket(slot, ready);
        return true;
    }
}

/// <summary>
/// Chat message sent inside a room by a client.
/// Evidence: Observed in engine.dll ?SendFieldChat@IScavengerWorldNet@@UAEXPAD@Z (opcode 0x0047).
/// </summary>
public sealed record FieldChatRequestPacket(
    string Message) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldChat;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(Message);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChatRequestPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string message = reader.ReadNullTerminatedString();
        packet = new FieldChatRequestPacket(message);
        return true;
    }
}

/// <summary>
/// Room chat broadcast sent to all members in the room.
/// Evidence: Observed in engine.dll 0x361976ab -> 0x36193b70 (opcode 0x0047).
/// </summary>
public sealed record FieldChatBroadcastPacket(
    string Message) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldChat;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(Message);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChatBroadcastPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string message = reader.ReadNullTerminatedString();
        packet = new FieldChatBroadcastPacket(message);
        return true;
    }
}


/// <summary>
/// Player requests to switch team (Red &lt;-&gt; Blue) in waiting room.
/// Evidence: Observed in engine.dll ?SendFieldChangeTeam@IScavengerWorldNet@@UAEXXZ (opcode 0x003E).
/// </summary>
public sealed record FieldChangeTeamRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldChangeTeam;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChangeTeamRequestPacket packet)
    {
        packet = new FieldChangeTeamRequestPacket();
        return true;
    }
}

/// <summary>
/// Broadcast or reply confirming player team change: [u8 Result, u8 OldSlot, u8 NewSlot].
/// Evidence: Observed in engine.dll 0x36197649 -> 0x36193b0b and RakionWorldServ 0x40779D (opcode 0x003E).
/// </summary>
public sealed record FieldChangeTeamBroadcastPacket(
    byte Result,
    byte OldSlot,
    byte NewSlot) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldChangeTeam;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteByte(OldSlot);
        writer.WriteByte(NewSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChangeTeamBroadcastPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte result = reader.ReadByte();
        byte oldSlot = reader.Remaining >= 1 ? reader.ReadByte() : (byte)0;
        byte newSlot = reader.Remaining >= 1 ? reader.ReadByte() : (byte)0;
        packet = new FieldChangeTeamBroadcastPacket(result, oldSlot, newSlot);
        return true;
    }
}

/// <summary>
/// Request from host to toggle slot status (Open / Closed): [u8 SlotIndex, u8 Status].
/// Evidence: Observed in engine.dll ?SendFieldChangeSlotStatus@IScavengerWorldNet@@UAEXEE@Z (opcode 0x0042).
/// </summary>
public sealed record FieldChangeSlotStatusRequestPacket(
    byte SlotIndex,
    byte Status) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldChangeSlotStatus;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SlotIndex);
        writer.WriteByte(Status);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChangeSlotStatusRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte slotIndex = reader.ReadByte();
        byte status = reader.ReadByte();
        packet = new FieldChangeSlotStatusRequestPacket(slotIndex, status);
        return true;
    }
}

/// <summary>
/// Broadcast confirming slot status change: [u8 SlotIndex, u8 Status].
/// Evidence: Observed in engine.dll 0x36197665 -> 0x36193c50 and RakionWorldServ 0x407980 (opcode 0x0042).
/// </summary>
public sealed record FieldChangeSlotStatusBroadcastPacket(
    byte SlotIndex,
    byte Status) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldChangeSlotStatus;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SlotIndex);
        writer.WriteByte(Status);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChangeSlotStatusBroadcastPacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte slotIndex = reader.ReadByte();
        byte status = reader.ReadByte();
        packet = new FieldChangeSlotStatusBroadcastPacket(slotIndex, status);
        return true;
    }
}

/// <summary>
/// Request from host to update room settings / rules: [string RoomName, string Password, string MapName, u8 Mode, u8 MaxPlayers, u16 RoundTime, u8 Rounds, u8 SubMode].
/// Evidence: Observed in engine.dll ?SendFieldChangeRule@IScavengerWorldNet@@UAEXPAD00EEGEE@Z (opcode 0x0041).
/// </summary>
public sealed record FieldChangeRuleRequestPacket(
    string RoomName,
    string Password,
    string MapName,
    byte Mode,
    byte MaxPlayers,
    ushort RoundTime,
    byte Rounds,
    byte SubMode) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldChangeRule;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(RoomName);
        writer.WriteNullTerminatedString(Password);
        writer.WriteNullTerminatedString(MapName);
        writer.WriteByte(Mode);
        writer.WriteByte(MaxPlayers);
        writer.WriteUInt16(RoundTime);
        writer.WriteByte(Rounds);
        writer.WriteByte(SubMode);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChangeRuleRequestPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string roomName = reader.ReadNullTerminatedString();
        string password = reader.ReadNullTerminatedString();
        string mapName = reader.ReadNullTerminatedString();
        byte mode = reader.ReadByte();
        byte maxPlayers = reader.ReadByte();
        ushort roundTime = reader.ReadUInt16();
        byte rounds = reader.ReadByte();
        byte subMode = reader.ReadByte();

        packet = new FieldChangeRuleRequestPacket(
            roomName,
            password,
            mapName,
            mode,
            maxPlayers,
            roundTime,
            rounds,
            subMode);
        return true;
    }
}

/// <summary>
/// Broadcast notifying members of updated room settings: [string RoomName, string Password, string MapName, u8 Mode, u8 MaxPlayers, u16 RoundTime, u8 Rounds, u8 SubMode].
/// Evidence: Observed in engine.dll 0x36193b50 and rakion.bin 0x476100 (opcode 0x0041).
/// </summary>
public sealed record FieldChangeRuleBroadcastPacket(
    string RoomName,
    string Password,
    string MapName,
    byte Mode,
    byte MaxPlayers,
    ushort RoundTime,
    byte Rounds,
    byte SubMode) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldChangeRule;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(RoomName);
        writer.WriteNullTerminatedString(Password);
        writer.WriteNullTerminatedString(MapName);
        writer.WriteByte(Mode);
        writer.WriteByte(MaxPlayers);
        writer.WriteUInt16(RoundTime);
        writer.WriteByte(Rounds);
        writer.WriteByte(SubMode);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldChangeRuleBroadcastPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string roomName = reader.ReadNullTerminatedString();
        string password = reader.ReadNullTerminatedString();
        string mapName = reader.ReadNullTerminatedString();
        byte mode = reader.ReadByte();
        byte maxPlayers = reader.ReadByte();
        ushort roundTime = reader.ReadUInt16();
        byte rounds = reader.ReadByte();
        byte subMode = reader.ReadByte();

        packet = new FieldChangeRuleBroadcastPacket(
            roomName,
            password,
            mapName,
            mode,
            maxPlayers,
            roundTime,
            rounds,
            subMode);
        return true;
    }
}
