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
        var slots = new ItemSlot[AccountBoxBlock.SlotCount];
        slots[0] = new ItemSlot(1001, 3, 50, 0);
        slots[119] = new ItemSlot(2002, 7, 0, 29_000_000);
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
        var slots = new ItemSlot[AccountBoxBlock.SlotCount];
        slots[5] = new ItemSlot(0x1234, 9, 0xAABBCCDD, 0x01020304);
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

    [TestMethod]
    public void InventoryMoveRequest_RoundTrip_AndRangeRule()
    {
        var packet = new InventoryMoveRequestPacket(InventoryLocation.AccountBox, 0, InventoryLocation.AccountBox, 119);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        CollectionAssert.AreEqual(new byte[] { 0x08, 0x00, 0x31, 0x00, 0x00, 0x00, 0x00, 0x77 }, bytes);
        Assert.IsTrue(InventoryMoveRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual(packet, parsed);
        Assert.IsTrue(parsed.IsInRange);
        Assert.IsFalse((parsed with { DestinationSlot = 120 }).IsInRange);
        Assert.IsFalse((parsed with { SourceLocation = InventoryLocation.Equipment, SourceSlot = 19 }).IsInRange);
    }

    [TestMethod]
    public void InventoryMoveResponse_RoundTrip_Is19Bytes()
    {
        var packet = new InventoryMoveResponsePacket(
            InventoryMoveResponsePacket.ResultSuccess,
            new InventorySlotState(InventoryLocation.AccountBox, 0, 0, 0, 0),
            new InventorySlotState(InventoryLocation.AccountBox, 5, 1002, 5, 77));
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.HasCount(PacketHeader.HeaderSize + 19, bytes);
        Assert.AreEqual((byte)5, bytes[PacketHeader.HeaderSize + 11]);
        Assert.IsTrue(InventoryMoveResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual(packet, parsed);
    }

    [TestMethod]
    public void CharacterEquipment_WritesFourParallelArraysAtObservedSizes()
    {
        var slots = new ItemSlot[CharacterEquipment.SlotCount];
        slots[3] = new ItemSlot(0x1234, 7, 0x11223344, 0x55667788);

        using var writer = new PacketWriter();
        CharacterEquipment.Serialize(writer, slots);
        var bytes = writer.WrittenSpan;

        Assert.AreEqual(0x26 + 0x13 + 0x4C + 0x4C, bytes.Length);
        Assert.AreEqual((ushort)0x1234, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[(3 * 2)..]));
        Assert.AreEqual((byte)7, bytes[0x26 + 3]);
        Assert.AreEqual(0x11223344u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[(0x26 + 0x13 + 3 * 4)..]));
        Assert.AreEqual(0x55667788u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[(0x26 + 0x13 + 0x4C + 3 * 4)..]));
    }

    [TestMethod]
    public void WorldLogin_WithEquipment_KeepsPacketSize()
    {
        var slots = new ItemSlot[CharacterEquipment.SlotCount];
        slots[0] = new ItemSlot(1001, 1, 0, 0);
        var bare = new CharacterSummary(1, "Hero", 0, CharacterClass.Swordman, 1, 0, 0);

        byte[] without = Build(new WorldLoginResponsePacket(0, 1, Characters: [bare], Username: "admin"));
        byte[] with = Build(new WorldLoginResponsePacket(0, 1, Characters: [bare with { Equipment = slots }], Username: "admin"));

        Assert.HasCount(without.Length, with);
        Assert.AreEqual(3, Enumerable.Range(0, with.Length).Count(i => with[i] != without[i])); // u16 id (2 bytes) + level
    }

    [TestMethod]
    public void InventoryBuyRequest_RoundTrip_WithAndWithoutCoupon()
    {
        var plain = new InventoryBuyRequestPacket(1001, PayWithGold: true);
        var coupon = new InventoryBuyRequestPacket(12000, PayWithGold: false, UsesCoupon: 1, CouponSlot: 7);

        byte[] plainBytes = Build(plain);
        byte[] couponBytes = Build(coupon);

        CollectionAssert.AreEqual(new byte[] { 0x08, 0x00, 0x2E, 0x00, 0xE9, 0x03, 0x01, 0x00 }, plainBytes);
        Assert.HasCount(PacketHeader.HeaderSize + 6, couponBytes);
        Assert.IsTrue(InventoryBuyRequestPacket.TryParse(plainBytes.AsSpan(PacketHeader.HeaderSize), out var parsedPlain));
        Assert.IsTrue(InventoryBuyRequestPacket.TryParse(couponBytes.AsSpan(PacketHeader.HeaderSize), out var parsedCoupon));
        Assert.AreEqual(plain, parsedPlain);
        Assert.AreEqual(coupon, parsedCoupon);
        Assert.IsFalse(InventoryBuyRequestPacket.TryParse(couponBytes.AsSpan(PacketHeader.HeaderSize, 4), out _));
    }

    [TestMethod]
    public void InventoryBuyResponse_RoundTrip_SuccessAndFailure()
    {
        var success = new InventoryBuyResponsePacket(0, 2300, 50, [new BoughtItem(29, 1001, 1)]);
        var failure = new InventoryBuyResponsePacket(InventoryBuyResponsePacket.ResultBoxFull, 5000, 50, [new BoughtItem(1, 1, 1)]);

        byte[] successBytes = Build(success);
        byte[] failureBytes = Build(failure);

        Assert.HasCount(PacketHeader.HeaderSize + 15, successBytes); // 10 + 1 slot + 2 id + 1 level + 1 count
        Assert.HasCount(PacketHeader.HeaderSize + 11, failureBytes); // failures carry no item arrays
        Assert.IsTrue(InventoryBuyResponsePacket.TryParse(successBytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual(2300u, parsed.Gold);
        Assert.AreEqual(new BoughtItem(29, 1001, 1), parsed.Items!.Single());
        Assert.IsTrue(InventoryBuyResponsePacket.TryParse(failureBytes.AsSpan(PacketHeader.HeaderSize), out var parsedFailure));
        Assert.IsEmpty(parsedFailure.Items!);
    }

    [TestMethod]
    public void InventorySell_RequestAndResponse_RoundTrip()
    {
        var request = new InventorySellRequestPacket(28);
        var response = new InventorySellResponsePacket(InventorySellResponsePacket.ResultSuccess, 7080, 50000, 28);

        byte[] requestBytes = Build(request);
        byte[] responseBytes = Build(response);

        CollectionAssert.AreEqual(new byte[] { 0x05, 0x00, 0x2F, 0x00, 0x1C }, requestBytes);
        Assert.HasCount(PacketHeader.HeaderSize + 10, responseBytes);
        Assert.IsTrue(InventorySellRequestPacket.TryParse(requestBytes.AsSpan(PacketHeader.HeaderSize), out var parsedRequest));
        Assert.IsTrue(InventorySellResponsePacket.TryParse(responseBytes.AsSpan(PacketHeader.HeaderSize), out var parsedResponse));
        Assert.AreEqual(request, parsedRequest);
        Assert.AreEqual(response, parsedResponse);
    }

    private static byte[] Build(IPacket packet)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        return writer.Build();
    }
}
