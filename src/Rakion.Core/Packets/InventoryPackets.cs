using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Client request to open the inventory screen. The client sends the opcode only.
/// Evidence: Observed in engine.dll ?SendInventoryEnter@IScavengerWorldNet@@UAEXXZ
/// (opcode 0x002C, 2-byte message).
/// The server response layout is not modelled yet. UNKNOWN: inventory list layout.
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
