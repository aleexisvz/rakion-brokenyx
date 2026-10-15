using System.Buffers.Binary;
using System.Net;
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
using Rakion.World.Configuration;
using Rakion.World.Handlers;
using Rakion.World.Hosting;

namespace Rakion.World.Tests;

[TestClass]
public sealed class WorldServerIntegrationTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private UserRepository _userRepository = null!;
    private CharacterRepository _characterRepository = null!;
    private AccountBalanceRepository _balanceRepository = null!;
    private SessionTicketService _ticketService = null!;
    private IOptions<WorldOptions> _options = null!;
    private WorldPacketHandler _handler = null!;
    private WorldServer _server = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _userRepository = new UserRepository(_dbFactory);
        _characterRepository = new CharacterRepository(_dbFactory);
        _balanceRepository = new AccountBalanceRepository(_dbFactory);
        _ticketService = new SessionTicketService();

        _options = Options.Create(new WorldOptions
        {
            ListenIp = "127.0.0.1",
            Port = 0,
            ServerId = 1,
            ServerName = "TestWorld"
        });

        _handler = new WorldPacketHandler(
            _options,
            _userRepository,
            _characterRepository,
            _balanceRepository,
            _ticketService,
            NullLogger<WorldPacketHandler>.Instance);

        _server = new WorldServer(_options, _handler, NullLogger<WorldServer>.Instance);
        _server.Start();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _server.StopAsync();
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task WorldLogin_ValidTicket_ReturnsSuccessAndCharacterList()
    {
        // Arrange
        var user = await CreateUserAsync("WarriorGuy");
        await _balanceRepository.AddGoldAsync(user.Id, 15000);
        await _balanceRepository.AddCashAsync(user.Id, 500);

        await _characterRepository.CreateCharacterAsync(user.Id, "NinjaShadow", CharacterClass.Ninja, 0);
        await _characterRepository.CreateCharacterAsync(user.Id, "Smithy", CharacterClass.Blacksmith, 1);

        uint ticket = _ticketService.IssueTicket(user.Username, "127.0.0.1");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        // Act
        var loginReq = new WorldLoginRequestPacket(ticket, user.Username);
        await SendPacketAsync(stream, loginReq);

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert
        Assert.AreEqual((ushort)RakionOpcode.WorldLogin, header.Opcode);
        Assert.IsTrue(WorldLoginResponsePacket.TryParse(payload, out var response));
        Assert.AreEqual(0, response.ResultCode);
        Assert.AreEqual((uint)user.Id, response.UserId);
        Assert.AreEqual(25000u, response.Gold);
        Assert.AreEqual(500u, response.Cash);
        Assert.AreEqual(4, response.UnlockedSlots);
        Assert.IsNotNull(response.Characters);
        Assert.HasCount(2, response.Characters);

        var ninja = response.Characters.First(c => c.Name == "NinjaShadow");
        Assert.AreEqual(0, ninja.Slot);
        Assert.AreEqual(CharacterClass.Ninja, ninja.Class);
        Assert.AreEqual(1, ninja.Level);

        var smith = response.Characters.First(c => c.Name == "Smithy");
        Assert.AreEqual(1, smith.Slot);
        Assert.AreEqual(CharacterClass.Blacksmith, smith.Class);
    }

    [TestMethod]
    public async Task WorldLogin_InvalidTicket_ReturnsFailureResultCode()
    {
        // Arrange
        await CreateUserAsync("LegitUser");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        // Act: Use random unissued ticket
        var loginReq = new WorldLoginRequestPacket(999999, "LegitUser");
        await SendPacketAsync(stream, loginReq);

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert
        Assert.AreEqual((ushort)RakionOpcode.WorldLogin, header.Opcode);
        Assert.IsTrue(WorldLoginResponsePacket.TryParse(payload, out var response));
        Assert.AreNotEqual(0, response.ResultCode);
    }

    [TestMethod]
    public async Task WorldLogin_EncryptedHandshake_RespondsWithEncryptedLoginAndPart2Packets()
    {
        // Arrange
        var user = await CreateUserAsync("CryptoPlayer");
        await _balanceRepository.AddGoldAsync(user.Id, 50000);
        await _characterRepository.CreateCharacterAsync(user.Id, "CryptoNinja", CharacterClass.Ninja, 0);

        uint ticket = _ticketService.IssueTicket(user.Username, "127.0.0.1");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        // Build raw plaintext login request: [0]=0, [1..5]=ticket, [5..17]=padding, [17..]=user
        using var plainWriter = new PacketWriter();
        plainWriter.WriteByte(0); // flag
        plainWriter.WriteUInt32(ticket);
        plainWriter.WriteZeroBytes(12); // padding
        plainWriter.WriteNullTerminatedString(user.Username);

        byte[] encryptedWire = RakionWorldCrypto.EncryptPacket(77, (ushort)RakionOpcode.WorldLogin, plainWriter.WrittenSpan);
        await stream.WriteAsync(encryptedWire);
        await stream.FlushAsync();

        // Act: Read Packet 1 (Encrypted WorldLoginResponse)
        byte[] lenBuffer = new byte[2];
        await ReadExactAsync(stream, lenBuffer);
        ushort wireLen1 = BinaryPrimitives.ReadUInt16LittleEndian(lenBuffer);
        byte[] cipher1 = new byte[wireLen1 - 2];
        await ReadExactAsync(stream, cipher1);

        byte[] decBuffer1 = new byte[cipher1.Length];
        bool ok1 = RakionWorldCrypto.TryDecrypt(cipher1, decBuffer1, out int decLen1);
        Assert.IsTrue(ok1);
        ushort op1 = BinaryPrimitives.ReadUInt16LittleEndian(decBuffer1.AsSpan(0, 2));
        ushort seq1 = BinaryPrimitives.ReadUInt16LittleEndian(decBuffer1.AsSpan(2, 2));
        Assert.AreEqual((ushort)RakionOpcode.WorldLogin, op1);
        Assert.AreEqual((ushort)77, seq1);

        bool parsed1 = WorldLoginResponsePacket.TryParse(decBuffer1.AsSpan(4, decLen1 - 4), out var resp1);
        Assert.IsTrue(parsed1);
        Assert.AreEqual(0, resp1.ResultCode);
        Assert.AreEqual((uint)user.Id, resp1.UserId);
        Assert.AreEqual(60000u, resp1.Gold);
        Assert.IsNotNull(resp1.Characters);
        Assert.HasCount(1, resp1.Characters);
        Assert.AreEqual("CryptoNinja", resp1.Characters[0].Name);

        // Act: Read Packet 2 (Encrypted WorldLoginPart2)
        await ReadExactAsync(stream, lenBuffer);
        ushort wireLen2 = BinaryPrimitives.ReadUInt16LittleEndian(lenBuffer);
        byte[] cipher2 = new byte[wireLen2 - 2];
        await ReadExactAsync(stream, cipher2);

        byte[] decBuffer2 = new byte[cipher2.Length];
        bool ok2 = RakionWorldCrypto.TryDecrypt(cipher2, decBuffer2, out int decLen2);
        Assert.IsTrue(ok2);
        ushort op2 = BinaryPrimitives.ReadUInt16LittleEndian(decBuffer2.AsSpan(0, 2));
        ushort seq2 = BinaryPrimitives.ReadUInt16LittleEndian(decBuffer2.AsSpan(2, 2));
        Assert.AreEqual((ushort)RakionOpcode.WorldLoginPart2, op2);
        Assert.AreEqual((ushort)77, seq2);
        Assert.AreEqual(0, decBuffer2[4]); // WordCount
        Assert.IsTrue(WorldLoginPart2Packet.TryParse(decBuffer2.AsSpan(4, decLen2 - 4), out _));
    }

    [TestMethod]
    public async Task GameGuard_Receives0x0010_RespondsWith0x001BSuccess()
    {
        // Arrange
        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        // Act: Send GameGuard check opcode 0x0010 with 16 dummy bytes
        using (var writer = new PacketWriter((ushort)RakionOpcode.GameGuard))
        {
            writer.WriteBytes(new byte[16]);
            await stream.WriteAsync(writer.Build());
            await stream.FlushAsync();
        }

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert: Server emits 0x001B confirmation
        Assert.AreEqual((ushort)RakionOpcode.GameGuardResponse, header.Opcode);
        Assert.IsTrue(GameGuardPacket.TryParseResponse(payload, out var ggPacket));
        Assert.AreEqual(0, ggPacket.ResultCode);
    }

    [TestMethod]
    public async Task CharacterCreate_ValidParameters_CreatesAndReturnsCharacter()
    {
        // Arrange
        var user = await CreateUserAsync("CreatorGuy");
        uint ticket = _ticketService.IssueTicket(user.Username, "127.0.0.1");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        // Handshake
        await SendPacketAsync(stream, new WorldLoginRequestPacket(ticket, user.Username));
        await ReadPacketAsync(stream);

        // Act: Create new Swordsman in slot 0
        var createReq = new CharacterCreateRequestPacket("Excalibur", CharacterClass.Swordman, 0);
        await SendPacketAsync(stream, createReq);

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert
        Assert.AreEqual((ushort)RakionOpcode.CharacterCreate, header.Opcode);
        Assert.IsTrue(CharacterCreateResponsePacket.TryParse(payload, out var createResp));
        Assert.AreEqual(0, createResp.ResultCode);
        Assert.IsNotNull(createResp.Character);
        Assert.AreEqual("Excalibur", createResp.Character.Name);
        Assert.AreEqual(CharacterClass.Swordman, createResp.Character.Class);
        Assert.AreEqual(0, createResp.Character.Slot);

        // Verify in database
        var characters = await _characterRepository.GetCharactersByUserIdAsync(user.Id);
        Assert.HasCount(1, characters);
        Assert.AreEqual("Excalibur", characters[0].Name);
    }

    [TestMethod]
    public async Task CharacterCreate_DuplicateName_ReturnsFailure()
    {
        // Arrange
        var user = await CreateUserAsync("HeroUser");
        await _characterRepository.CreateCharacterAsync(user.Id, "HeroOne", CharacterClass.Archer, 0);
        uint ticket = _ticketService.IssueTicket(user.Username, "127.0.0.1");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        await SendPacketAsync(stream, new WorldLoginRequestPacket(ticket, user.Username));
        await ReadPacketAsync(stream);

        // Act: Attempt to create another character with the same name
        var duplicateReq = new CharacterCreateRequestPacket("HeroOne", CharacterClass.Mage, 1);
        await SendPacketAsync(stream, duplicateReq);

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert
        Assert.AreEqual((ushort)RakionOpcode.CharacterCreate, header.Opcode);
        Assert.IsTrue(CharacterCreateResponsePacket.TryParse(payload, out var response));
        Assert.AreNotEqual(0, response.ResultCode);
    }

    [TestMethod]
    public async Task CharacterSelect_ValidId_SelectsCharacter()
    {
        // Arrange
        var user = await CreateUserAsync("PlayerSelect");
        var ch = await _characterRepository.CreateCharacterAsync(user.Id, "SelectMe", CharacterClass.Mage, 0);
        uint ticket = _ticketService.IssueTicket(user.Username, "127.0.0.1");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        await SendPacketAsync(stream, new WorldLoginRequestPacket(ticket, user.Username));
        await ReadPacketAsync(stream);

        // Act: Select character
        var selectReq = new CharacterSelectRequestPacket((uint)ch.Id);
        await SendPacketAsync(stream, selectReq);

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert: Opcode 0x002E
        Assert.AreEqual((ushort)RakionOpcode.CharacterSelectResponse, header.Opcode);
        Assert.IsTrue(CharacterSelectResponsePacket.TryParse(payload, out var selectResp));
        Assert.AreEqual(0, selectResp.ResultCode);
        Assert.AreEqual((uint)ch.Id, selectResp.CharacterId);
    }

    [TestMethod]
    public async Task CharacterDelete_ValidKey_DeletesCharacter()
    {
        // Arrange
        var user = await CreateUserAsync("PlayerDelete");
        var ch = await _characterRepository.CreateCharacterAsync(user.Id, "DeleteMe", CharacterClass.Archer, 0);
        uint ticket = _ticketService.IssueTicket(user.Username, "127.0.0.1");

        using var client = await ConnectClientAsync();
        var stream = client.GetStream();

        await SendPacketAsync(stream, new WorldLoginRequestPacket(ticket, user.Username));
        await ReadPacketAsync(stream);

        // Act: Delete character with default key "sirmaster"
        var deleteReq = new CharacterDeleteRequestPacket((uint)ch.Id, "sirmaster");
        await SendPacketAsync(stream, deleteReq);

        var (header, payload) = await ReadPacketAsync(stream);

        // Assert: Opcode 0x002D
        Assert.AreEqual((ushort)RakionOpcode.CharacterDeleteResponse, header.Opcode);
        Assert.IsTrue(CharacterDeleteResponsePacket.TryParse(payload, out var deleteResp));
        Assert.AreEqual(0, deleteResp.ResultCode);
        Assert.AreEqual((uint)ch.Id, deleteResp.CharacterId);

        // Verify character is removed from active roster
        var characters = await _characterRepository.GetCharactersByUserIdAsync(user.Id);
        Assert.IsEmpty(characters);
    }

    private async Task<UserEntity> CreateUserAsync(string username)
    {
        var user = new UserEntity
        {
            Username = username,
            PasswordHash = "dummy_hash",
            Email = $"{username.ToLowerInvariant()}@rakion.test"
        };
        int userId = await _userRepository.CreateUserAsync(user);
        user.Id = userId;

        // Ensure user game info exists with 4 slots
        await _userRepository.GetGameInfoByUserIdAsync(userId);
        return user;
    }

    private async Task<TcpClient> ConnectClientAsync()
    {
        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        return client;
    }

    private static async Task SendPacketAsync(NetworkStream stream, IPacket packet)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<(PacketHeader Header, byte[] Payload)> ReadPacketAsync(NetworkStream stream)
    {
        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        if (!PacketHeader.TryParse(headerBuffer, out var header))
        {
            throw new InvalidOperationException("Failed to parse packet header.");
        }

        byte[] payload = new byte[header.PayloadLength];
        if (header.PayloadLength > 0)
        {
            await ReadExactAsync(stream, payload);
        }

        return (header, payload);
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cts.Token);
            if (read == 0)
            {
                throw new EndOfStreamException("Socket reached end of stream.");
            }
            totalRead += read;
        }
    }
}
