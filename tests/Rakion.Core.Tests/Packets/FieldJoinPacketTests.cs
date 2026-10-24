using Rakion.Core.Enums;
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
    public void GamePoint_RoundTrips_AndLevelUpIsLevelThenPoints()
    {
        var report = new FieldGamePointRequestPacket(90, 70, 1, 30, 30, 30, 5);

        byte[] payload = PayloadOf(report);

        Assert.HasCount(FieldGamePointRequestPacket.Size, payload);
        Assert.IsTrue(FieldGamePointRequestPacket.TryParse(payload, out var parsed));
        Assert.AreEqual(report, parsed);
        Assert.IsFalse(FieldGamePointRequestPacket.TryParse(payload[..22], out _));
        CollectionAssert.AreEqual(new byte[] { 12, 33, 0 }, PayloadOf(new CharacterLevelUpPacket(12, 33)));
        CollectionAssert.AreEqual(new byte[] { 10, 12, 2, 0, 5 }, PayloadOf(new FieldPlayerLevelPacket(10, 12, 2, 0, 5)));
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

    [TestMethod]
    public void StagePoint_RoundTrips_AndRejectsOutOfRangeHeaders()
    {
        var report = new FieldGameStagePointRequestPacket(12, 5, [8001, 13002], 300, 120, 4, 0, 9);
        byte[] payload = PayloadOf(report);

        Assert.HasCount(3 + 4 + 20, payload);
        Assert.IsTrue(FieldGameStagePointRequestPacket.TryParse(payload, out var parsed));
        CollectionAssert.AreEqual(new ushort[] { 8001, 13002 }, parsed.Items.ToArray());
        Assert.AreEqual((300u, 120u, 9u), (parsed.Exp, parsed.Gold, parsed.CreatureExp3));
        Assert.IsFalse(FieldGameStagePointRequestPacket.TryParse([100, 1, 0], out _), "stage 100");
        Assert.IsFalse(FieldGameStagePointRequestPacket.TryParse([1, 6, 0], out _), "rank 6");
        Assert.IsFalse(FieldGameStagePointRequestPacket.TryParse([1, 1, 5], out _), "5 items");
        Assert.IsFalse(FieldGameStagePointRequestPacket.TryParse(payload[..^1], out _), "truncated");

        CollectionAssert.AreEqual(new byte[] { 0, 12, 5, 1, 0x41, 0x1F }, PayloadOf(new FieldGameStagePointPacket(0, 12, 5, [8001])));
        CollectionAssert.AreEqual(new byte[] { 1 }, PayloadOf(new FieldGameStagePointPacket(FieldGameStagePointPacket.NotStored)));
        Assert.IsTrue(FieldGameRoundEndRequestPacket.TryParse([3], out var end));
        Assert.AreEqual(FieldGameRoundEndRequestPacket.Failed, end.Reason);
    }

    [TestMethod]
    public void AccountUpgradePackets_MatchTheClientLayouts()
    {
        Assert.IsTrue(CouponRequestPacket.TryParse(ClientOpcode.InventoryBuyBag, [1, 0x10, 0x00], out var coupon));
        Assert.AreEqual((true, (ushort)16), (coupon.UseCoupon, coupon.CouponSlot));
        Assert.IsTrue(CouponRequestPacket.TryParse(ClientOpcode.CharacterStateClear, [0, 0, 0], out var plain));
        Assert.IsFalse(plain.UseCoupon);
        Assert.IsFalse(CouponRequestPacket.TryParse(ClientOpcode.InventoryBuyBag, [1], out _));

        CollectionAssert.AreEqual(
            new byte[] { 0, 0x10, 0x27, 0, 0, 12, 0, 5, 0, 0 },
            PayloadOf(new CharacterStateClearResponsePacket(0, 10000, 12, 5)));
        CollectionAssert.AreEqual(
            new byte[] { 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0 },
            PayloadOf(new AccountUpgradeResponsePacket(ServerOpcode.InventoryBuyBag, 0, 1, 2, 3)));
    }

    [TestMethod]
    public void VotePackets_MatchTheClientLayouts()
    {
        var open = new FieldGameVoteOpenRequestPacket(4, "afk");
        Assert.IsTrue(FieldGameVoteOpenRequestPacket.TryParse(PayloadOf(open), out var parsed));
        Assert.AreEqual(open, parsed);
        Assert.IsTrue(FieldGameVoteRequestPacket.TryParse([2], out var vote));
        Assert.AreEqual(FieldGameVoteRequestPacket.No, vote.Choice);
        Assert.IsFalse(FieldGameVoteRequestPacket.TryParse([4], out _));

        CollectionAssert.AreEqual(new byte[] { 4, (byte)'a', (byte)'f', (byte)'k', 0 }, PayloadOf(new FieldGameVoteOpenPacket(4, "afk")));
        CollectionAssert.AreEqual(new byte[] { 7 }, PayloadOf(new FieldGameVoteResultPacket(FieldGameVoteResultPacket.TargetIsMaster)));
        CollectionAssert.AreEqual(new byte[] { 0, 1, 3, 1, 1, 0, 4 }, PayloadOf(new FieldGameVoteResultPacket(0, true, 3, 1, 1, 0, 4)));
    }

    [TestMethod]
    public void AdminNoticePackets_RoundTripAndRefuseLongFields()
    {
        var request = new AdminNoticeRequestPacket(AdminNoticeRequestPacket.InMatch, "Hanako", "hola");
        Assert.IsTrue(AdminNoticeRequestPacket.TryParse(PayloadOf(request), out var parsed));
        Assert.AreEqual(request, parsed);
        Assert.IsFalse(AdminNoticeRequestPacket.TryParse(PayloadOf(new AdminNoticeRequestPacket(0, new string('a', 13), "x")), out _));
        Assert.IsFalse(AdminNoticeRequestPacket.TryParse(PayloadOf(new AdminNoticeRequestPacket(0, string.Empty, new string('a', 0x81))), out _));

        CollectionAssert.AreEqual(new byte[] { (byte)'o', (byte)'k', 0 }, PayloadOf(new SystemNoticePacket("ok")));
        CollectionAssert.AreEqual(new byte[] { 1 }, PayloadOf(new AdminNoticeResponsePacket(AdminNoticeResponsePacket.NotOnline)));
    }

    private static byte[] PayloadOf(IPacket packet)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        return writer.Build()[PacketHeader.HeaderSize..].ToArray();
    }
}
