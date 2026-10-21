using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class FieldPacketTests
{
    [TestMethod]
    public void FieldListRequestPacket_SerializesAndParsesCorrectly()
    {
        var packet = new FieldListRequestPacket(
            PageSize: 10,
            PageIndex: 2,
            Filter1: 1,
            Filter2: 0,
            Filter3: 2,
            Filter4: 0,
            Filter5: 1,
            Filter6: 0,
            Filter7: 3);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.FieldList, packet.Opcode);
        Assert.AreEqual(0x0036, (ushort)packet.Opcode);
        Assert.HasCount(14, bytes); // 4 header + 10 payload

        bool success = FieldListRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual((byte)10, parsed.PageSize);
        Assert.AreEqual((ushort)2, parsed.PageIndex);
        Assert.AreEqual((byte)1, parsed.Filter1);
        Assert.AreEqual((byte)0, parsed.Filter2);
        Assert.AreEqual((byte)2, parsed.Filter3);
        Assert.AreEqual((byte)0, parsed.Filter4);
        Assert.AreEqual((byte)1, parsed.Filter5);
        Assert.AreEqual((byte)0, parsed.Filter6);
        Assert.AreEqual((byte)3, parsed.Filter7);
    }

    [TestMethod]
    public void FieldListResponsePacket_Empty_SerializesAndParsesCorrectly()
    {
        var packet = FieldListResponsePacket.Empty;
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.FieldList, packet.Opcode);
        Assert.AreEqual(0x0036, (ushort)packet.Opcode);
        Assert.HasCount(5, bytes); // 4 header + 1 payload (count = 0)
        Assert.AreEqual(0, bytes[4]);

        bool success = FieldListResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.IsEmpty(parsed.Fields);
    }

    [TestMethod]
    public void FieldListResponsePacket_WithEntries_SerializesAndParsesCorrectly()
    {
        var entries = new List<FieldListEntry>
        {
            new(
                RoomNumber: 1,
                HasPassword: true,
                IsInProgress: false,
                Mode: 1,
                Map: 3,
                MaxPlayers: 8,
                RoundTime: 5,
                Rounds: 3,
                SubMode: 0,
                LevelLimit: 15,
                CurrentPlayers: 2,
                MaxAllowed: 8,
                HostUserId: 1001,
                HostLevel: 10,
                RoomName: "Pro Only 1v1",
                Extra: 0)
        };

        var packet = new FieldListResponsePacket(entries);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        bool success = FieldListResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.HasCount(1, parsed.Fields);

        var first = parsed.Fields[0];
        Assert.AreEqual((ushort)1, first.RoomNumber);
        Assert.IsTrue(first.HasPassword);
        Assert.IsFalse(first.IsInProgress);
        Assert.AreEqual((byte)1, first.Mode);
        Assert.AreEqual((byte)3, first.Map);
        Assert.AreEqual((byte)8, first.MaxPlayers);
        Assert.AreEqual((byte)5, first.RoundTime);
        Assert.AreEqual((byte)3, first.Rounds);
        Assert.AreEqual((byte)0, first.SubMode);
        Assert.AreEqual((byte)15, first.LevelLimit);
        Assert.AreEqual((byte)2, first.CurrentPlayers);
        Assert.AreEqual((byte)8, first.MaxAllowed);
        Assert.AreEqual(1001u, first.HostUserId);
        Assert.AreEqual((ushort)10, first.HostLevel);
        Assert.AreEqual("Pro Only 1v1", first.RoomName);
        Assert.AreEqual((ushort)0, first.Extra);
    }
}
