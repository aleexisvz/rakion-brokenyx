using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class FieldJoinPacketTests
{
    private static readonly FieldPlayerRecord Player = new(
        "Admin",
        string.Empty,
        Class: 2,
        Level: 15,
        [new ItemSlot(1011, 12, 0, 0), new ItemSlot(1111, 3, 0, 0)]);

    [TestMethod]
    public void PlayerRecord_HasLegacyLayout()
    {
        using var writer = new PacketWriter();
        Player.Serialize(writer);
        byte[] bytes = writer.WrittenSpan.ToArray();

        // "Admin\0" + "\0" + 16 fixed bytes + 19 x u16 + 19 x u8
        Assert.HasCount(6 + 1 + 16 + 38 + 19, bytes);
        Assert.AreEqual((byte)2, bytes[7 + 13]);
        Assert.AreEqual((byte)15, bytes[7 + 14]);
        Assert.AreEqual((byte)0xF3, bytes[23]);    // 1011 = 0x03F3, little-endian
        Assert.AreEqual((byte)12, bytes[23 + 38]);
    }

    [TestMethod]
    public void PlayerEnter_Success_RoundTrips()
    {
        var packet = new FieldPlayerEnterPacket(FieldPlayerEnterPacket.Entered, 10, FieldSlotInfo.Waiting, 7, 0, Player);

        Assert.IsTrue(FieldPlayerEnterPacket.TryParse(PayloadOf(packet), out var parsed));

        Assert.AreEqual((byte)10, parsed.SlotIndex);
        Assert.AreEqual((ushort)7, parsed.PlayerId);
        Assert.AreEqual("Admin", parsed.Player!.Name);
        Assert.AreEqual((ushort)1111, parsed.Player.Equipment![1].ItemId);
        Assert.AreEqual((ushort)0x0038, packet.Opcode);
    }

    [TestMethod]
    public void SlotUdp_IsOneSlotByte()
    {
        var packet = new FieldSlotUdpPacket(10);

        CollectionAssert.AreEqual(new byte[] { 10 }, PayloadOf(packet));
        Assert.IsTrue(FieldSlotUdpPacket.TryParse([3], out var parsed));
        Assert.AreEqual((byte)3, parsed.SlotIndex);
        Assert.AreEqual((ushort)0x0062, packet.Opcode);
    }

    [TestMethod]
    public void PlayerEnter_Failure_IsOnlyTheResult()
    {
        CollectionAssert.AreEqual(new byte[] { 7 }, PayloadOf(new FieldPlayerEnterPacket(FieldPlayerEnterPacket.LevelOutOfRange)));
    }

    [TestMethod]
    public void FieldInfo_RoundTrips_AndSkipsEmptyAndClosedSlots()
    {
        var slots = Enumerable.Range(0, FieldInfoPacket.SlotCount)
            .Select(i => i switch
            {
                0 => new FieldSlotInfo(FieldSlotInfo.Waiting, 1, 0, Player),
                10 => new FieldSlotInfo(FieldSlotInfo.Ready, 2, 0, Player with { Name = "Guest" }),
                < 4 or (> 10 and < 14) => new FieldSlotInfo(FieldSlotInfo.Empty, 0, 0, null),
                _ => new FieldSlotInfo(FieldSlotInfo.Closed, 0, 0, null),
            })
            .ToList();
        var packet = new FieldInfoPacket(3, 1, 0, 0, 1, 1, 99, 0, 0, 5, 600, 20, "room", string.Empty, "map", slots);

        byte[] payload = PayloadOf(packet);

        Assert.AreEqual((byte)1, payload[5], "mode is the sixth byte");
        Assert.IsTrue(FieldInfoPacket.TryParse(payload, out var parsed));
        Assert.AreEqual((ushort)600, parsed.TimeLimit);
        Assert.AreEqual("map", parsed.MapName);
        Assert.AreEqual("Guest", parsed.Slots[10].Player!.Name);
        Assert.IsNull(parsed.Slots[5].Player);
        Assert.AreEqual(FieldSlotInfo.Closed, parsed.Slots[19].State);
    }

    private static byte[] PayloadOf(IPacket packet)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        return writer.Build()[PacketHeader.HeaderSize..].ToArray();
    }
}
