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
/// (opcode 0x002D, 2-byte message).
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
