using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class InventoryPacketTests
{
    [TestMethod]
    public void InventoryEnterRequest_RoundTrip_IsOpcodeOnly()
    {
        var packet = new InventoryEnterRequestPacket();
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)0x002C, packet.Opcode);
        Assert.AreEqual((ushort)ClientOpcode.InventoryEnter, packet.Opcode);
        Assert.HasCount(PacketHeader.HeaderSize, bytes);
        Assert.IsTrue(InventoryEnterRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out _));
    }

    [TestMethod]
    public void InventoryLeaveRequest_RoundTrip_IsOpcodeOnly()
    {
        var packet = new InventoryLeaveRequestPacket();
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)0x002D, packet.Opcode);
        Assert.AreEqual((ushort)ClientOpcode.InventoryLeave, packet.Opcode);
        Assert.HasCount(PacketHeader.HeaderSize, bytes);
        Assert.IsTrue(InventoryLeaveRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out _));
    }
}
