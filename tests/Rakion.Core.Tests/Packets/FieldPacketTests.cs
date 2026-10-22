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

        Assert.AreEqual((ushort)ClientOpcode.FieldList, packet.Opcode);
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

        Assert.AreEqual((ushort)ServerOpcode.FieldList, packet.Opcode);
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


    [TestMethod]
    public void FieldCreateRequestPacket_SerializesAndParsesCorrectly()
    {
        var packet = new FieldCreateRequestPacket(
            RoomName: "Test Room 1v1",
            Password: "secret",
            MapName: "Underground",
            Mode: 1,
            MaxPlayers: 8,
            RoundTime: 3,
            Rounds: 5,
            SubMode: 0,
            LevelLimit: 0,
            Extra1: 0,
            Extra2: 0);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)ClientOpcode.FieldCreate, packet.Opcode);
        Assert.AreEqual(0x003B, (ushort)packet.Opcode);
        bool success = FieldCreateRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual("Test Room 1v1", parsed.RoomName);
        Assert.AreEqual("secret", parsed.Password);
        Assert.AreEqual("Underground", parsed.MapName);
        Assert.AreEqual((byte)1, parsed.Mode);
        Assert.AreEqual((byte)8, parsed.MaxPlayers);
        Assert.AreEqual((byte)3, parsed.RoundTime);
        Assert.AreEqual((ushort)5, parsed.Rounds);
        Assert.AreEqual((byte)0, parsed.SubMode);
        Assert.AreEqual((byte)0, parsed.LevelLimit);
    }

    [TestMethod]
    public void FieldCreateResponsePacket_Success_SerializesAndParsesCorrectly()
    {
        var packet = new FieldCreateResponsePacket(ResultCode: 0, RoomNumber: 42);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldCreate, packet.Opcode);
        Assert.AreEqual(0x003B, (ushort)packet.Opcode);
        bool success = FieldCreateResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual((byte)0, parsed.ResultCode);
        Assert.AreEqual((ushort)42, parsed.RoomNumber);
    }

    [TestMethod]
    public void FieldExitResponsePacket_SerializesAndParsesCorrectly()
    {
        var packet = new FieldExitResponsePacket(ResultCode: 0);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldExit, packet.Opcode);
        Assert.AreEqual(0x003A, (ushort)packet.Opcode);
        bool success = FieldExitResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual((byte)0, parsed.ResultCode);
    }

    [TestMethod]
    public void FieldReadyPackets_SerializeAndParseCorrectly()
    {
        var req = new FieldReadyRequestPacket(ReadyState: 1);
        using var wReq = new PacketWriter(req.Opcode);
        req.Serialize(wReq);
        byte[] reqBytes = wReq.Build();

        Assert.AreEqual((ushort)ClientOpcode.FieldReady, req.Opcode);
        Assert.AreEqual(0x003D, (ushort)req.Opcode);
        bool sReq = FieldReadyRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var pReq);
        Assert.IsTrue(sReq);
        Assert.AreEqual((byte)1, pReq.ReadyState);

        var bcast = new FieldReadyBroadcastPacket(SlotIndex: 2, ReadyState: 1);
        using var wBcast = new PacketWriter(bcast.Opcode);
        bcast.Serialize(wBcast);
        byte[] bcastBytes = wBcast.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldReady, bcast.Opcode);
        Assert.AreEqual(0x003D, (ushort)bcast.Opcode);
        bool sBcast = FieldReadyBroadcastPacket.TryParse(bcastBytes.AsSpan(PacketHeader.HeaderSize), out var pBcast);
        Assert.IsTrue(sBcast);
        Assert.AreEqual((byte)2, pBcast.SlotIndex);
        Assert.AreEqual((byte)1, pBcast.ReadyState);
    }

    [TestMethod]
    public void FieldChatPackets_SerializeAndParseCorrectly()
    {
        var req = new FieldChatRequestPacket("Hello Room!");
        using var wReq = new PacketWriter(req.Opcode);
        req.Serialize(wReq);
        byte[] reqBytes = wReq.Build();

        Assert.AreEqual((ushort)ClientOpcode.FieldChat, req.Opcode);
        Assert.AreEqual(0x0047, (ushort)req.Opcode);
        bool sReq = FieldChatRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var pReq);
        Assert.IsTrue(sReq);
        Assert.AreEqual("Hello Room!", pReq.Message);

        var bcast = new FieldChatBroadcastPacket("Player : Hello Room!");
        using var wBcast = new PacketWriter(bcast.Opcode);
        bcast.Serialize(wBcast);
        byte[] bcastBytes = wBcast.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldChat, bcast.Opcode);
        Assert.AreEqual(0x0047, (ushort)bcast.Opcode);
        bool sBcast = FieldChatBroadcastPacket.TryParse(bcastBytes.AsSpan(PacketHeader.HeaderSize), out var pBcast);
        Assert.IsTrue(sBcast);
        Assert.AreEqual("Player : Hello Room!", pBcast.Message);
    }

    [TestMethod]
    public void FieldChangeTeamPackets_SerializeAndParseCorrectly()
    {
        var req = new FieldChangeTeamRequestPacket();
        using var wReq = new PacketWriter(req.Opcode);
        req.Serialize(wReq);
        byte[] reqBytes = wReq.Build();

        Assert.AreEqual((ushort)ClientOpcode.FieldChangeTeam, req.Opcode);
        Assert.AreEqual(0x003E, (ushort)req.Opcode);
        bool sReq = FieldChangeTeamRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out _);
        Assert.IsTrue(sReq);

        var bcast = new FieldChangeTeamBroadcastPacket(Result: 0, OldSlot: 0, NewSlot: 10);
        using var wBcast = new PacketWriter(bcast.Opcode);
        bcast.Serialize(wBcast);
        byte[] bcastBytes = wBcast.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldChangeTeam, bcast.Opcode);
        Assert.AreEqual(0x003E, (ushort)bcast.Opcode);
        bool sBcast = FieldChangeTeamBroadcastPacket.TryParse(bcastBytes.AsSpan(PacketHeader.HeaderSize), out var pBcast);
        Assert.IsTrue(sBcast);
        Assert.AreEqual((byte)0, pBcast.Result);
        Assert.AreEqual((byte)0, pBcast.OldSlot);
        Assert.AreEqual((byte)10, pBcast.NewSlot);
    }

    [TestMethod]
    public void FieldChangeSlotStatusPackets_SerializeAndParseCorrectly()
    {
        var req = new FieldChangeSlotStatusRequestPacket(SlotIndex: 4, Status: 0);
        using var wReq = new PacketWriter(req.Opcode);
        req.Serialize(wReq);
        byte[] reqBytes = wReq.Build();

        Assert.AreEqual((ushort)ClientOpcode.FieldChangeSlotStatus, req.Opcode);
        Assert.AreEqual(0x0042, (ushort)req.Opcode);
        bool sReq = FieldChangeSlotStatusRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var pReq);
        Assert.IsTrue(sReq);
        Assert.AreEqual((byte)4, pReq.SlotIndex);
        Assert.AreEqual((byte)0, pReq.Status);

        var bcast = new FieldChangeSlotStatusBroadcastPacket(SlotIndex: 4, Status: 0);
        using var wBcast = new PacketWriter(bcast.Opcode);
        bcast.Serialize(wBcast);
        byte[] bcastBytes = wBcast.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldChangeSlotStatus, bcast.Opcode);
        Assert.AreEqual(0x0042, (ushort)bcast.Opcode);
        bool sBcast = FieldChangeSlotStatusBroadcastPacket.TryParse(bcastBytes.AsSpan(PacketHeader.HeaderSize), out var pBcast);
        Assert.IsTrue(sBcast);
        Assert.AreEqual((byte)4, pBcast.SlotIndex);
        Assert.AreEqual((byte)0, pBcast.Status);
    }

    [TestMethod]
    public void FieldChangeRulePackets_SerializeAndParseCorrectly()
    {
        var req = new FieldChangeRuleRequestPacket(
            RoomName: "Updated Name",
            Password: "",
            MapName: "Colosseum",
            Mode: 2,
            MaxPlayers: 12,
            RoundTime: 5,
            Rounds: 3,
            SubMode: 1,
            LevelLimit: 20);

        using var wReq = new PacketWriter(req.Opcode);
        req.Serialize(wReq);
        byte[] reqBytes = wReq.Build();

        Assert.AreEqual((ushort)ClientOpcode.FieldChangeRule, req.Opcode);
        Assert.AreEqual(0x0041, (ushort)req.Opcode);
        bool sReq = FieldChangeRuleRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var pReq);
        Assert.IsTrue(sReq);
        Assert.AreEqual("Updated Name", pReq.RoomName);
        Assert.AreEqual("Colosseum", pReq.MapName);
        Assert.AreEqual((byte)2, pReq.Mode);
        Assert.AreEqual((byte)12, pReq.MaxPlayers);
        Assert.AreEqual((ushort)5, pReq.RoundTime);

        var bcast = new FieldChangeRuleBroadcastPacket(
            RoomName: "Updated Name",
            Password: "",
            MapName: "Colosseum",
            Mode: 2,
            MaxPlayers: 12,
            RoundTime: 5,
            Rounds: 3,
            SubMode: 1,
            LevelLimit: 20);

        using var wBcast = new PacketWriter(bcast.Opcode);
        bcast.Serialize(wBcast);
        byte[] bcastBytes = wBcast.Build();

        Assert.AreEqual((ushort)ServerOpcode.FieldChangeRule, bcast.Opcode);
        Assert.AreEqual(0x0041, (ushort)bcast.Opcode);
        bool sBcast = FieldChangeRuleBroadcastPacket.TryParse(bcastBytes.AsSpan(PacketHeader.HeaderSize), out var pBcast);
        Assert.IsTrue(sBcast);
        Assert.AreEqual("Updated Name", pBcast.RoomName);
    }
}
