using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class WorldPacketTests
{
    [TestMethod]
    public void GameGuardPacket_SerializesAndParsesCorrectly()
    {
        var packet = new GameGuardPacket(ResultCode: 0);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes.AsSpan(0, PacketHeader.HeaderSize), out var header));
        Assert.AreEqual((ushort)RakionOpcode.GameGuardResponse, header.Opcode);

        bool parsed = GameGuardPacket.TryParseResponse(bytes.AsSpan(PacketHeader.HeaderSize), out var response);
        Assert.IsTrue(parsed);
        Assert.AreEqual((byte)0, response.ResultCode);
    }

    [TestMethod]
    public void WorldLoginRequestPacket_SerializesAndParsesCorrectly()
    {
        var packet = new WorldLoginRequestPacket(12345678, "SwordMaster");
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes.AsSpan(0, PacketHeader.HeaderSize), out var header));
        Assert.AreEqual((ushort)RakionOpcode.WorldLogin, header.Opcode);

        bool parsed = WorldLoginRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var roundtrip);
        Assert.IsTrue(parsed);
        Assert.AreEqual(12345678u, roundtrip.SessionTicket);
        Assert.AreEqual("SwordMaster", roundtrip.Username);
    }

    [TestMethod]
    public void WorldLoginResponsePacket_WithCharacters_SerializesAndParsesCorrectly()
    {
        var chars = new List<CharacterSummary>
        {
            new(101, "NinjaGirl", 0, CharacterClass.Ninja, 15, 45000, 5,
                Hp: 150, Ap: 80, Speed: 130, AttackSpeed: 110, MaxCp: 120, Win: 25, Lose: 4, Draw: 1),
            new(102, "IronGolem", 1, CharacterClass.Blacksmith, 8, 12000, 2,
                Hp: 220, Ap: 160, Speed: 90, AttackSpeed: 85, MaxCp: 90, Win: 10, Lose: 8, Draw: 0)
        };

        var packet = new WorldLoginResponsePacket(0, UserId: 42, Gold: 50000, Cash: 1000, UnlockedSlots: 3, Characters: chars);
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes.AsSpan(0, PacketHeader.HeaderSize), out var header));
        Assert.AreEqual((ushort)RakionOpcode.WorldLogin, header.Opcode);

        bool parsed = WorldLoginResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var roundtrip);
        Assert.IsTrue(parsed);
        Assert.AreEqual((byte)0, roundtrip.ResultCode);
        Assert.AreEqual(42u, roundtrip.UserId);
        Assert.AreEqual(50000u, roundtrip.Gold);
        Assert.AreEqual(1000u, roundtrip.Cash);
        Assert.IsNotNull(roundtrip.Characters);
        Assert.HasCount(2, roundtrip.Characters);

        var c0 = roundtrip.Characters[0];
        Assert.AreEqual("NinjaGirl", c0.Name);
        Assert.AreEqual(CharacterClass.Ninja, c0.Class);
        Assert.AreEqual((byte)15, c0.Level);
        Assert.AreEqual(130, c0.Speed);

        var c1 = roundtrip.Characters[1];
        Assert.AreEqual("IronGolem", c1.Name);
        Assert.AreEqual(CharacterClass.Blacksmith, c1.Class);
    }

    [TestMethod]
    public void CharacterCreatePackets_RoundtripSerialization()
    {
        var request = new CharacterCreateRequestPacket("MageHero", CharacterClass.Mage, Slot: 2);
        using var reqWriter = new PacketWriter(request.Opcode);
        request.Serialize(reqWriter);
        byte[] reqBytes = reqWriter.Build();

        bool reqParsed = CharacterCreateRequestPacket.TryParse(reqBytes.AsSpan(PacketHeader.HeaderSize), out var roundtripReq);
        Assert.IsTrue(reqParsed);
        Assert.AreEqual("MageHero", roundtripReq.Name);
        Assert.AreEqual(CharacterClass.Mage, roundtripReq.Class);
        Assert.AreEqual((byte)2, roundtripReq.Slot);

        var summary = new CharacterSummary(201, "MageHero", 2, CharacterClass.Mage, 1, 0, 0);
        var response = new CharacterCreateResponsePacket(0, summary);
        using var resWriter = new PacketWriter(response.Opcode);
        response.Serialize(resWriter);
        byte[] resBytes = resWriter.Build();

        bool resParsed = CharacterCreateResponsePacket.TryParse(resBytes.AsSpan(PacketHeader.HeaderSize), out var roundtripRes);
        Assert.IsTrue(resParsed);
        Assert.AreEqual((byte)0, roundtripRes.ResultCode);
        Assert.IsNotNull(roundtripRes.Character);
        Assert.AreEqual(201u, roundtripRes.Character.Id);
        Assert.AreEqual("MageHero", roundtripRes.Character.Name);
    }

    [TestMethod]
    public void CharacterDeleteAndSelectPackets_RoundtripSerialization()
    {
        // Delete
        var delReq = new CharacterDeleteRequestPacket(555, "mypass123");
        using var delWriter = new PacketWriter(delReq.Opcode);
        delReq.Serialize(delWriter);
        byte[] delBytes = delWriter.Build();

        Assert.IsTrue(CharacterDeleteRequestPacket.TryParse(delBytes.AsSpan(PacketHeader.HeaderSize), out var roundtripDel));
        Assert.AreEqual(555u, roundtripDel.CharacterId);
        Assert.AreEqual("mypass123", roundtripDel.DeleteKey);

        var delRes = new CharacterDeleteResponsePacket(0, 555);
        using var delResWriter = new PacketWriter(delRes.Opcode);
        delRes.Serialize(delResWriter);
        byte[] delResBytes = delResWriter.Build();

        Assert.IsTrue(CharacterDeleteResponsePacket.TryParse(delResBytes.AsSpan(PacketHeader.HeaderSize), out var roundtripDelRes));
        Assert.AreEqual((byte)0, roundtripDelRes.ResultCode);
        Assert.AreEqual(555u, roundtripDelRes.CharacterId);

        // Select
        var selReq = new CharacterSelectRequestPacket(777);
        using var selWriter = new PacketWriter(selReq.Opcode);
        selReq.Serialize(selWriter);
        byte[] selBytes = selWriter.Build();

        Assert.IsTrue(CharacterSelectRequestPacket.TryParse(selBytes.AsSpan(PacketHeader.HeaderSize), out var roundtripSel));
        Assert.AreEqual(777u, roundtripSel.CharacterId);

        var selRes = new CharacterSelectResponsePacket(0, 777);
        using var selResWriter = new PacketWriter(selRes.Opcode);
        selRes.Serialize(selResWriter);
        byte[] selResBytes = selResWriter.Build();

        Assert.IsTrue(CharacterSelectResponsePacket.TryParse(selResBytes.AsSpan(PacketHeader.HeaderSize), out var roundtripSelRes));
        Assert.AreEqual((byte)0, roundtripSelRes.ResultCode);
        Assert.AreEqual(777u, roundtripSelRes.CharacterId);
    }

    [TestMethod]
    public void WorldLoginRequestPacket_RetailClientPayload_ParsesSuccessfully()
    {
        // 68 bytes exactly matching retail v258 client SendLogin(user, pass, country, clientPath, flag)
        byte[] payload = new byte[68];
        payload[0] = 0; // flag
        int pos = 1;

        byte[] pathBytes = System.Text.Encoding.ASCII.GetBytes("C:\\Program Files (x86)\\Softnyx\\Rakion\\Bin\\Rakion.bin\0");
        pathBytes.CopyTo(payload, pos);
        pos += pathBytes.Length;

        byte[] userBytes = System.Text.Encoding.ASCII.GetBytes("admin\0");
        userBytes.CopyTo(payload, pos);
        pos += userBytes.Length;

        byte[] passBytes = System.Text.Encoding.ASCII.GetBytes("admin\0");
        passBytes.CopyTo(payload, pos);
        pos += passBytes.Length;

        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(pos, 2), 0);
        pos += 2;

        Assert.AreEqual(68, pos);
        Assert.HasCount(68, payload);

        bool success = WorldLoginRequestPacket.TryParse(payload, out var packet);
        Assert.IsTrue(success);
        Assert.AreEqual("admin", packet.Username);
        Assert.AreEqual("admin", packet.Password);
        Assert.AreEqual("C:\\Program Files (x86)\\Softnyx\\Rakion\\Bin\\Rakion.bin", packet.ClientPath);
        Assert.AreEqual((ushort)0, packet.Country);
    }
}

