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

    [TestMethod]
    public void InventoryEnterResponse_RoundTrip_MatchesObservedLayout()
    {
        var packet = new InventoryEnterResponsePacket(InventoryEnterResponsePacket.ResultSuccess, 0x01020304);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        // Header (length 9, opcode 0x002C) + u8 result + u32 little-endian.
        CollectionAssert.AreEqual(new byte[] { 0x09, 0x00, 0x2C, 0x00, 0x00, 0x04, 0x03, 0x02, 0x01 }, bytes);
        Assert.AreEqual((ushort)ServerOpcode.InventoryEnter, packet.Opcode);
        Assert.IsTrue(InventoryEnterResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual(packet, parsed);
    }

    [TestMethod]
    public void InventoryEnterResponse_TryParse_RejectsShortPayload()
    {
        Assert.IsFalse(InventoryEnterResponsePacket.TryParse([0x00, 0x01, 0x02, 0x03], out _));
    }
}
