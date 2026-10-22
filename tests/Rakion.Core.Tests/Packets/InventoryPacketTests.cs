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

    [TestMethod]
    public void AccountBoxBlock_RoundTrip_PreservesSlotsAndExpiredItems()
    {
        var slots = new AccountBoxSlot[AccountBoxBlock.SlotCount];
        slots[0] = new AccountBoxSlot(1001, 3, 50, 0);
        slots[119] = new AccountBoxSlot(2002, 7, 0, 29_000_000);
        var block = new AccountBoxBlock(slots, [3003, 3004], bagCount: 2);

        using var writer = new PacketWriter();
        block.Serialize(writer);
        byte[] bytes = writer.WrittenSpan.ToArray();

        Assert.HasCount(AccountBoxBlock.EmptyBlockSize + 2 * 2, bytes);
        Assert.IsTrue(AccountBoxBlock.TryParse(bytes, out var parsed));
        CollectionAssert.AreEqual(new ushort[] { 3003, 3004 }, parsed.ExpiredItemIds.ToArray());
        Assert.AreEqual(slots[0], parsed.Slots[0]);
        Assert.AreEqual(slots[119], parsed.Slots[119]);
        Assert.IsTrue(parsed.Slots[1].IsEmpty);
        Assert.AreEqual((byte)2, parsed.BagCount);
    }

    [TestMethod]
    public void AccountBoxBlock_TryParse_RejectsTruncatedBlock()
    {
        using var writer = new PacketWriter();
        AccountBoxBlock.Empty.Serialize(writer);
        byte[] bytes = writer.WrittenSpan.ToArray();

        Assert.IsFalse(AccountBoxBlock.TryParse(bytes.AsSpan(0, bytes.Length - 1), out _));
    }

    [TestMethod]
    public void WorldLoginPart2_WithAccountBox_WritesSlotArraysAtObservedOffsets()
    {
        var slots = new AccountBoxSlot[AccountBoxBlock.SlotCount];
        slots[5] = new AccountBoxSlot(0x1234, 9, 0xAABBCCDD, 0x01020304);
        var packet = new WorldLoginPart2Packet(UnlockedSlots: 3, AccountBox: new AccountBoxBlock(slots, bagCount: 1));

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        var payload = writer.Build().AsSpan(PacketHeader.HeaderSize);

        Assert.AreEqual(WorldLoginPart2Packet.BasePayloadSize, payload.Length);
        Assert.AreEqual((byte)1, payload[1]); // BagCount follows the (empty) expired list.
        const int ItemIds = 2;
        const int Levels = ItemIds + 240;
        const int Exps = Levels + 120;
        const int LimitTimes = Exps + 480;
        Assert.AreEqual((ushort)0x1234, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload[(ItemIds + 5 * 2)..]));
        Assert.AreEqual((byte)9, payload[Levels + 5]);
        Assert.AreEqual(0xAABBCCDDu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload[(Exps + 5 * 4)..]));
        Assert.AreEqual(0x01020304u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload[(LimitTimes + 5 * 4)..]));
    }

    [TestMethod]
    public void InventoryLeaveResponse_RoundTrip_IsOneResultByte()
    {
        var packet = new InventoryLeaveResponsePacket(InventoryLeaveResponsePacket.ResultSuccess);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        CollectionAssert.AreEqual(new byte[] { 0x05, 0x00, 0x2D, 0x00, 0x00 }, bytes);
        Assert.IsTrue(InventoryLeaveResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual(packet, parsed);
    }
}
