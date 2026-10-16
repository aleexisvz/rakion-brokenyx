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
        payload[0] = 0x00;
        payload[1] = 0x44;
        payload[2] = 0x00;
        int pos = 3;

        byte[] pathBytes = System.Text.Encoding.ASCII.GetBytes("C:\\Program Files (x86)\\Softnyx\\Rakion\\Bin\\Rakion.bin\0");
        pathBytes.CopyTo(payload, pos);
        pos += pathBytes.Length;

        byte[] userBytes = System.Text.Encoding.ASCII.GetBytes("admin\0");
        userBytes.CopyTo(payload, pos);
        pos += userBytes.Length;

        byte[] passBytes = System.Text.Encoding.ASCII.GetBytes("admin\0");
        passBytes.CopyTo(payload, pos);
        pos += passBytes.Length;

        Assert.AreEqual(68, pos);
        Assert.HasCount(68, payload);

        bool success = WorldLoginRequestPacket.TryParse(payload, out var packet);
        Assert.IsTrue(success);
        Assert.AreEqual("admin", packet.Username);
        Assert.AreEqual("admin", packet.Password);
        Assert.AreEqual("C:\\Program Files (x86)\\Softnyx\\Rakion\\Bin\\Rakion.bin", packet.ClientPath);
    }

    [TestMethod]
    public void WorldLoginRequestPacket_CleanRetailSendLogin_ParsesUserAndPassword()
    {
        // Retail IScavengerWorldNet::SendLogin (0x36190AD0):
        // [0]=Language(0), [1..2]=SubPacketLength(27), [3..]="admin\0", "secret\0", <8-byte IP MD5 + \0>, IdcIndex(1)
        byte[] payload =
        [
            0x00, 0x1B, 0x00,
            (byte)'a', (byte)'d', (byte)'m', (byte)'i', (byte)'n', 0x00,
            (byte)'s', (byte)'e', (byte)'c', (byte)'r', (byte)'e', (byte)'t', 0x00,
            0x30, 0x61, 0xA6, 0x86, 0x24, 0x0F, 0x74, 0xE7, 0x00,
            0x01, 0x00
        ];

        bool success = WorldLoginRequestPacket.TryParse(payload, out var packet);
        Assert.IsTrue(success);
        Assert.AreEqual("admin", packet.Username);
        Assert.AreEqual("secret", packet.Password);
    }

    [TestMethod]
    public void WorldLoginRequestPacket_LiveRetailSendLogin20Bytes_ParsesCorrectly()
    {
        // Live client captured bytes: 00440061646D696E0061646D696E0090F969E665
        byte[] payload =
        [
            0x00, 0x44, 0x00,
            (byte)'a', (byte)'d', (byte)'m', (byte)'i', (byte)'n', 0x00,
            (byte)'a', (byte)'d', (byte)'m', (byte)'i', (byte)'n', 0x00,
            0x90, 0xF9, 0x69, 0xE6, 0x65
        ];

        bool success = WorldLoginRequestPacket.TryParse(payload, out var packet);
        Assert.IsTrue(success);
        Assert.AreEqual(0u, packet.SessionTicket);
        Assert.AreEqual("admin", packet.Username);
        Assert.AreEqual("admin", packet.Password);
    }

    [TestMethod]
    public void WorldLoginPart2Packet_DefaultSlots_SerializesExact1326Bytes()
    {
        var packet = new WorldLoginPart2Packet();
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes.AsSpan(0, PacketHeader.HeaderSize), out var header));
        Assert.AreEqual((ushort)RakionOpcode.WorldLoginPart2, header.Opcode);

        ReadOnlySpan<byte> payload = bytes.AsSpan(PacketHeader.HeaderSize);
        Assert.AreEqual(WorldLoginPart2Packet.BasePayloadSize, payload.Length);
        Assert.AreEqual((byte)0, payload[0]); // WordCount
        Assert.AreEqual((byte)0, payload[1]); // Flag

        bool parsed = WorldLoginPart2Packet.TryParse(payload, out var roundtrip);
        Assert.IsTrue(parsed);
        Assert.IsNotNull(roundtrip);
    }

    [TestMethod]
    public void UdpSuccessPacket_SerializesAndParsesCorrectly()
    {
        var packet = new UdpSuccessPacket(
            Result: 0,
            Ip1: 0x0100007F,
            Port1: 40708,
            Ip2: 0x0100007F,
            Port2: 40709);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes.AsSpan(0, PacketHeader.HeaderSize), out var header));
        Assert.AreEqual((ushort)RakionOpcode.UdpSuccess, header.Opcode);

        ReadOnlySpan<byte> payload = bytes.AsSpan(PacketHeader.HeaderSize);
        Assert.AreEqual(UdpSuccessPacket.PayloadSize, payload.Length);
        Assert.AreEqual((byte)0, payload[0]); // Result == 0 (Success)

        bool parsed = UdpSuccessPacket.TryParse(payload, out var roundtrip);
        Assert.IsTrue(parsed);
        Assert.AreEqual((byte)0, roundtrip.Result);
        Assert.AreEqual(0x0100007Fu, roundtrip.Ip1);
        Assert.AreEqual((ushort)40708, roundtrip.Port1);
        Assert.AreEqual(0x0100007Fu, roundtrip.Ip2);
        Assert.AreEqual((ushort)40709, roundtrip.Port2);
    }

    [TestMethod]
    public void WorldLoginResponsePacket_CharacterFieldOrder_MatchesEngineDllDisassembly()
    {
        // Verified against engine.dll 0x361961C9..0x36196225:
        // Draw (4 bytes) -> Flag (1 byte) -> Level (1 byte) -> Slot (1 byte) -> Rank (4 bytes) -> Hit1 (2 bytes)...
        const byte expectedLevel = 42;
        const byte expectedSlot = 0;
        const uint expectedDraw = 7;
        const uint expectedExp = 12345;
        const uint expectedWin = 99;
        const uint expectedLose = 11;
        const byte expectedClass = (byte)CharacterClass.Ninja;
        const uint expectedLevelPoint = 3;

        var character = new CharacterSummary(
            Id: 999,
            Name: "T", // "T\0" = 2 bytes
            Slot: expectedSlot,
            Class: CharacterClass.Ninja,
            Level: expectedLevel,
            Exp: expectedExp,
            LevelPoint: expectedLevelPoint,
            Win: expectedWin,
            Lose: expectedLose,
            Draw: expectedDraw);

        var packet = new WorldLoginResponsePacket(
            ResultCode: 0,
            UserId: 1,
            Gold: 100,
            Cash: 50,
            UnlockedSlots: 1,
            Characters: [character]);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        var reader = new PacketReader(bytes.AsSpan(PacketHeader.HeaderSize));
        byte res = reader.ReadByte();
        Assert.AreEqual((byte)0, res);
        uint uId = reader.ReadUInt32();
        Assert.AreEqual(1u, uId);
        reader.Skip(2); // country
        uint gold = reader.ReadUInt32();
        Assert.AreEqual(100u, gold);
        uint cash = reader.ReadUInt32();
        Assert.AreEqual(50u, cash);
        reader.Skip(4); // rankGrade
        reader.ReadNullTerminatedString(); // Username

        // Auxiliary blocks matching RakionWorldServ
        reader.Skip(4 + 2 + 4 + 4 + 4);
        reader.ReadNullTerminatedString();
        reader.ReadNullTerminatedString();
        reader.Skip(4 + 2);
        reader.ReadNullTerminatedString();
        reader.ReadNullTerminatedString();
        reader.Skip(1 + 1 + 2 + 4 + 4);
        byte slots = reader.ReadByte();
        Assert.AreEqual((byte)1, slots);
        byte extra = reader.ReadByte();

        // Slot 0 Character Record
        uint id = reader.ReadUInt32();
        Assert.AreEqual(999u, id);

        string name = reader.ReadNullTerminatedString();
        Assert.AreEqual("T", name);

        uint exp = reader.ReadUInt32();
        Assert.AreEqual(expectedExp, exp);

        uint win = reader.ReadUInt32();
        Assert.AreEqual(expectedWin, win);

        uint lose = reader.ReadUInt32();
        Assert.AreEqual(expectedLose, lose);

        byte classId = reader.ReadByte();
        Assert.AreEqual(expectedClass, classId);

        uint lp = reader.ReadUInt32();
        Assert.AreEqual(expectedLevelPoint, lp);

        uint draw = reader.ReadUInt32();
        Assert.AreEqual(expectedDraw, draw);

        // CRITICAL CHECK: In engine.dll 0x361961D9:
        // Offset immediately after Draw is Flag (1 byte), followed by Level (1 byte), followed by Slot (1 byte), followed by Rank (4 bytes)
        byte flag = reader.ReadByte();
        Assert.AreEqual((byte)0, flag, "Flag must be 0 immediately after Draw (engine.dll 0x361961D9)");

        byte level = reader.ReadByte();
        Assert.AreEqual(expectedLevel, level, "Level must follow Flag (engine.dll 0x361961FC)");

        byte slot = reader.ReadByte();
        Assert.AreEqual(expectedSlot, slot, "Slot must follow Level (engine.dll 0x3619620A)");

        uint rank = reader.ReadUInt32();
        Assert.AreEqual(0u, rank, "Rank must follow Slot (engine.dll 0x3619621B)");
    }
}


