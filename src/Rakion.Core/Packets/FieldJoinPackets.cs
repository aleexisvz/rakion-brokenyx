using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// A room member as the client draws it in the waiting room: name, second string, seven numeric fields and the
/// 19 equipment item ids and levels.
/// Evidence: layout Observed in RakionWorldServ.exe 0x40B7F0 (writer) and engine.dll 0x36196E60..0x36196F4E
/// (reader). Level is Observed (the legacy join compares it with the room level range, 0x405920); the rest of the
/// meanings are Inferred or Unknown as noted per field.
/// </summary>
public sealed record FieldPlayerRecord(
    string Name,
    string Guild,
    byte Class,
    byte Level,
    IReadOnlyList<ItemSlot>? Equipment,
    uint Ip1 = 0,
    ushort Port1 = 0,
    uint Ip2 = 0,
    ushort Port2 = 0)
{
    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(Name);   // user + 0x14A8 (Inferred: character name)
        writer.WriteNullTerminatedString(Guild);  // user + 0x14C2 (Inferred: guild name)
        writer.WriteByte(0);                      // user + 0x1478, UNKNOWN
        writer.WriteUInt32(Ip1);                  // user + 0x1450 (Inferred: UDP endpoint 1, peers reach the player here)
        writer.WriteUInt16(Port1);                // user + 0x1458
        writer.WriteUInt32(Ip2);                  // user + 0x1454 (Inferred: UDP endpoint 2)
        writer.WriteUInt16(Port2);                // user + 0x145A
        writer.WriteByte(Class);                  // user + 0x1530 (Inferred: class)
        writer.WriteByte(Level);                  // user + 0x1531
        writer.WriteByte(0);                      // user + 0x1473, UNKNOWN

        for (int i = 0; i < CharacterEquipment.SlotCount; i++)
        {
            writer.WriteUInt16(At(i).ItemId);
        }

        for (int i = 0; i < CharacterEquipment.SlotCount; i++)
        {
            writer.WriteByte(At(i).Level);
        }
    }

    public static bool TryParse(ref PacketReader reader, out FieldPlayerRecord record)
    {
        record = null!;
        string name = reader.ReadNullTerminatedString();
        string guild = reader.ReadNullTerminatedString();
        const int fixedSize = 1 + 4 + 2 + 4 + 2 + 3 + (CharacterEquipment.SlotCount * 3);
        if (reader.Remaining < fixedSize)
        {
            return false;
        }

        reader.ReadByte();
        uint ip1 = reader.ReadUInt32();
        ushort port1 = reader.ReadUInt16();
        uint ip2 = reader.ReadUInt32();
        ushort port2 = reader.ReadUInt16();
        byte cls = reader.ReadByte();
        byte level = reader.ReadByte();
        reader.ReadByte();

        var slots = new ItemSlot[CharacterEquipment.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = slots[i] with { ItemId = reader.ReadUInt16() };
        }

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = slots[i] with { Level = reader.ReadByte() };
        }

        record = new FieldPlayerRecord(name, guild, cls, level, slots, ip1, port1, ip2, port2);
        return true;
    }

    private ItemSlot At(int index) =>
        Equipment is not null && index < Equipment.Count ? Equipment[index] : default;
}

/// <summary>
/// One waiting-room slot of <see cref="FieldInfoPacket"/>. Wire states: 0 empty, 1 waiting, 2 ready, 3 in game,
/// 5 closed; empty and closed slots carry no player.
/// Evidence: Observed in RakionWorldServ.exe 0x407491 (skips states 0 and 5) and engine.dll 0x36196E0B (maps states
/// above 3 down by one and skips 0 and 4). Meaning of 1/2/3 Observed in the ready (0x407520) and start (0x4079D0) code.
/// </summary>
public sealed record FieldSlotInfo(byte State, ushort PlayerId, byte Flag, FieldPlayerRecord? Player)
{
    public const byte Empty = 0;
    public const byte Waiting = 1;
    public const byte Ready = 2;
    public const byte InGame = 3;
    public const byte Loaded = 4;
    public const byte Closed = 5;

    public bool HasPlayer => State is not (Empty or Closed);
}

/// <summary>
/// Room details sent to a player who just entered: fixed rule header, room name, password and map name, then the
/// 20 slots.
/// Evidence: Observed in RakionWorldServ.exe 0x4073B7..0x4074EF (writer, opcode 0x37) and engine.dll 0x36196CE0
/// (reader). Each header byte is named after the legacy room field it copies; meanings are Inferred unless noted.
/// </summary>
public sealed record FieldInfoPacket(
    ushort RoomNumber,
    byte RoomState,
    byte MasterSlot,
    byte Kind,
    byte Mode,
    byte MinLevel,
    byte MaxLevel,
    byte RuleExtra,
    byte Unknown,
    byte Option,
    ushort TimeLimit,
    byte Goal,
    string Name,
    string Password,
    string MapName,
    IReadOnlyList<FieldSlotInfo> Slots) : IPacket
{
    public const int SlotCount = 20;

    public ushort Opcode => (ushort)ServerOpcode.FieldInfo;

    public void Serialize(PacketWriter writer)
    {
        if (Slots.Count != SlotCount)
        {
            throw new InvalidOperationException($"A room has {SlotCount} slots.");
        }

        writer.WriteUInt16(RoomNumber);
        writer.WriteByte(RoomState);   // room + 0x08 (1 waiting, 2 playing)
        writer.WriteByte(MasterSlot);  // room + 0x121
        writer.WriteByte(Kind);        // room + 0x118, first rule byte of the create request
        writer.WriteByte(Mode);        // room + 0x119 (Observed: modes 1..4 drive the start checks)
        writer.WriteByte(MinLevel);    // room + 0x111
        writer.WriteByte(MaxLevel);    // room + 0x112
        writer.WriteByte(RuleExtra);   // room + 0x113
        writer.WriteByte(Unknown);     // room + 0x2BC, UNKNOWN
        writer.WriteByte(Option);      // room + 0x11A
        writer.WriteUInt16(TimeLimit); // room + 0x11C
        writer.WriteByte(Goal);        // room + 0x11E
        writer.WriteNullTerminatedString(Name);
        writer.WriteNullTerminatedString(Password);
        writer.WriteNullTerminatedString(MapName);

        foreach (var slot in Slots)
        {
            writer.WriteByte(slot.State);
            if (!slot.HasPlayer)
            {
                continue;
            }

            writer.WriteUInt16(slot.PlayerId);
            writer.WriteByte(slot.Flag);
            (slot.Player ?? throw new InvalidOperationException("An occupied slot needs a player record.")).Serialize(writer);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldInfoPacket packet)
    {
        packet = null!;
        if (span.Length < 14)
        {
            return false;
        }

        var reader = new PacketReader(span);
        ushort number = reader.ReadUInt16();
        byte state = reader.ReadByte();
        byte master = reader.ReadByte();
        byte kind = reader.ReadByte();
        byte mode = reader.ReadByte();
        byte minLevel = reader.ReadByte();
        byte maxLevel = reader.ReadByte();
        byte extra = reader.ReadByte();
        byte unknown = reader.ReadByte();
        byte option = reader.ReadByte();
        ushort timeLimit = reader.ReadUInt16();
        byte goal = reader.ReadByte();
        string name = reader.ReadNullTerminatedString();
        string password = reader.ReadNullTerminatedString();
        string map = reader.ReadNullTerminatedString();

        var slots = new List<FieldSlotInfo>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            if (reader.Remaining < 1)
            {
                return false;
            }

            byte slotState = reader.ReadByte();
            if (slotState is FieldSlotInfo.Empty or FieldSlotInfo.Closed)
            {
                slots.Add(new FieldSlotInfo(slotState, 0, 0, null));
                continue;
            }

            if (reader.Remaining < 3)
            {
                return false;
            }

            ushort playerId = reader.ReadUInt16();
            byte flag = reader.ReadByte();
            if (!FieldPlayerRecord.TryParse(ref reader, out var player))
            {
                return false;
            }

            slots.Add(new FieldSlotInfo(slotState, playerId, flag, player));
        }

        packet = new FieldInfoPacket(number, state, master, kind, mode, minLevel, maxLevel, extra, unknown, option, timeLimit, goal, name, password, map, slots);
        return true;
    }
}

/// <summary>
/// Network check between room members: [u8 Slot]. The client sends the slot of the member it wants to reach; the
/// server relays the message to that member with the sender's slot instead.
/// Evidence: Observed in engine.dll ?SendFieldSlotUDP@IScavengerWorldNet@@UAEXE@Z (0x36192560, opcode 0x0062),
/// engine.dll 0x36194510 (reader, one byte) and RakionWorldServ.exe 0x406930 (relay, 3-byte send).
/// </summary>
public sealed record FieldSlotUdpPacket(byte SlotIndex) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldSlotUdp;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SlotIndex);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldSlotUdpPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new FieldSlotUdpPacket(span[0]);
        return true;
    }
}

/// <summary>
/// Quick enter found no room. No payload; a successful quick enter is answered like a normal enter.
/// Evidence: Observed in RakionWorldServ.exe 0x423436 (2-byte send) and engine.dll 0x36193A60 (no read).
/// </summary>
public sealed record FieldQuickEnterFailedPacket : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldQuickEnter;

    public void Serialize(PacketWriter writer)
    {
    }
}

/// <summary>
/// Room enter result. On success it is broadcast to the whole room (the newcomer included) and carries the new
/// member: [u8 0][u8 Slot][u8 SlotState][u16 PlayerId][u8 Flag] + player record. On failure only [u8 Result] goes
/// to the requester.
/// Evidence: Observed in RakionWorldServ.exe 0x406F40 (results 1, 2, 3, 6, 7, 8 and the 0x38 broadcast at 0x407358)
/// and engine.dll 0x361970E0 (reader: parses the rest only when the result is 0).
/// </summary>
public sealed record FieldPlayerEnterPacket(
    byte Result,
    byte SlotIndex = 0,
    byte SlotState = 0,
    ushort PlayerId = 0,
    byte Flag = 0,
    FieldPlayerRecord? Player = null) : IPacket
{
    public const byte Entered = 0;

    /// <summary>Room not open (legacy room state 0).</summary>
    public const byte RoomUnavailable = 1;

    /// <summary>No free slot.</summary>
    public const byte RoomFull = 2;

    /// <summary>Wrong password.</summary>
    public const byte WrongPassword = 3;

    /// <summary>Match already running.</summary>
    public const byte InProgress = 6;

    /// <summary>Character level outside the room range.</summary>
    public const byte LevelOutOfRange = 7;

    public ushort Opcode => (ushort)ServerOpcode.FieldEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        if (Result != Entered)
        {
            return;
        }

        writer.WriteByte(SlotIndex);
        writer.WriteByte(SlotState);
        writer.WriteUInt16(PlayerId);
        writer.WriteByte(Flag);
        (Player ?? throw new InvalidOperationException("A successful enter needs a player record.")).Serialize(writer);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldPlayerEnterPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        if (span[0] != Entered)
        {
            packet = new FieldPlayerEnterPacket(span[0]);
            return true;
        }

        if (span.Length < 6)
        {
            return false;
        }

        var reader = new PacketReader(span[1..]);
        byte slot = reader.ReadByte();
        byte state = reader.ReadByte();
        ushort playerId = reader.ReadUInt16();
        byte flag = reader.ReadByte();
        if (!FieldPlayerRecord.TryParse(ref reader, out var player))
        {
            return false;
        }

        packet = new FieldPlayerEnterPacket(Entered, slot, state, playerId, flag, player);
        return true;
    }
}
