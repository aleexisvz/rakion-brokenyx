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
///   u8                     BagCount: unlocked bags, 30 slots each (Derived, see below)
///   120 x u16              ItemId per slot
///   120 x u8               Level per slot
///   120 x u32              Exp per slot
///   120 x u32              LimitTime per slot (LegacyGameClock minutes, 0 = permanent; Inferred)
///   u32                    Stage level pass purchase time (LegacyGameClock minutes, 0 = none). engine.dll 0x36196C4C passes
///                          it to rakion.bin 0x47E781, which keeps it at AccountInfo+0x2D98; 0x47E7A7 turns it into the
///                          hours left of the 24 h pass, and the Stage list (0x46553F) skips its level check while more
///                          than 0.1 h remain. Observed offsets; the legacy agent stores the purchase minute (0x418738).
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

    /// <summary>Stage level pass purchase time in LegacyGameClock minutes; 0 when the account has none.</summary>
    public uint StageLevelFreeBoughtMinute { get; init; }

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

        writer.WriteUInt32(StageLevelFreeBoughtMinute);
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

/// <summary>
/// Client shop purchase: [u16 ItemId][u8 PayWithGold][u8 UsesCoupon] and, when UsesCoupon is non-zero,
/// [u16 CouponSlot].
/// Evidence: Observed in engine.dll ?SendInventoryBuy@IScavengerWorldNet@@UAEXGEEG@Z (0x002E, sends the
/// last u16 only when the third argument is non-zero). Meanings Derived from the legacy server
/// (RakionWorldServ.exe 0x4212BE, 0x40CB10): a non-zero PayWithGold checks the gold price and balance,
/// zero the cash ones; UsesCoupon = 1 reads a coupon item (ids 11000..11999) from box slot CouponSlot.
/// </summary>
public sealed record InventoryBuyRequestPacket(ushort ItemId, bool PayWithGold, byte UsesCoupon = 0, ushort CouponSlot = 0) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventoryBuy;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(ItemId);
        writer.WriteByte(PayWithGold ? (byte)1 : (byte)0);
        writer.WriteByte(UsesCoupon);
        if (UsesCoupon != 0)
        {
            writer.WriteUInt16(CouponSlot);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryBuyRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 4)
        {
            return false;
        }

        var reader = new PacketReader(span);
        ushort itemId = reader.ReadUInt16();
        bool payWithGold = reader.ReadByte() != 0;
        byte usesCoupon = reader.ReadByte();
        ushort couponSlot = 0;
        if (usesCoupon != 0)
        {
            if (reader.Remaining < 2)
            {
                return false;
            }

            couponSlot = reader.ReadUInt16();
        }

        packet = new InventoryBuyRequestPacket(itemId, payWithGold, usesCoupon, couponSlot);
        return true;
    }
}

/// <summary>One item a purchase put into the account box.</summary>
public readonly record struct BoughtItem(byte BoxSlot, ushort ItemId, byte Level);

/// <summary>
/// Server result for <see cref="InventoryBuyRequestPacket"/>:
///   u8 Result, u32 Gold, u32 Cash, u8 N,
///   (Result == 0 only) N x u8 box slot, N x u16 item id, N x u8 level,
///   u8 M, M x u32 (UNKNOWN: purpose not yet verified; sent empty).
/// Evidence: layout Observed in engine.dll 0x36195640. rakion.bin 0x4774E0 stores Gold at account+0x64 and
/// Cash at account+0x68 (the balances after the purchase, Derived) and places each item into the box record
/// of its slot. Result codes Observed in RakionWorldServ.exe 0x40CB10 (meanings Derived): 0 bought, 3 cannot
/// be bought or not enough money, 4 box full, 0x14/0x15/0x16 coupon errors.
/// </summary>
public sealed record InventoryBuyResponsePacket(byte Result, uint Gold, uint Cash, IReadOnlyList<BoughtItem>? Items = null) : IPacket
{
    public const byte ResultSuccess = 0;
    public const byte ResultNotBuyable = 3;
    public const byte ResultBoxFull = 4;
    public const byte ResultCouponInvalid = 0x14;

    public ushort Opcode => (ushort)ServerOpcode.InventoryBuy;

    public void Serialize(PacketWriter writer)
    {
        var items = Result == ResultSuccess ? Items ?? [] : [];
        if (items.Count > byte.MaxValue)
        {
            throw new InvalidOperationException("At most 255 items fit in one purchase result.");
        }

        writer.WriteByte(Result);
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteByte((byte)items.Count);
        foreach (var item in items)
        {
            writer.WriteByte(item.BoxSlot);
        }

        foreach (var item in items)
        {
            writer.WriteUInt16(item.ItemId);
        }

        foreach (var item in items)
        {
            writer.WriteByte(item.Level);
        }

        writer.WriteByte(0); // UNKNOWN: count of the trailing u32 list.
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryBuyResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 11)
        {
            return false;
        }

        byte result = reader.ReadByte();
        uint gold = reader.ReadUInt32();
        uint cash = reader.ReadUInt32();
        int count = reader.ReadByte();
        var items = new List<BoughtItem>();
        if (result == ResultSuccess && count > 0)
        {
            if (reader.Remaining < count * 4 + 1)
            {
                return false;
            }

            byte[] slots = reader.ReadBytes(count).ToArray();
            var ids = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = reader.ReadUInt16();
            }

            for (int i = 0; i < count; i++)
            {
                items.Add(new BoughtItem(slots[i], ids[i], reader.ReadByte()));
            }
        }

        if (reader.Remaining < 1)
        {
            return false;
        }

        packet = new InventoryBuyResponsePacket(result, gold, cash, items);
        return true;
    }
}

/// <summary>
/// Client request to sell the item in an account box slot: [u8 BoxSlot].
/// Evidence: Observed in engine.dll ?SendInventorySell@IScavengerWorldNet@@UAEXE@Z (0x002F, 3-byte message);
/// the legacy server reads it as a box slot and rejects values of 120 or more (RakionWorldServ.exe 0x421606).
/// </summary>
public sealed record InventorySellRequestPacket(byte BoxSlot) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventorySell;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(BoxSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventorySellRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new InventorySellRequestPacket(span[0]);
        return true;
    }
}

/// <summary>
/// Server result for <see cref="InventorySellRequestPacket"/>: [u8 Result][u32 Gold][u32 Cash][u8 BoxSlot].
/// Evidence: Observed in engine.dll 0x361936A0. rakion.bin 0x478A70 applies it only when Result is 0: Gold and
/// Cash become the account balances and the box record of BoxSlot is cleared (Derived).
/// Result codes from RakionWorldServ.exe 0x40CD70 (meanings Derived): 0 sold, 3 empty slot or not sellable.
/// </summary>
public sealed record InventorySellResponsePacket(byte Result, uint Gold, uint Cash, byte BoxSlot) : IPacket
{
    public const byte ResultSuccess = 0;
    public const byte ResultNotSellable = 3;

    public ushort Opcode => (ushort)ServerOpcode.InventorySell;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteByte(BoxSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventorySellResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 10)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new InventorySellResponsePacket(reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadByte());
        return true;
    }
}

/// <summary>
/// Client request to spend one stat point: [u8 Stat].
/// Evidence: Observed in engine.dll ?SendInventoryAllocationPoint@IScavengerWorldNet@@UAEXE@Z (0x0033, 3-byte message).
/// </summary>
public sealed record InventoryAllocationPointRequestPacket(CharacterStat Stat) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventoryAllocationPoint;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Stat);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryAllocationPointRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new InventoryAllocationPointRequestPacket((CharacterStat)span[0]);
        return true;
    }
}

/// <summary>
/// Server result for <see cref="InventoryAllocationPointRequestPacket"/>:
/// [u8 Result][u16 LevelPoints][u16 PowerPoints][u8 Stat][u16 NewValue].
/// Evidence: Observed in engine.dll 0x361938A0. rakion.bin 0x47DBB0 applies it only on Result 0: NewValue goes
/// to the stat chosen by Stat, LevelPoints to the character record (+0x34) and PowerPoints to account+0x58
/// (meanings Derived). Result codes Observed in RakionWorldServ.exe 0x40B3D0 (meanings Derived): 0 spent,
/// 3 no points left, 4 stat at its maximum, 5 unknown stat.
/// </summary>
public sealed record InventoryAllocationPointResponsePacket(
    byte Result,
    ushort LevelPoints,
    ushort PowerPoints,
    CharacterStat Stat,
    ushort NewValue) : IPacket
{
    public const byte ResultSuccess = 0;
    public const byte ResultNoPoints = 3;
    public const byte ResultStatAtMaximum = 4;
    public const byte ResultUnknownStat = 5;

    public ushort Opcode => (ushort)ServerOpcode.InventoryAllocationPoint;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt16(LevelPoints);
        writer.WriteUInt16(PowerPoints);
        writer.WriteByte((byte)Stat);
        writer.WriteUInt16(NewValue);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryAllocationPointResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 8)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new InventoryAllocationPointResponsePacket(
            reader.ReadByte(),
            reader.ReadUInt16(),
            reader.ReadUInt16(),
            (CharacterStat)reader.ReadByte(),
            reader.ReadUInt16());
        return true;
    }
}

/// <summary>
/// Client request to buy 30 days of PowerUser: [u8 Extend][u8 UsesCoupon] and, when UsesCoupon is non-zero,
/// [u16 CouponSlot].
/// Evidence: Observed in engine.dll ?SendInventoryBuyPowerUser@IScavengerWorldNet@@UAEXEEG@Z (0x0034). The legacy
/// server accepts only 0 or 1 in the first byte (RakionWorldServ.exe 0x422B4B) and, when it is non-zero, prices the
/// card at 6000 cash and adds 30 days to the current expiry; otherwise 8000 cash and expiry = now + 30 days
/// (0x40B2C0, DBCommandInventoryBuyPowerUser SQL). Meaning of the byte as "extend" Derived.
/// </summary>
public sealed record InventoryBuyPowerUserRequestPacket(bool Extend, byte UsesCoupon = 0, ushort CouponSlot = 0) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventoryBuyPowerUser;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Extend ? (byte)1 : (byte)0);
        writer.WriteByte(UsesCoupon);
        if (UsesCoupon != 0)
        {
            writer.WriteUInt16(CouponSlot);
        }
    }

    /// <summary>Rejects a first byte other than 0 or 1, as the legacy server does.</summary>
    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryBuyPowerUserRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 2 || span[0] > 1)
        {
            return false;
        }

        ushort couponSlot = 0;
        if (span[1] != 0)
        {
            if (span.Length < 4)
            {
                return false;
            }

            couponSlot = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
        }

        packet = new InventoryBuyPowerUserRequestPacket(span[0] == 1, span[1], couponSlot);
        return true;
    }
}

/// <summary>
/// Server result for <see cref="InventoryBuyPowerUserRequestPacket"/>:
///   u8 Result, u32 Gold, u32 Cash, u32 PowerUserExpiry, u16 PowerUserPoints, u8 M, M x u32 (UNKNOWN; sent empty).
/// Evidence: layout Observed in engine.dll 0x361958C0. rakion.bin 0x474F50 stores Gold/Cash as balances, the expiry
/// (LegacyGameClock minutes, converted to days/hours/minutes left) and the points at account+0x58 (Derived).
/// Result codes Observed in RakionWorldServ.exe 0x40B2C0 / DBCommandInventoryBuyPowerUser (meanings Derived):
/// 0 bought, 3 not enough cash, 4 expiry already more than 180 days ahead, 0x14..0x16 coupon errors.
/// </summary>
public sealed record InventoryBuyPowerUserResponsePacket(
    byte Result,
    uint Gold,
    uint Cash,
    uint PowerUserExpiry,
    ushort PowerUserPoints) : IPacket
{
    public const byte ResultSuccess = 0;
    public const byte ResultNotEnoughCash = 3;
    public const byte ResultLimitReached = 4;
    public const byte ResultCouponInvalid = 0x14;

    public ushort Opcode => (ushort)ServerOpcode.InventoryBuyPowerUser;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteUInt32(PowerUserExpiry);
        writer.WriteUInt16(PowerUserPoints);
        writer.WriteByte(0); // UNKNOWN: count of the trailing u32 list.
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryBuyPowerUserResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 16)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new InventoryBuyPowerUserResponsePacket(
            reader.ReadByte(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt16());
        return true;
    }
}

/// <summary>
/// Client blacksmith request: [u8 TargetSlot][u8 StoneSlot][u8 N][N x u8 MaterialSlot], all account box slots.
/// Evidence: Observed in engine.dll ?SendEncahntReinforce@IScavengerWorldNet@@UAEXEEEPAE@Z (0x0074) and in the legacy
/// parser RakionWorldServ.exe 0x421E80, which disconnects when N is above 3 (meanings Derived from 0x40C310).
/// </summary>
public sealed record EnchantReinforceRequestPacket(byte TargetSlot, byte StoneSlot, IReadOnlyList<byte> MaterialSlots) : IPacket
{
    public const int MaxMaterials = 3;

    public ushort Opcode => (ushort)ClientOpcode.EnchantReinforce;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(TargetSlot);
        writer.WriteByte(StoneSlot);
        writer.WriteByte((byte)MaterialSlots.Count);
        foreach (byte slot in MaterialSlots)
        {
            writer.WriteByte(slot);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out EnchantReinforceRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 3 || span[2] > MaxMaterials || span.Length < 3 + span[2])
        {
            return false;
        }

        packet = new EnchantReinforceRequestPacket(span[0], span[1], span.Slice(3, span[2]).ToArray());
        return true;
    }

    public bool Equals(EnchantReinforceRequestPacket? other) =>
        other is not null && TargetSlot == other.TargetSlot && StoneSlot == other.StoneSlot && MaterialSlots.SequenceEqual(other.MaterialSlots);

    public override int GetHashCode() => HashCode.Combine(TargetSlot, StoneSlot, MaterialSlots.Count);
}

/// <summary>
/// Server result for <see cref="EnchantReinforceRequestPacket"/>.
/// Results below 6 mean the attempt happened: the client clears the stone and material slots and applies the outcome
/// to the target (rakion.bin 0x478D80, Observed): 0 level + 1, 1 no change, 2/3/4 level - 1/2/3, 5 item destroyed.
/// 6 and above are refusals that consume nothing (legacy RakionWorldServ.exe 0x40C310: 6 not reinforceable or at the
/// cap, 7 invalid stone or material, 8 stone not usable at this level). Meanings Derived.
/// </summary>
public sealed record EnchantReinforceResponsePacket(
    byte Result,
    byte TargetSlot,
    byte StoneSlot,
    IReadOnlyList<byte> MaterialSlots) : IPacket
{
    public const byte LevelUp = 0;
    public const byte NoChange = 1;
    public const byte LevelDown1 = 2;
    public const byte LevelDown2 = 3;
    public const byte LevelDown3 = 4;
    public const byte Destroyed = 5;
    public const byte NotReinforceable = 6;
    public const byte InvalidStoneOrMaterial = 7;
    public const byte StoneNotForThisLevel = 8;

    public ushort Opcode => (ushort)ServerOpcode.EnchantReinforce;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        if (Result >= NotReinforceable)
        {
            return;
        }

        writer.WriteByte(TargetSlot);
        writer.WriteByte(StoneSlot);
        writer.WriteByte((byte)MaterialSlots.Count);
        foreach (byte slot in MaterialSlots)
        {
            writer.WriteByte(slot);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out EnchantReinforceResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        if (span[0] >= NotReinforceable)
        {
            packet = new EnchantReinforceResponsePacket(span[0], 0, 0, []);
            return true;
        }

        if (span.Length < 4 || span.Length < 4 + span[3])
        {
            return false;
        }

        packet = new EnchantReinforceResponsePacket(span[0], span[1], span[2], span.Slice(4, span[3]).ToArray());
        return true;
    }

    public bool Equals(EnchantReinforceResponsePacket? other) =>
        other is not null && Result == other.Result && TargetSlot == other.TargetSlot && StoneSlot == other.StoneSlot
        && MaterialSlots.SequenceEqual(other.MaterialSlots);

    public override int GetHashCode() => HashCode.Combine(Result, TargetSlot, StoneSlot, MaterialSlots.Count);
}

/// <summary>
/// A potion drunk during a match (0x006E): [u8 EquipmentSlot][u16 ItemId]. The legacy server (RakionWorldServ.exe
/// 0x428C90 -> 0x40E5F0, Observed) checks that the character's equipment slot (the 19-entry arrays loaded at
/// 0x40BE4F, where the potion slots live) holds that item with a quantity left, lowers the quantity (kept in the
/// item's exp) by one and answers nothing.
/// </summary>
public sealed record FieldGamePotionRequestPacket(byte EquipmentSlot, ushort ItemId) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldGamePotion;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(EquipmentSlot);
        writer.WriteUInt16(ItemId);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGamePotionRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 3)
        {
            return false;
        }

        packet = new FieldGamePotionRequestPacket(span[0], (ushort)(span[1] | (span[2] << 8)));
        return true;
    }
}

/// <summary>Potion stack request (0x0073): [u8 SourceSlot][u8 TargetSlot], both account box slots (RakionWorldServ.exe 0x421AC1, Observed).</summary>
public sealed record InventoryStackPotionRequestPacket(byte SourceSlot, byte TargetSlot) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.InventoryStackPotion;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(SourceSlot);
        writer.WriteByte(TargetSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out InventoryStackPotionRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        packet = new InventoryStackPotionRequestPacket(span[0], span[1]);
        return true;
    }
}

/// <summary>
/// Potion stack answer (0x0073): [u8 Result]. rakion.bin 0x4782E0 merges the slots on 0 and shows a message for 1 and
/// 2 (Observed); which refusal maps to 1 or 2 is Inferred.
/// </summary>
public sealed record InventoryStackPotionResponsePacket(byte Result) : IPacket
{
    public const byte Stacked = 0;
    public const byte Refused = 1;

    public ushort Opcode => (ushort)ServerOpcode.InventoryStackPotion;

    public void Serialize(PacketWriter writer) => writer.WriteByte(Result);
}
