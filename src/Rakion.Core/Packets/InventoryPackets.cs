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
/// One slot of the account-wide item box (rows with characterid = 0).
/// Evidence: Observed client record layout (engine.dll 0x36196B90..0x36196C3F): ItemId u16 at +0x00,
/// Level u8 at +0x08, then two u32 at +0x14 and +0x20. That those are Exp and LimitTime is Derived
/// from the order the legacy server copies them (RakionWorldServ.exe 0x412065..0x4120E6).
/// </summary>
public readonly record struct AccountBoxSlot(ushort ItemId, byte Level, uint Exp, uint LimitTime)
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

    private readonly AccountBoxSlot[] _slots;
    private readonly ushort[] _expiredItemIds;

    public AccountBoxBlock(IReadOnlyList<AccountBoxSlot>? slots = null, IReadOnlyList<ushort>? expiredItemIds = null, byte bagCount = 0)
    {
        if (slots is not null && slots.Count > SlotCount)
        {
            throw new ArgumentException($"The account box has {SlotCount} slots.", nameof(slots));
        }

        if (expiredItemIds is not null && expiredItemIds.Count > byte.MaxValue)
        {
            throw new ArgumentException("At most 255 expired items fit in the block.", nameof(expiredItemIds));
        }

        _slots = new AccountBoxSlot[SlotCount];
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

    public IReadOnlyList<AccountBoxSlot> Slots => _slots;
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

        var slots = new AccountBoxSlot[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            slots[i] = new AccountBoxSlot(itemIds[i], levels[i], exps[i], reader.ReadUInt32());
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
