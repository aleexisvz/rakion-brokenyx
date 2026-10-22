using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Client request to open the inventory screen. The client sends the opcode only.
/// Evidence: Observed in engine.dll ?SendInventoryEnter@IScavengerWorldNet@@UAEXXZ
/// (opcode 0x002C, 2-byte message). The server answers with <see cref="InventoryEnterResponsePacket"/>.
/// </summary>
public sealed record InventoryEnterRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventoryEnter;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryEnterRequestPacket packet)
    {
        packet = new InventoryEnterRequestPacket();
        return true;
    }
}

/// <summary>
/// Client notification that the inventory screen was closed. The client sends the opcode only.
/// Evidence: Observed in engine.dll ?SendInventoryLeave@IScavengerWorldNet@@UAEXXZ
/// (opcode 0x002D, 2-byte message). The server answers with <see cref="InventoryLeaveResponsePacket"/>.
/// </summary>
public sealed record InventoryLeaveRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventoryLeave;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryLeaveRequestPacket packet)
    {
        packet = new InventoryLeaveRequestPacket();
        return true;
    }
}

/// <summary>
/// Server result for <see cref="InventoryEnterRequestPacket"/>. It carries no item list.
/// Wire layout (Observed: RakionWorldServ.exe 0x427A9D sends 7 bytes; engine.dll 0x36193650 reads u8 + u32):
///   [0]    byte Result (0 = success, 1 = database error)
///   [1..4] uint ServerTimeMinutes (see <see cref="Utilities.LegacyGameClock"/>; 0 on the early-reject path)
/// Inferred: the client uses ServerTimeMinutes to show the remaining time of rented items.
/// </summary>
public sealed record InventoryEnterResponsePacket(byte Result, uint ServerTimeMinutes) : IPacket
{
    public const byte ResultSuccess = 0;
    public const byte ResultDatabaseError = 1;

    public ushort Opcode => (ushort)ServerOpcode.InventoryEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(ServerTimeMinutes);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryEnterResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 5)
        {
            return false;
        }

        packet = new InventoryEnterResponsePacket(reader.ReadByte(), reader.ReadUInt32());
        return true;
    }
}

/// <summary>
/// One item slot as the client receives it, in the account box or in a character's equipment.
/// Evidence: Observed client record layout (engine.dll 0x36196B90..0x36196C3F): ItemId u16 at +0x00,
/// Level u8 at +0x08, then two u32 at +0x14 and +0x20. That those are Exp and LimitTime is Derived
/// from the order the legacy server copies them (RakionWorldServ.exe 0x412065..0x4120E6).
/// </summary>
public readonly record struct ItemSlot(ushort ItemId, byte Level, uint Exp, uint LimitTime)
{
    public bool IsEmpty => ItemId == 0;
}

/// <summary>
/// Account item box sent at the end of WorldLoginPart2 (0x000D).
/// Wire layout, Observed in the client parser (engine.dll 0x36196B41..0x36196C71):
///   u8 N, N x u16          expired item ids (Derived: legacy server collects rows whose limittime passed)
///   u8                     BagCount: unlocked bags, 40 slots each (Derived, see below)
///   120 x u16              ItemId per slot
///   120 x u8               Level per slot
///   120 x u32              Exp per slot
///   120 x u32              LimitTime per slot (LegacyGameClock minutes, 0 = permanent; Inferred)
///   u32                    UNKNOWN: purpose not yet verified (sent as 0)
/// BagCount: rakion.bin 0x47E75A stores this byte at listener+0x80, the same field the BuyBag reply
/// (rakion.bin 0x475283) updates as the account's bag count. Observed offsets; meaning Derived.
/// </summary>
public sealed record AccountBoxBlock
{
    public const int SlotCount = 120;

    /// <summary>Size of the block when no expired items are listed.</summary>
    public const int EmptyBlockSize = 1 + 1 + SlotCount * (2 + 1 + 4 + 4) + 4;

    private readonly ItemSlot[] _slots;
    private readonly ushort[] _expiredItemIds;

    public AccountBoxBlock(IReadOnlyList<ItemSlot>? slots = null, IReadOnlyList<ushort>? expiredItemIds = null, byte bagCount = 0)
    {
        if (slots is not null && slots.Count > SlotCount)
        {
            throw new ArgumentException($"The account box has {SlotCount} slots.", nameof(slots));
        }

        if (expiredItemIds is not null && expiredItemIds.Count > byte.MaxValue)
        {
            throw new ArgumentException("At most 255 expired items fit in the block.", nameof(expiredItemIds));
        }

        _slots = new ItemSlot[SlotCount];
        if (slots is not null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                _slots[i] = slots[i];
            }
        }

        _expiredItemIds = expiredItemIds?.ToArray() ?? [];
        BagCount = bagCount;
    }

    public static AccountBoxBlock Empty { get; } = new();

    public IReadOnlyList<ItemSlot> Slots => _slots;
    public IReadOnlyList<ushort> ExpiredItemIds => _expiredItemIds;
    public byte BagCount { get; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)_expiredItemIds.Length);
        foreach (ushort itemId in _expiredItemIds)
        {
            writer.WriteUInt16(itemId);
        }

        writer.WriteByte(BagCount);

        foreach (var slot in _slots)
        {
            writer.WriteUInt16(slot.ItemId);
        }

        foreach (var slot in _slots)
        {
            writer.WriteByte(slot.Level);
        }

        foreach (var slot in _slots)
        {
            writer.WriteUInt32(slot.Exp);
        }

        foreach (var slot in _slots)
        {
            writer.WriteUInt32(slot.LimitTime);
        }

        writer.WriteUInt32(0); // UNKNOWN: purpose not yet verified.
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out AccountBoxBlock block)
    {
        block = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        int expiredCount = reader.ReadByte();
        if (reader.Remaining < expiredCount * 2 + EmptyBlockSize - 1)
        {
            return false;
        }

        var expired = new ushort[expiredCount];
        for (int i = 0; i < expiredCount; i++)
        {
            expired[i] = reader.ReadUInt16();
        }

        byte bagCount = reader.ReadByte();

        var itemIds = new ushort[SlotCount];
        var levels = new byte[SlotCount];
        var exps = new uint[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            itemIds[i] = reader.ReadUInt16();
        }

        for (int i = 0; i < SlotCount; i++)
        {
            levels[i] = reader.ReadByte();
        }

        for (int i = 0; i < SlotCount; i++)
        {
            exps[i] = reader.ReadUInt32();
        }

        var slots = new ItemSlot[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            slots[i] = new ItemSlot(itemIds[i], levels[i], exps[i], reader.ReadUInt32());
        }

        reader.Skip(4);
        block = new AccountBoxBlock(slots, expired, bagCount);
        return true;
    }
}

/// <summary>
/// Server result for <see cref="InventoryLeaveRequestPacket"/>: [u8 Result].
/// Evidence: Observed in engine.dll 0x36193680 (opcode 0x2D reads one byte). Without it the client stays
/// on "leaving inventory". Inferred: 0 = success, matching the other inventory results.
/// </summary>
public sealed record InventoryLeaveResponsePacket(byte Result) : IPacket
{
    public const byte ResultSuccess = 0;

    public ushort Opcode => (ushort)ServerOpcode.InventoryLeave;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryLeaveResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new InventoryLeaveResponsePacket(span[0]);
        return true;
    }
}

/// <summary>
/// Where an inventory slot lives. Evidence: RakionWorldServ.exe 0x4218EF validates location 0 against
/// 120 slots and any other value against 19 slots (Observed); names Derived.
/// </summary>
public enum InventoryLocation : byte
{
    AccountBox = 0,
    Equipment = 1,
}

/// <summary>
/// Client request to move an item between two slots: (u8, u8, u8, u8).
/// Evidence: Observed in engine.dll ?SendInventoryMove@IScavengerWorldNet@@UAEXEEEE@Z (0x0031, 6-byte message)
/// and in the legacy parser RakionWorldServ.exe 0x4218EF: source location, source slot, destination
/// location, destination slot.
/// </summary>
public sealed record InventoryMoveRequestPacket(
    InventoryLocation SourceLocation,
    byte SourceSlot,
    InventoryLocation DestinationLocation,
    byte DestinationSlot) : IPacket
{
    public const int AccountBoxSlots = 120;
    public const int EquipmentSlots = 19;

    public ushort Opcode => (ushort)ClientOpcode.InventoryMove;

    /// <summary>Same range rule as the legacy server: box slots 0..119, equipment slots 0..18.</summary>
    public bool IsInRange =>
        SourceSlot < SlotCount(SourceLocation) && DestinationSlot < SlotCount(DestinationLocation);

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)SourceLocation);
        writer.WriteByte(SourceSlot);
        writer.WriteByte((byte)DestinationLocation);
        writer.WriteByte(DestinationSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryMoveRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 4)
        {
            return false;
        }

        packet = new InventoryMoveRequestPacket((InventoryLocation)span[0], span[1], (InventoryLocation)span[2], span[3]);
        return true;
    }

    private static int SlotCount(InventoryLocation location) =>
        location == InventoryLocation.AccountBox ? AccountBoxSlots : EquipmentSlots;
}

/// <summary>
/// Contents of one slot after a move: location, slot, item id, level, exp (9 bytes).
/// Evidence: Observed field sizes in engine.dll 0x36193810; that the u32 is exp is Derived from the legacy
/// server's slot arrays (item id, level, exp are contiguous: RakionWorldServ.exe 0x40CF8C..0x40CFDD).
/// </summary>
public readonly record struct InventorySlotState(InventoryLocation Location, byte Slot, ushort ItemId, byte Level, uint Exp)
{
    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Location);
        writer.WriteByte(Slot);
        writer.WriteUInt16(ItemId);
        writer.WriteByte(Level);
        writer.WriteUInt32(Exp);
    }

    public static InventorySlotState Read(ref PacketReader reader) =>
        new((InventoryLocation)reader.ReadByte(), reader.ReadByte(), reader.ReadUInt16(), reader.ReadByte(), reader.ReadUInt32());
}

/// <summary>
/// Server result for <see cref="InventoryMoveRequestPacket"/>: [u8 Result][source state][destination state].
/// Evidence: Observed in engine.dll 0x36193810 (19-byte reader) and RakionWorldServ.exe 0x4219F7.
/// Result codes observed in RakionWorldServ.exe 0x40CF10: 0 = moved, 3 = source slot empty,
/// 4 = item cannot go there (meanings Derived).
/// </summary>
public sealed record InventoryMoveResponsePacket(byte Result, InventorySlotState Source, InventorySlotState Destination) : IPacket
{
    public const byte ResultSuccess = 0;
    public const byte ResultSourceEmpty = 3;
    public const byte ResultNotAllowed = 4;

    public ushort Opcode => (ushort)ServerOpcode.InventoryMove;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        Source.Serialize(writer);
        Destination.Serialize(writer);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryMoveResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 19)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte result = reader.ReadByte();
        var source = InventorySlotState.Read(ref reader);
        var destination = InventorySlotState.Read(ref reader);
        packet = new InventoryMoveResponsePacket(result, source, destination);
        return true;
    }
}

/// <summary>
/// The 19 equipment slots inside each character record of WorldLogin (0x000C) and WorldLoginPart2 (0x000D):
/// 19 x u16 item id, 19 x u8 level, 19 x u32 exp, 19 x u32 limit time.
/// Evidence: array sizes Observed (engine.dll 0x36196450..0x361964F0). Exp and limit time order is Derived from
/// the legacy DBCommandCharacterList copy (RakionWorldServ.exe 0x411E78..0x411F26: item id, level, exp, limit
/// time, then the row id kept server-side), the same order as the account box.
/// </summary>
public static class CharacterEquipment
{
    public const int SlotCount = 19;

    public static void Serialize(PacketWriter writer, IReadOnlyList<ItemSlot>? slots)
    {
        if (slots is not null && slots.Count > SlotCount)
        {
            throw new ArgumentException($"A character has {SlotCount} equipment slots.", nameof(slots));
        }

        for (int i = 0; i < SlotCount; i++)
        {
            writer.WriteUInt16(At(slots, i).ItemId);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            writer.WriteByte(At(slots, i).Level);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            writer.WriteUInt32(At(slots, i).Exp);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            writer.WriteUInt32(At(slots, i).LimitTime);
        }
    }

    private static ItemSlot At(IReadOnlyList<ItemSlot>? slots, int index) =>
        slots is not null && index < slots.Count ? slots[index] : default;
}

