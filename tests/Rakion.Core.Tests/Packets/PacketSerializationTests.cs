using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class PacketSerializationTests
{
    [TestMethod]
    public void ServerListRequest_Roundtrip_SerializesAndParsesCorrectly()
    {
        var request = new ServerListRequestPacket(ClientVersion: 1026);
        using var writer = new PacketWriter(request.Opcode);
        request.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes, out var header));
        Assert.AreEqual((ushort)RakionOpcode.ServerListRequest, header.Opcode);

        Assert.IsTrue(ServerListRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual((ushort)1026, parsed.ClientVersion);
    }

    [TestMethod]
    public void ServerListResponse_Roundtrip_SerializesAndParsesServers()
    {
        var servers = new List<GameServerEntry>
        {
            new(1, "WorldServer1", IPAddress.Parse("192.168.1.5"), 40708, UsedSalas: 5, MaxSalas: 2000, UsedSlots: 42, MaxSlots: 1000, IsOnline: true),
            new(2, "WorldServer2", IPAddress.Parse("192.168.1.6"), 40708, UsedSalas: 0, MaxSalas: 2000, UsedSlots: 0, MaxSlots: 1000, IsOnline: false)
        };

        var response = new ServerListResponsePacket(servers);
        using var writer = new PacketWriter(response.Opcode);
        response.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes, out var header));
        Assert.AreEqual((ushort)RakionOpcode.ServerListResponse, header.Opcode);

        Assert.IsTrue(ServerListResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.HasCount(2, parsed.Servers);

        var s1 = parsed.Servers[0];
        Assert.AreEqual(IPAddress.Parse("192.168.1.5"), s1.IpAddress);
        Assert.AreEqual((ushort)40708, s1.Port);
        Assert.AreEqual((ushort)5, s1.UsedSalas);
        Assert.AreEqual((ushort)2000, s1.MaxSalas);
        Assert.AreEqual((ushort)42, s1.UsedSlots);
        Assert.AreEqual((ushort)1000, s1.MaxSlots);
        Assert.IsTrue(s1.IsOnline);

        var s2 = parsed.Servers[1];
        Assert.IsFalse(s2.IsOnline);
    }

    [TestMethod]
    public void LoginRequest_Roundtrip_ParsesDirectAndPrefixedOpcodes()
    {
        var login = new LoginRequestPacket("TestPlayer", "SecretPass123", 1026, "127.0.0.1", 1);
        using var writer = new PacketWriter(login.Opcode);
        login.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.IsTrue(PacketHeader.TryParse(bytes, out var header));
        Assert.AreEqual((ushort)RakionOpcode.Login, header.Opcode);

        Assert.IsTrue(LoginRequestPacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), header.Opcode, out var parsed));
        Assert.AreEqual("TestPlayer", parsed.Username);
        Assert.AreEqual("SecretPass123", parsed.Password);
        Assert.AreEqual((ushort)1026, parsed.ClientVersion);
        Assert.AreEqual("127.0.0.1", parsed.ClientIp);
        Assert.AreEqual((byte)1, parsed.Flag);
    }

    [TestMethod]
    public void LoginResponse_GoldenTest_MatchesObservedBrokenServerBytes()
    {
        // 1. Standard 0x1310 response: 08-00-EB-CB-12-13-00-00
        var resp1 = LoginResponsePacket.Create(0x1310, LoginResultCode.Success);
        using var w1 = new PacketWriter(resp1.HeaderOpcode);
        resp1.Serialize(w1);
        byte[] b1 = w1.Build();

        byte[] expected1 = [0x08, 0x00, 0xEB, 0xCB, 0x12, 0x13, 0x00, 0x00];
        CollectionAssert.AreEqual(expected1, b1);

        // 2. Alt2 0x3000 response: 08-00-D3-EB-01-30-00-00
        var resp2 = LoginResponsePacket.Create(0x3000, LoginResultCode.Success);
        using var w2 = new PacketWriter(resp2.HeaderOpcode);
        resp2.Serialize(w2);
        byte[] b2 = w2.Build();

        byte[] expected2 = [0x08, 0x00, 0xD3, 0xEB, 0x01, 0x30, 0x00, 0x00];
        CollectionAssert.AreEqual(expected2, b2);

        // 3. Alt4 0x4000 response: 0C-00-AF-1B-01-40-00-00-1C-00-00-00 (with ticket = 28 / 0x1C)
        var resp3 = LoginResponsePacket.Create(0x4000, LoginResultCode.Success, sessionTicket: 28);
        using var w3 = new PacketWriter(resp3.HeaderOpcode);
        resp3.Serialize(w3);
        byte[] b3 = w3.Build();

        byte[] expected3 = [0x0C, 0x00, 0xAF, 0x1B, 0x01, 0x40, 0x00, 0x00, 0x1C, 0x00, 0x00, 0x00];
        CollectionAssert.AreEqual(expected3, b3);
    }
}
