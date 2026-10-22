using System.Buffers;
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
using Rakion.Network.Tcp;
using Rakion.World.Channels;
using Rakion.World.Configuration;
using Rakion.World.Handlers;
using Rakion.World.Hosting;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class ChannelIntegrationTests
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
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_channel_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
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
                new() { Index = 1, Name = "Free 01", Type = ChannelType.Free, MaxLevel = 99, MaxPlayers = 100 },
                new() { Index = 2, Name = "Clan 01", Type = ChannelType.Clan, MaxLevel = 99, MaxPlayers = 100 }
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

    [TestMethod]
    public void ChannelManager_LevelRestrictionsAndCapacity_WorkCorrectly()
    {
        var dummyConn = new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), _handler, NullLogger.Instance);
        var session = new WorldSession(dummyConn)
        {
            User = new UserEntity { Id = 1, Username = "testuser" },
            ActiveCharacter = new CharacterEntity { Id = 1, UserId = 1, Name = "HighLevelHero", Level = 25 }
        };

        // 1. Beginner channel (MaxLevel 15) must reject level 25 character
        bool beginnerSuccess = _channelManager.TryEnterChannel(0, session, "", out byte reason);
        Assert.IsFalse(beginnerSuccess);
        Assert.AreEqual((byte)1, reason);
        Assert.IsNull(session.ActiveChannel);

        // 2. Free channel must allow level 25 character
        bool freeSuccess = _channelManager.TryEnterChannel(1, session, "", out reason);
        Assert.IsTrue(freeSuccess);
        Assert.AreEqual((byte)0, reason);
        Assert.IsNotNull(session.ActiveChannel);
        Assert.AreEqual("Free 01", session.ActiveChannel.Name);
        Assert.AreEqual(1, session.ActiveChannel.CurrentPlayers);

        // 3. Leaving channel
        _channelManager.LeaveChannel(session);
        Assert.IsNull(session.ActiveChannel);
    }

    [TestMethod]
    public async Task WorldServer_ChannelFlow_ListEnterExit_SucceedsOverTcp()
    {
        int port = ((System.Net.IPEndPoint)_server.LocalEndPoint!).Port;

        int userId = await _userRepository.CreateUserAsync(new UserEntity
        {
            Username = "channeluser",
            PasswordHash = "hash",
            Email = "c@test.com"
        });

        var charEntity = await _characterRepository.CreateCharacterAsync(userId, "ChannelHero", CharacterClass.Swordman, slotIndex: 0);
        int charId = charEntity.Id;

        _ticketService.IssueTicket("channeluser", "127.0.0.1");

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        using var stream = client.GetStream();

        // 1. Authenticate via WorldLogin (encrypted)
        var loginReq = new WorldLoginRequestPacket(SessionTicket: 0, Username: "channeluser", ClientPath: "Rakion.exe");
        await SendClientPacketAsync(stream, 1, loginReq.Opcode, loginReq);

        // Read WorldLoginResponse
        var (opcode1, payload1) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.WorldLogin, opcode1);

        // Read WorldLoginPart2
        var (opcode2, payload2) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.WorldLoginPart2, opcode2);

        // 2. Select Character
        var selectReq = new CharacterSelectRequestPacket((uint)charId);
        await SendClientPacketAsync(stream, 2, selectReq.Opcode, selectReq);

        var (selOp, selPayload) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.CharacterSelect, selOp);
        Assert.IsTrue(CharacterSelectResponsePacket.TryParse(selPayload, out var selResp));
        Assert.AreEqual((byte)0, selResp.ResultCode);

        // Character selection automatically initiates lobby entrance (Opcode 0x001F and 0x001E)
        var (autoEnterOp, autoEnterPayload) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelEnter, autoEnterOp);
        Assert.IsTrue(ChannelEnterResponsePacket.TryParse(autoEnterPayload, out var autoEnterResp));
        Assert.AreEqual((byte)0, autoEnterResp.ResultCode);

        var (autoCharsOp, _) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelCharacters, autoCharsOp);

        // 3. Request Channel List (0x001D)
        var listReq = new ChannelListRequestPacket();
        await SendClientPacketAsync(stream, 3, listReq.Opcode, listReq);

        var (listOp, listPayload) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelList, listOp);
        Assert.IsTrue(ChannelListResponsePacket.TryParse(listPayload, out var listResp));
        Assert.HasCount(3, listResp.Channels);
        Assert.AreEqual("Beginner 01", listResp.Channels[0].Name);
        Assert.AreEqual("Free 01", listResp.Channels[1].Name);
        Assert.AreEqual("Clan 01", listResp.Channels[2].Name);

        // 4. Enter Beginner Channel (0) with level 10 character -> succeeds
        var enterReq = new ChannelEnterRequestPacket(ChannelIndex: 0);
        await SendClientPacketAsync(stream, 4, enterReq.Opcode, enterReq);

        var (enterOp, enterPayload) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelEnter, enterOp);
        Assert.IsTrue(ChannelEnterResponsePacket.TryParse(enterPayload, out var enterResp));
        Assert.AreEqual((byte)0, enterResp.ResultCode);
        Assert.AreEqual((byte)0, enterResp.ChannelIndex);
        Assert.AreEqual("Beginner 01", enterResp.ChannelName);

        // Server delivers initial ChannelCharacters (0x001E) upon channel entrance
        var (autoEnterCharsOp, _) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelCharacters, autoEnterCharsOp);

        // 5. Request Channel Characters (0x001E) explicitly
        var charReq = new ChannelCharactersRequestPacket();
        await SendClientPacketAsync(stream, 5, charReq.Opcode, charReq);

        var (charsOp, charsPayload) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelCharacters, charsOp);
        Assert.IsTrue(ChannelCharactersResponsePacket.TryParse(charsPayload, out var charsResp));
        Assert.HasCount(1, charsResp.Characters);
        Assert.AreEqual("ChannelHero", charsResp.Characters[0].Name);

        // 6. Exit Channel (0x0020)
        var exitReq = new ChannelExitRequestPacket();
        await SendClientPacketAsync(stream, 6, exitReq.Opcode, exitReq);

        var (exitOp, exitPayload) = await ReadServerPacketAsync(stream);
        Assert.AreEqual((ushort)RakionOpcode.ChannelExit, exitOp);
        Assert.IsTrue(ChannelExitResponsePacket.TryParse(exitPayload, out var exitResp));
        Assert.AreEqual((byte)0, exitResp.ResultCode);
    }

    [TestMethod]
    public async Task WorldServer_ChannelChatAndWhisper_WorkOverTcp()
    {
        int port = ((System.Net.IPEndPoint)_server.LocalEndPoint!).Port;

        // Create Player 1
        int user1 = await _userRepository.CreateUserAsync(new UserEntity { Username = "user1", PasswordHash = "h", Email = "u1@test.com" });
        var ce1 = await _characterRepository.CreateCharacterAsync(user1, "Alice", CharacterClass.Archer, slotIndex: 0);
        int char1 = ce1.Id;
        _ticketService.IssueTicket("user1", "127.0.0.1");

        // Create Player 2
        int user2 = await _userRepository.CreateUserAsync(new UserEntity { Username = "user2", PasswordHash = "h", Email = "u2@test.com" });
        var ce2 = await _characterRepository.CreateCharacterAsync(user2, "Bob", CharacterClass.Swordman, slotIndex: 0);
        int char2 = ce2.Id;
        _ticketService.IssueTicket("user2", "127.0.0.1");

        using var client1 = new TcpClient();
        await client1.ConnectAsync("127.0.0.1", port);
        using var stream1 = client1.GetStream();

        using var client2 = new TcpClient();
        await client2.ConnectAsync("127.0.0.1", port);
        using var stream2 = client2.GetStream();

        // Login & select Alice
        await SendClientPacketAsync(stream1, 1, (ushort)RakionOpcode.WorldLogin, new WorldLoginRequestPacket(SessionTicket: 0, Username: "user1"));
        await ReadServerPacketAsync(stream1); // login resp
        await ReadServerPacketAsync(stream1); // login2
        await SendClientPacketAsync(stream1, 2, (ushort)RakionOpcode.CharacterSelect, new CharacterSelectRequestPacket((uint)char1));
        await ReadServerPacketAsync(stream1); // char select resp (0x0014)
        await ReadServerPacketAsync(stream1); // auto channel enter resp (0x001F)
        await ReadServerPacketAsync(stream1); // auto channel chars resp (0x001E)

        // Login & select Bob
        await SendClientPacketAsync(stream2, 1, (ushort)RakionOpcode.WorldLogin, new WorldLoginRequestPacket(SessionTicket: 0, Username: "user2"));
        await ReadServerPacketAsync(stream2); // login resp
        await ReadServerPacketAsync(stream2); // login2
        await SendClientPacketAsync(stream2, 2, (ushort)RakionOpcode.CharacterSelect, new CharacterSelectRequestPacket((uint)char2));
        await ReadServerPacketAsync(stream2); // char select resp (0x0014)
        await ReadServerPacketAsync(stream2); // auto channel enter resp (0x001F)
        await ReadServerPacketAsync(stream2); // auto channel chars resp (0x001E)

        // Alice receives broadcast of Bob entering channel 0
        await ReadServerPacketAsync(stream1);

        // Both enter Free Channel (1)
        await SendClientPacketAsync(stream1, 3, (ushort)RakionOpcode.ChannelEnter, new ChannelEnterRequestPacket(1));
        await ReadServerPacketAsync(stream1); // enter resp (0x001F)
        await ReadServerPacketAsync(stream1); // channel chars (0x001E)

        await SendClientPacketAsync(stream2, 3, (ushort)RakionOpcode.ChannelEnter, new ChannelEnterRequestPacket(1));
        await ReadServerPacketAsync(stream2); // enter resp (0x001F)
        await ReadServerPacketAsync(stream2); // channel chars (0x001E)

        // Alice receives broadcast of Bob entering Free Channel 1
        await ReadServerPacketAsync(stream1);

        // 1. Channel Chat: Alice sends chat message
        await SendClientPacketAsync(stream1, 4, (ushort)RakionOpcode.ChannelChat, new ChannelChatRequestPacket("Hello Bob!"));

        // Both Alice and Bob should receive broadcast
        var (chatOp1, chatPayload1) = await ReadServerPacketAsync(stream1);
        Assert.AreEqual((ushort)RakionOpcode.ChannelChat, chatOp1);
        Assert.IsTrue(ChannelChatBroadcastPacket.TryParse(chatPayload1, out var chatB1));
        Assert.AreEqual("Alice : Hello Bob!", chatB1.FormattedMessage);

        var (chatOp2, chatPayload2) = await ReadServerPacketAsync(stream2);
        Assert.AreEqual((ushort)RakionOpcode.ChannelChat, chatOp2);
        Assert.IsTrue(ChannelChatBroadcastPacket.TryParse(chatPayload2, out var chatB2));
        Assert.AreEqual("Alice : Hello Bob!", chatB2.FormattedMessage);

        // 2. Whisper: Bob whispers to Alice
        await SendClientPacketAsync(stream2, 4, (ushort)RakionOpcode.CharacterWhisper, new CharacterWhisperRequestPacket("Alice", "Can you hear me?"));

        // Alice receives incoming whisper
        var (wOpAlice, wPayloadAlice) = await ReadServerPacketAsync(stream1);
        Assert.AreEqual((ushort)RakionOpcode.CharacterWhisper, wOpAlice);
        Assert.IsTrue(CharacterWhisperResponsePacket.TryParse(wPayloadAlice, out var wRespAlice));
        Assert.AreEqual((byte)0, wRespAlice.ResultCode);
        Assert.AreEqual("Bob", wRespAlice.CharacterName);
        Assert.AreEqual("Can you hear me?", wRespAlice.Message);

        // Bob receives confirmation echo
        var (wOpBob, wPayloadBob) = await ReadServerPacketAsync(stream2);
        Assert.AreEqual((ushort)RakionOpcode.CharacterWhisper, wOpBob);
        Assert.IsTrue(CharacterWhisperResponsePacket.TryParse(wPayloadBob, out var wRespBob));
        Assert.AreEqual((byte)0, wRespBob.ResultCode);

        // 3. Whisper to offline player: Bob whispers to "Ghost"
        await SendClientPacketAsync(stream2, 5, (ushort)RakionOpcode.CharacterWhisper, new CharacterWhisperRequestPacket("Ghost", "Where are you?"));
        var (wOpFail, wPayloadFail) = await ReadServerPacketAsync(stream2);
        Assert.AreEqual((ushort)RakionOpcode.CharacterWhisper, wOpFail);
        Assert.IsTrue(CharacterWhisperResponsePacket.TryParse(wPayloadFail, out var wRespFail));
        Assert.AreEqual((byte)1, wRespFail.ResultCode);
    }

    [TestMethod]
    public async Task FieldList_ReturnsEmptyList_WhenNoRoomsExist()
    {
        int port = ((System.Net.IPEndPoint)_server.LocalEndPoint!).Port;

        int userId = await _userRepository.CreateUserAsync(new UserEntity
        {
            Username = "fielduser",
            PasswordHash = "hash",
            Email = "f@test.com"
        });

        var charEntity = await _characterRepository.CreateCharacterAsync(userId, "FieldHero", CharacterClass.Swordman, slotIndex: 0);
        int charId = charEntity.Id;

        _ticketService.IssueTicket("fielduser", "127.0.0.1");

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        using var stream = client.GetStream();

        // 1. Handshake login
        var loginReq = new WorldLoginRequestPacket(SessionTicket: 0, Username: "fielduser", ClientPath: "Rakion.exe");
        await SendClientPacketAsync(stream, 1, loginReq.Opcode, loginReq);
        await ReadServerPacketAsync(stream); // WorldLoginResponse
        await ReadServerPacketAsync(stream); // WorldLoginPart2

        // 2. Select character (auto-enters channel 0 and receives 0x0014, 0x001F, 0x001E)
        var selectReq = new CharacterSelectRequestPacket((uint)charId);
        await SendClientPacketAsync(stream, 2, selectReq.Opcode, selectReq);
        await ReadServerPacketAsync(stream); // CharacterSelectResponse (0x0014)
        await ReadServerPacketAsync(stream); // ChannelEnterResponse (0x001F)
        await ReadServerPacketAsync(stream); // ChannelCharactersResponse (0x001E)

        // 3. Client requests FieldList (Opcode 0x0036) upon entering the lobby
        await SendClientPacketAsync(stream, 3, (ushort)RakionOpcode.FieldList, new FieldListRequestPacket());
        var (fieldOp, fieldPayload) = await ReadServerPacketAsync(stream);

        Assert.AreEqual((ushort)RakionOpcode.FieldList, fieldOp);
        Assert.AreEqual(0x0036, (ushort)fieldOp);
        Assert.IsTrue(FieldListResponsePacket.TryParse(fieldPayload, out var fieldResp));
        Assert.IsEmpty(fieldResp.Fields);
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
