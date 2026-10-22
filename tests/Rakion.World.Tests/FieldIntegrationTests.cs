using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rakion.Broker.Services;
using Rakion.Core.Cryptography;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.World.Channels;
using Rakion.World.Configuration;
using Rakion.World.Handlers;
using Rakion.World.Hosting;

namespace Rakion.World.Tests;

[TestClass]
public sealed class FieldIntegrationTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private UserRepository _userRepository = null!;
    private CharacterRepository _characterRepository = null!;
    private AccountBalanceRepository _balanceRepository = null!;
    private SessionTicketService _ticketService = null!;
    private IOptions<WorldOptions> _options = null!;
    private ChannelManager _channelManager = null!;
    private WorldPacketHandler _handler = null!;
    private WorldServer _server = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_field_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _userRepository = new UserRepository(_dbFactory);
        _characterRepository = new CharacterRepository(_dbFactory);
        _balanceRepository = new AccountBalanceRepository(_dbFactory);
        _ticketService = new SessionTicketService();

        _options = Options.Create(new WorldOptions
        {
            ListenIp = "127.0.0.1",
            Port = 0,
            UdpPort1 = 0,
            UdpPort2 = 0,
            ServerId = 1,
            ServerName = "TestWorld",
            Channels = new List<ChannelConfig>
            {
                new() { Index = 0, Name = "Beginner 01", Type = ChannelType.Beginner, MaxLevel = 15, MaxPlayers = 100 },
                new() { Index = 1, Name = "Free 01", Type = ChannelType.Free, MaxLevel = 99, MaxPlayers = 100 }
            }
        });

        _channelManager = new ChannelManager(_options, NullLogger<ChannelManager>.Instance);

        _handler = new WorldPacketHandler(
            _options,
            _userRepository,
            _characterRepository,
            _balanceRepository,
            _ticketService,
            _channelManager,
            NullLogger<WorldPacketHandler>.Instance);

        var udpHandler = new WorldUdpPacketHandler(NullLogger<WorldUdpPacketHandler>.Instance);
        _server = new WorldServer(_options, _handler, udpHandler, NullLogger<WorldServer>.Instance);
        _server.Start();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _server.StopAsync();
        _dbFactory.Dispose();
    }

    private async Task<(TcpClient Client, NetworkStream Stream)> CreateAuthenticatedPlayerAsync(string username, string characterName, byte channelIndex)
    {
        int port = ((System.Net.IPEndPoint)_server.LocalEndPoint!).Port;

        int userId = await _userRepository.CreateUserAsync(new UserEntity
        {
            Username = username,
            PasswordHash = "hash",
            Email = $"{username}@test.com"
        });

        var charEntity = await _characterRepository.CreateCharacterAsync(userId, characterName, CharacterClass.Swordman, slotIndex: 0);
        int charId = charEntity.Id;

        _ticketService.IssueTicket(username, "127.0.0.1");

        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // 1. WorldLogin
        var loginReq = new WorldLoginRequestPacket(SessionTicket: 0, Username: username, ClientPath: "Rakion.exe");
        await SendClientPacketAsync(stream, 1, loginReq.Opcode, loginReq);
        await ReadServerPacketAsync(stream); // WorldLoginResponse
        await ReadServerPacketAsync(stream); // WorldLoginPart2

        // 2. Select Character (auto-enters channel 0 and receives 0x0014, 0x001F, 0x001E)
        var selectReq = new CharacterSelectRequestPacket((uint)charId);
        await SendClientPacketAsync(stream, 2, selectReq.Opcode, selectReq);
        await ReadServerPacketAsync(stream); // CharacterSelectResponse (0x0014)
        await ReadServerPacketAsync(stream); // ChannelEnterResponse (0x001F)
        await ReadServerPacketAsync(stream); // ChannelCharactersResponse (0x001E)

        return (client, stream);
    }

    [TestMethod]
    public async Task FieldLifecycle_Create_List_Join_Ready_Chat_Exit_WorksEndToEnd()
    {
        // Player 1 (Host)
        var (c1, s1) = await CreateAuthenticatedPlayerAsync("hostUser", "HostSword", 0);

        // 1. Initial FieldList should be empty
        var listReq = new FieldListRequestPacket(PageSize: 10, PageIndex: 0);
        await SendClientPacketAsync(s1, 3, listReq.Opcode, listReq);
        var (opList1, payloadList1) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.FieldList, opList1);
        Assert.IsTrue(FieldListResponsePacket.TryParse(payloadList1, out var pList1));
        Assert.IsEmpty(pList1.Fields);

        // 2. Host creates room (Opcode 0x003B)
        var createReq = new FieldCreateRequestPacket(
            RoomName: "Battleground 1v1",
            Password: string.Empty,
            MapName: "Underground",
            Mode: 1,
            MaxPlayers: 8,
            RoundTime: 5,
            Rounds: 3,
            SubMode: 0,
            LevelLimit: 0,
            Extra1: 0,
            Extra2: 0);
        await SendClientPacketAsync(s1, 4, createReq.Opcode, createReq);
        var (opCreate, payloadCreate) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.FieldCreate, opCreate);
        Assert.AreEqual(0x003B, (ushort)opCreate);
        Assert.IsTrue(FieldCreateResponsePacket.TryParse(payloadCreate, out var pCreate));
        Assert.AreEqual((byte)0, pCreate.ResultCode);
        Assert.AreEqual((ushort)1, pCreate.RoomNumber);

        // 3. FieldList now shows 1 room with pagination
        await SendClientPacketAsync(s1, 5, listReq.Opcode, listReq);
        var (opList2, payloadList2) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.FieldList, opList2);
        Assert.IsTrue(FieldListResponsePacket.TryParse(payloadList2, out var pList2));
        Assert.HasCount(1, pList2.Fields);
        Assert.AreEqual("Battleground 1v1", pList2.Fields[0].RoomName);
        Assert.AreEqual((byte)1, pList2.Fields[0].CurrentPlayers);

        // 4. Player 2 joins via QuickEnter (0x0039)
        var (c2, s2) = await CreateAuthenticatedPlayerAsync("guestUser", "GuestNinja", 0);

        // Host (s1) was in channel 0, so it receives guest's ChannelEnter broadcast (0x001F)
        var (opBroadcastChan, _) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.ChannelEnter, opBroadcastChan);

        var quickReq = new FieldQuickEnterRequestPacket();
        await SendClientPacketAsync(s2, 3, quickReq.Opcode, quickReq);
        var (opQuick, payloadQuick) = await ReadServerPacketAsync(s2);
        Assert.AreEqual((ushort)ServerOpcode.FieldCreate, opQuick);
        Assert.IsTrue(FieldCreateResponsePacket.TryParse(payloadQuick, out var pQuickEnter));
        Assert.AreEqual((byte)0, pQuickEnter.ResultCode);
        Assert.AreEqual((ushort)1, pQuickEnter.RoomNumber);

        // 5. Guest toggles ready (0x003D)
        var readyReq = new FieldReadyRequestPacket(ReadyState: 1);
        await SendClientPacketAsync(s2, 4, readyReq.Opcode, readyReq);

        // Both host and guest receive ready broadcast (0x003D)
        var (opReadyHost, payloadReadyHost) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.FieldReady, opReadyHost);
        Assert.IsTrue(FieldReadyBroadcastPacket.TryParse(payloadReadyHost, out var bReadyHost));
        Assert.AreEqual((byte)1, bReadyHost.ReadyState);

        var (opReadyGuest, payloadReadyGuest) = await ReadServerPacketAsync(s2);
        Assert.AreEqual((ushort)ServerOpcode.FieldReady, opReadyGuest);

        // 6. Guest sends room chat (0x0047)
        var chatReq = new FieldChatRequestPacket("Good luck everyone!");
        await SendClientPacketAsync(s2, 5, chatReq.Opcode, chatReq);

        var (opChatHost, payloadChatHost) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.FieldChat, opChatHost);
        Assert.IsTrue(FieldChatBroadcastPacket.TryParse(payloadChatHost, out var bChatHost));
        Assert.IsTrue(bChatHost.Message.Contains("GuestNinja : Good luck everyone!", StringComparison.Ordinal));

        var (opChatGuest, payloadChatGuest) = await ReadServerPacketAsync(s2);
        Assert.AreEqual((ushort)ServerOpcode.FieldChat, opChatGuest);

        // 7. Guest exits room (0x003A)
        var exitReq = new FieldExitRequestPacket();
        await SendClientPacketAsync(s2, 6, exitReq.Opcode, exitReq);
        var (opExit, payloadExit) = await ReadServerPacketAsync(s2);
        Assert.AreEqual((ushort)ServerOpcode.FieldExit, opExit);
        Assert.IsTrue(FieldExitResponsePacket.TryParse(payloadExit, out var pExit));
        Assert.AreEqual((byte)0, pExit.ResultCode);

        // 8. Host exits room (room destroyed)
        await SendClientPacketAsync(s1, 6, exitReq.Opcode, exitReq);
        var (opHostExit, payloadHostExit) = await ReadServerPacketAsync(s1);
        Assert.AreEqual((ushort)ServerOpcode.FieldExit, opHostExit);

        // FieldList is empty again
        await SendClientPacketAsync(s1, 7, listReq.Opcode, listReq);
        var (opList3, payloadList3) = await ReadServerPacketAsync(s1);
        Assert.IsTrue(FieldListResponsePacket.TryParse(payloadList3, out var pList3));
        Assert.IsEmpty(pList3.Fields);

        c1.Dispose();
        c2.Dispose();
    }

    private static async Task SendClientPacketAsync(NetworkStream stream, ushort sequenceId, ushort opcode, IPacket packet)
    {
        using var writer = new PacketWriter();
        packet.Serialize(writer);
        byte[] enc = RakionWorldCrypto.EncryptClientPacket(sequenceId, opcode, writer.WrittenSpan);
        await stream.WriteAsync(enc);
        await stream.FlushAsync();
    }

    private static async Task<(ushort Opcode, byte[] Payload)> ReadServerPacketAsync(NetworkStream stream)
    {
        byte[] lenBuffer = new byte[2];
        await ReadExactAsync(stream, lenBuffer);
        ushort wireLen = BinaryPrimitives.ReadUInt16LittleEndian(lenBuffer);

        byte[] cipher = new byte[wireLen - 2];
        await ReadExactAsync(stream, cipher);

        byte[] plain = new byte[cipher.Length];
        bool ok = RakionWorldCrypto.TryDecrypt(cipher, plain, out int decLen);
        Assert.IsTrue(ok);

        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(0, 2));
        byte[] payload = plain.AsSpan(2, decLen - 2).ToArray();
        return (opcode, payload);
    }

    private static async Task ReadExactAsync(NetworkStream stream, Memory<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[totalRead..]);
            if (read == 0)
            {
                throw new EndOfStreamException("Socket closed prematurely.");
            }
            totalRead += read;
        }
    }
}
