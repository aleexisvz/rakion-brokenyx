using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class ChannelPacketTests
{
    [TestMethod]
    public void ChannelListRequestPacket_SerializesAndParsesCorrectly()
    {
        var packet = new ChannelListRequestPacket(Category: 1, SubCategory: 2);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelList, packet.Opcode);
        bool success = ChannelListRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual(1, parsed.Category);
        Assert.AreEqual(2, parsed.SubCategory);
    }

    [TestMethod]
    public void ChannelListResponsePacket_SerializesAndParsesCorrectly()
    {
        var channels = new List<ChannelListEntry>
        {
            new(0, ChannelType.Beginner, CurrentPlayers: 12, MaxPlayers: 100, Name: "Beginner 01"),
            new(1, ChannelType.Free, CurrentPlayers: 45, MaxPlayers: 100, Name: "Free 01"),
            new(2, ChannelType.Clan, CurrentPlayers: 5, MaxPlayers: 100, Name: "Clan 01")
        };

        var packet = new ChannelListResponsePacket(channels);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelList, packet.Opcode);
        bool success = ChannelListResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.HasCount(3, parsed.Channels);

        Assert.AreEqual((byte)0, parsed.Channels[0].Index);
        Assert.AreEqual(ChannelType.Beginner, parsed.Channels[0].Type);
        Assert.AreEqual((byte)12, parsed.Channels[0].CurrentPlayers);
        Assert.AreEqual((byte)100, parsed.Channels[0].MaxPlayers);
        Assert.AreEqual("Beginner 01", parsed.Channels[0].Name);

        Assert.AreEqual((byte)1, parsed.Channels[1].Index);
        Assert.AreEqual(ChannelType.Free, parsed.Channels[1].Type);
        Assert.AreEqual("Free 01", parsed.Channels[1].Name);

        Assert.AreEqual((byte)2, parsed.Channels[2].Index);
        Assert.AreEqual(ChannelType.Clan, parsed.Channels[2].Type);
        Assert.AreEqual("Clan 01", parsed.Channels[2].Name);
    }

    [TestMethod]
    public void ChannelEnterRequestPacket_SerializesAndParsesCorrectly()
    {
        var packet = new ChannelEnterRequestPacket(ChannelIndex: 1, Password: "secretPassword");
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelEnter, packet.Opcode);
        bool success = ChannelEnterRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual((byte)1, parsed.ChannelIndex);
        Assert.AreEqual("secretPassword", parsed.Password);
    }

    [TestMethod]
    public void ChannelEnterResponsePacket_Success_SerializesAndParsesCorrectly()
    {
        var packet = new ChannelEnterResponsePacket(
            ResultCode: 0,
            ChannelIndex: 1,
            ServerId: 0,
            ChannelName: "Free 01",
            ChannelType: 1,
            MaxPlayers: 100,
            Flags: 0);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelEnter, packet.Opcode);
        bool success = ChannelEnterResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual((byte)0, parsed.ResultCode);
        Assert.AreEqual((byte)1, parsed.ChannelIndex);
        Assert.AreEqual((ushort)0, parsed.ServerId);
        Assert.AreEqual("Free 01", parsed.ChannelName);
        Assert.AreEqual((byte)1, parsed.ChannelType);
        Assert.AreEqual((byte)100, parsed.MaxPlayers);
        Assert.AreEqual(0u, parsed.Flags);
    }

    [TestMethod]
    public void ChannelEnterResponsePacket_Failure_SerializesAndParsesCorrectly()
    {
        var packet = new ChannelEnterResponsePacket(ResultCode: 2);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelEnter, packet.Opcode);
        bool success = ChannelEnterResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed);
        Assert.IsTrue(success);
        Assert.AreEqual((byte)2, parsed.ResultCode);
    }

    [TestMethod]
    public void ChannelExitPackets_SerializeAndParseCorrectly()
    {
        var req = new ChannelExitRequestPacket();
        using var reqWriter = new PacketWriter(req.Opcode);
        req.Serialize(reqWriter);
        byte[] reqBytes = reqWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelExit, req.Opcode);
        Assert.IsTrue(ChannelExitRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out _));

        var resp = new ChannelExitResponsePacket(ResultCode: 0);
        using var respWriter = new PacketWriter(resp.Opcode);
        resp.Serialize(respWriter);
        byte[] respBytes = respWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelExit, resp.Opcode);
        Assert.IsTrue(ChannelExitResponsePacket.TryParse(respBytes.AsSpan(PacketHeader.HeaderSize), out var parsedResp));
        Assert.AreEqual((byte)0, parsedResp.ResultCode);
    }

    [TestMethod]
    public void ChannelChatPackets_SerializeAndParseCorrectly()
    {
        var req = new ChannelChatRequestPacket("Hello everyone in channel!");
        using var reqWriter = new PacketWriter(req.Opcode);
        req.Serialize(reqWriter);
        byte[] reqBytes = reqWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelChat, req.Opcode);
        Assert.IsTrue(ChannelChatRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var parsedReq));
        Assert.AreEqual("Hello everyone in channel!", parsedReq.Message);

        var broadcast = new ChannelChatBroadcastPacket(ChatType: 0, FormattedMessage: "Warrior01 : Hello everyone in channel!");
        using var bWriter = new PacketWriter(broadcast.Opcode);
        broadcast.Serialize(bWriter);
        byte[] bBytes = bWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelChat, broadcast.Opcode);
        Assert.IsTrue(ChannelChatBroadcastPacket.TryParse(bBytes.AsSpan(PacketHeader.HeaderSize), out var parsedB));
        Assert.AreEqual((byte)0, parsedB.ChatType);
        Assert.AreEqual("Warrior01 : Hello everyone in channel!", parsedB.FormattedMessage);
    }

    [TestMethod]
    public void ChannelCharactersPackets_SerializeAndParseCorrectly()
    {
        var req = new ChannelCharactersRequestPacket();
        using var reqWriter = new PacketWriter(req.Opcode);
        req.Serialize(reqWriter);
        byte[] reqBytes = reqWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelCharacters, req.Opcode);
        Assert.IsTrue(ChannelCharactersRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out _));

        var chars = new List<ChannelCharacterSummary>
        {
            new(Slot: 0, Grade: 15, Name: "HeroOne", Class: 0, Level: 15, ClanId: 100),
            new(Slot: 1, Grade: 25, Name: "MageTwo", Class: 3, Level: 25, ClanId: 0)
        };

        var resp = new ChannelCharactersResponsePacket(
            ChannelIndex: 1,
            CharacterCount: 2,
            PageIndex: 0,
            ChannelName: "Free 01",
            ServerName: "WorldServer 1",
            Characters: chars);

        using var respWriter = new PacketWriter(resp.Opcode);
        resp.Serialize(respWriter);
        byte[] respBytes = respWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.ChannelCharacters, resp.Opcode);
        Assert.IsTrue(ChannelCharactersResponsePacket.TryParse(respBytes.AsSpan(PacketHeader.HeaderSize), out var parsedResp));
        Assert.AreEqual((byte)1, parsedResp.ChannelIndex);
        Assert.AreEqual((byte)2, parsedResp.CharacterCount);
        Assert.AreEqual("Free 01", parsedResp.ChannelName);
        Assert.AreEqual("WorldServer 1", parsedResp.ServerName);
        Assert.HasCount(2, parsedResp.Characters);

        Assert.AreEqual("HeroOne", parsedResp.Characters[0].Name);
        Assert.AreEqual((byte)0, parsedResp.Characters[0].Class);
        Assert.AreEqual((byte)15, parsedResp.Characters[0].Level);
        Assert.AreEqual(100u, parsedResp.Characters[0].ClanId);

        Assert.AreEqual("MageTwo", parsedResp.Characters[1].Name);
        Assert.AreEqual((byte)3, parsedResp.Characters[1].Class);
        Assert.AreEqual((byte)25, parsedResp.Characters[1].Level);
    }

    [TestMethod]
    public void CharacterWhisperPackets_SerializeAndParseCorrectly()
    {
        var req = new CharacterWhisperRequestPacket(TargetCharacterName: "FriendPlayer", Message: "Hey, want to party up?");
        using var reqWriter = new PacketWriter(req.Opcode);
        req.Serialize(reqWriter);
        byte[] reqBytes = reqWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.CharacterWhisper, req.Opcode);
        Assert.IsTrue(CharacterWhisperRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var parsedReq));
        Assert.AreEqual("FriendPlayer", parsedReq.TargetCharacterName);
        Assert.AreEqual("Hey, want to party up?", parsedReq.Message);

        // Success response
        var successResp = new CharacterWhisperResponsePacket(ResultCode: 0, CharacterName: "SenderHero", Message: "Hey, want to party up?");
        using var sWriter = new PacketWriter(successResp.Opcode);
        successResp.Serialize(sWriter);
        byte[] sBytes = sWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.CharacterWhisper, successResp.Opcode);
        Assert.IsTrue(CharacterWhisperResponsePacket.TryParse(sBytes.AsSpan(PacketHeader.HeaderSize), out var parsedS));
        Assert.AreEqual((byte)0, parsedS.ResultCode);
        Assert.AreEqual("SenderHero", parsedS.CharacterName);
        Assert.AreEqual("Hey, want to party up?", parsedS.Message);

        // Error response (target offline)
        var errorResp = new CharacterWhisperResponsePacket(ResultCode: 1, CharacterName: "OfflinePlayer", Message: string.Empty);
        using var eWriter = new PacketWriter(errorResp.Opcode);
        errorResp.Serialize(eWriter);
        byte[] eBytes = eWriter.Build();

        Assert.AreEqual((ushort)RakionOpcode.CharacterWhisper, errorResp.Opcode);
        Assert.IsTrue(CharacterWhisperResponsePacket.TryParse(eBytes.AsSpan(PacketHeader.HeaderSize), out var parsedE));
        Assert.AreEqual((byte)1, parsedE.ResultCode);
        Assert.AreEqual("OfflinePlayer", parsedE.CharacterName);
    }
}
