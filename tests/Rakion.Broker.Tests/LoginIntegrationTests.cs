using System.Net;
using System.Net.Sockets;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;
using Rakion.Broker.Handlers;
using Rakion.Broker.Hosting;
using Rakion.Broker.Services;
using Rakion.Core.Cryptography;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Security;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Broker.Tests;

[TestClass]
public sealed class LoginIntegrationTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private UserRepository _userRepository = null!;
    private PasswordHasher _hasher = null!;
    private SessionTicketService _ticketService = null!;
    private IOptions<BrokerOptions> _options = null!;
    private BrokerServer _server = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=broker_login_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _userRepository = new UserRepository(_dbFactory);
        _hasher = new PasswordHasher();
        _ticketService = new SessionTicketService();

        _options = Options.Create(new BrokerOptions
        {
            ListenIp = "127.0.0.1",
            Port = 0
        });

        var handler = new BrokerPacketHandler(_options, _userRepository, _hasher, _ticketService, NullLogger<BrokerPacketHandler>.Instance);
        _server = new BrokerServer(_options, handler, NullLogger<BrokerServer>.Instance);
        _server.Start();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _server.StopAsync();
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task Login_ValidCredentials_ReturnsSuccessAndIssuesSessionTicket()
    {
        // Arrange
        string passwordHash = _hasher.HashPassword("secret123");
        var user = new UserEntity
        {
            Username = "Warrior01",
            PasswordHash = passwordHash,
            Email = "warrior@rakion.test",
            Authority = 0
        };
        await _userRepository.CreateUserAsync(user);

        // Act
        var response = await SendLoginPacketAsync("Warrior01", "secret123", (ushort)RakionOpcode.Login);

        // Assert
        Assert.AreEqual(LoginResultCode.Success, response.Result);
        Assert.AreEqual((ushort)0xCBEB, response.HeaderOpcode);
        Assert.AreEqual((ushort)0x1312, response.SubOpcode);

        // Verify session ticket exists
        bool found = _ticketService.TryGetTicketByUsername("Warrior01", out var ticketInfo);
        Assert.IsTrue(found, "A valid session ticket should have been issued for the user.");
        Assert.IsGreaterThan(0u, ticketInfo.Ticket);
    }

    [TestMethod]
    public async Task Login_LegacyMd5Password_AuthenticatesAndUpgradesHashToBCrypt()
    {
        // Arrange: MD5 hash of "legacy_pass" is 29b32c31167c0b53479b3675bf27be70
        const string md5Hash = "29b32c31167c0b53479b3675bf27be70";
        var user = new UserEntity
        {
            Username = "OldPlayer",
            PasswordHash = md5Hash,
            Email = "old@rakion.test"
        };
        int userId = await _userRepository.CreateUserAsync(user);

        // Act
        var response = await SendLoginPacketAsync("OldPlayer", "legacy_pass", (ushort)RakionOpcode.Login);

        // Assert
        Assert.AreEqual(LoginResultCode.Success, response.Result);

        // Verify password hash in database was automatically upgraded to modern BCrypt
        var updatedUser = await _userRepository.GetByIdAsync(userId);
        Assert.IsNotNull(updatedUser);
        Assert.IsTrue(updatedUser.PasswordHash.StartsWith("$2", StringComparison.Ordinal), "Legacy hash should be upgraded to BCrypt ($2*).");
    }

    [TestMethod]
    public async Task Login_InvalidPassword_ReturnsInvalidCredentials()
    {
        // Arrange
        string hash = _hasher.HashPassword("correct_password");
        await _userRepository.CreateUserAsync(new UserEntity { Username = "TargetUser", PasswordHash = hash });

        // Act
        var response = await SendLoginPacketAsync("TargetUser", "WRONG_PASSWORD", (ushort)RakionOpcode.Login);

        // Assert
        Assert.AreEqual(LoginResultCode.InvalidCredentials, response.Result);
    }

    [TestMethod]
    public async Task Login_NonExistentUser_ReturnsInvalidCredentials()
    {
        var response = await SendLoginPacketAsync("DoesNotExist", "any_password", (ushort)RakionOpcode.Login);
        Assert.AreEqual(LoginResultCode.InvalidCredentials, response.Result);
    }

    [TestMethod]
    public async Task Login_BannedUser_ReturnsAccountBanned()
    {
        // Arrange
        string hash = _hasher.HashPassword("valid_pass");
        int userId = await _userRepository.CreateUserAsync(new UserEntity { Username = "Cheater99", PasswordHash = hash });

        // Mark account banned in user_game_info
        using var conn = await _dbFactory.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE user_game_info SET is_banned = 1, ban_date = @BanDate, ban_reason = 'Win-trading detected' WHERE user_id = @UserId",
            new { BanDate = DateTime.UtcNow.AddDays(7), UserId = userId });

        // Act
        var response = await SendLoginPacketAsync("Cheater99", "valid_pass", (ushort)RakionOpcode.Login);

        // Assert
        Assert.AreEqual(LoginResultCode.AccountBanned, response.Result);
    }

    [TestMethod]
    public async Task Login_AlternativeOpcode3000_ReturnsExpectedResponseStructure()
    {
        string hash = _hasher.HashPassword("alt_pass");
        await _userRepository.CreateUserAsync(new UserEntity { Username = "AltUser", PasswordHash = hash });

        var response = await SendLoginPacketAsync("AltUser", "alt_pass", (ushort)RakionOpcode.LoginAlt2);

        Assert.AreEqual(LoginResultCode.Success, response.Result);
        Assert.AreEqual((ushort)0xEBD3, response.HeaderOpcode);
        Assert.AreEqual((ushort)0x3001, response.SubOpcode);
    }

    [TestMethod]
    public async Task Login_AlternativeOpcode4000_ReturnsExtendedTicketResponse()
    {
        string hash = _hasher.HashPassword("alt4_pass");
        await _userRepository.CreateUserAsync(new UserEntity { Username = "Alt4User", PasswordHash = hash });

        var response = await SendLoginPacketAsync("Alt4User", "alt4_pass", (ushort)RakionOpcode.LoginAlt4);

        Assert.AreEqual(LoginResultCode.Success, response.Result);
        Assert.AreEqual((ushort)0x1BAF, response.HeaderOpcode);
        Assert.AreEqual((ushort)0x4001, response.SubOpcode);
        Assert.IsNotNull(response.SessionTicket);
    }
    [TestMethod]
    public async Task Login_NyxLauncherFixedHandshake_ExistingUser_ReturnsSuccessAndIssuesTicket()
    {
        string hash = _hasher.HashPassword("any_password");
        await _userRepository.CreateUserAsync(new UserEntity { Username = "LauncherUser", PasswordHash = hash });

        // Build 70-byte fixed packet
        byte[] packet = new byte[70];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 70);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), 0x0000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 0x1310);
        System.Text.Encoding.Latin1.GetBytes("LauncherUser").CopyTo(packet.AsSpan(14));

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));
        Assert.AreEqual((ushort)0xCBEB, responseHeader.Opcode);

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));

        Assert.AreEqual(LoginResultCode.Success, responsePacket.Result);
        Assert.AreEqual((ushort)0x1312, responsePacket.SubOpcode);

        bool foundTicket = _ticketService.TryGetTicketByUsername("LauncherUser", out var ticketInfo);
        Assert.IsTrue(foundTicket, "A valid session ticket should have been issued for LauncherUser.");
        Assert.IsGreaterThan(0u, ticketInfo.Ticket);
    }

    [TestMethod]
    public async Task Login_NyxLauncherFixedHandshake_NonExistentUser_ReturnsInvalidCredentials()
    {
        byte[] packet = new byte[70];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 70);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), 0x0000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), 0x1310);
        System.Text.Encoding.Latin1.GetBytes("NoSuchUser").CopyTo(packet.AsSpan(14));

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));

        Assert.AreEqual(LoginResultCode.InvalidCredentials, responsePacket.Result);
    }

    [TestMethod]
    public async Task Login_NyxLauncherEncrypted_DynamicUser_IssuesTicketToCorrectUser()
    {
        string hash = _hasher.HashPassword("secret123");
        await _userRepository.CreateUserAsync(new UserEntity { Username = "DynamicHero", PasswordHash = hash });

        // Build authentic encrypted 70-byte packet with NyxLauncherCrypto
        byte[] packet = NyxLauncherCrypto.BuildPacket("DynamicHero", "secret123");

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        // 1. Send 0x1310 handshake
        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));
        Assert.AreEqual((ushort)0xCBEB, responseHeader.Opcode);

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));
        Assert.AreEqual(LoginResultCode.Success, responsePacket.Result);
        Assert.AreEqual((ushort)0x1312, responsePacket.SubOpcode);

        // 2. Send 0x4000 "Start Game" on the SAME connection
        byte[] startPayload = Convert.FromHexString(
            "00400652616B696F6E000000001450656E7469756D3F49492028302E323520B56D29580B0000FFFFFF7F0000FFFF");
        byte[] startGamePacket = new byte[PacketHeader.HeaderSize + startPayload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(startGamePacket.AsSpan(0, 2), (ushort)startGamePacket.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(startGamePacket.AsSpan(2, 2), (ushort)RakionOpcode.None);
        startPayload.CopyTo(startGamePacket.AsSpan(PacketHeader.HeaderSize));

        await stream.WriteAsync(startGamePacket);
        await stream.FlushAsync();

        byte[] startHeaderBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, startHeaderBuffer);
        Assert.IsTrue(PacketHeader.TryParse(startHeaderBuffer, out var startResponseHeader));
        Assert.AreEqual((ushort)0x1BAF, startResponseHeader.Opcode);

        byte[] startPayloadBuffer = new byte[startResponseHeader.PayloadLength];
        await ReadExactAsync(stream, startPayloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(startPayloadBuffer, startResponseHeader.Opcode, out var startResponsePacket));
        Assert.AreEqual(LoginResultCode.Success, startResponsePacket.Result);
        Assert.AreEqual((ushort)0x4001, startResponsePacket.SubOpcode);
        Assert.IsNotNull(startResponsePacket.SessionTicket);

        // 3. Verify that the session ticket was issued to "DynamicHero", NOT "admin"
        bool foundDynamicTicket = _ticketService.TryGetTicketByUsername("DynamicHero", out var dynamicTicket);
        Assert.IsTrue(foundDynamicTicket, "Session ticket must be issued to the authenticated DynamicHero user.");
        Assert.AreEqual(startResponsePacket.SessionTicket.Value, dynamicTicket.Ticket);

        bool foundAdminTicket = _ticketService.TryGetTicketByUsername("admin", out _);
        Assert.IsFalse(foundAdminTicket, "No session ticket should be issued to admin when DynamicHero logged in.");
    }

    [TestMethod]
    public async Task Login_NyxLauncherEncrypted_NonExistentUser_ReturnsInvalidCredentials()
    {
        // Build authentic encrypted 70-byte packet with non-existent username
        byte[] packet = NyxLauncherCrypto.BuildPacket("NonExistentNyxUser", "any_password");

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));

        Assert.AreEqual(LoginResultCode.InvalidCredentials, responsePacket.Result);
        Assert.AreEqual((ushort)0x1312, responsePacket.SubOpcode);
    }

    [TestMethod]
    public async Task Login_NyxLauncherEncrypted_BannedUser_ReturnsAccountBanned()
    {
        string hash = _hasher.HashPassword("secret123");
        int userId = await _userRepository.CreateUserAsync(new UserEntity { Username = "BannedHero", PasswordHash = hash });

        // Mark account banned in user_game_info
        using var conn = await _dbFactory.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE user_game_info SET is_banned = 1, ban_date = @BanDate, ban_reason = 'Test violation' WHERE user_id = @UserId",
            new { BanDate = DateTime.UtcNow.AddDays(7), UserId = userId });

        byte[] packet = NyxLauncherCrypto.BuildPacket("BannedHero", "secret123");

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));

        Assert.AreEqual(LoginResultCode.AccountBanned, responsePacket.Result);
    }

    [TestMethod]
    public async Task Login_NyxLauncherSecondaryHandshake_OpcodeCDDFWith3000_ReturnsCL_AUTH_LOGIN_2_Success()
    {
        // 6-byte packet: TotalLength=6, Opcode=0xCDDF, Payload=0x3000
        byte[] packet = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), 6);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)RakionOpcode.ServerListRequest); // 0xCDDF
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), (ushort)RakionOpcode.LoginAlt2); // 0x3000

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));
        Assert.AreEqual((ushort)0xEBD3, responseHeader.Opcode);

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));

        Assert.AreEqual(LoginResultCode.Success, responsePacket.Result);
        Assert.AreEqual((ushort)0x3001, responsePacket.SubOpcode);
    }

    [TestMethod]
    public async Task Login_NyxLauncherStartGame_Opcode4000_ReturnsCL_AUTH_LOGIN_4_WithTicket()
    {
        // Real observed Start Game payload from NyxLauncher (46 bytes)
        byte[] payload = Convert.FromHexString(
            "00400652616B696F6E000000001450656E7469756D3F49492028302E323520B56D29580B0000FFFFFF7F0000FFFF");

        byte[] packet = new byte[PacketHeader.HeaderSize + payload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)packet.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)RakionOpcode.None);
        payload.CopyTo(packet.AsSpan(PacketHeader.HeaderSize));

        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(packet);
        await stream.FlushAsync();

        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));
        Assert.AreEqual((ushort)0x1BAF, responseHeader.Opcode);

        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);
        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));

        Assert.AreEqual(LoginResultCode.Success, responsePacket.Result);
        Assert.AreEqual((ushort)0x4001, responsePacket.SubOpcode);
        Assert.IsNotNull(responsePacket.SessionTicket);
        Assert.IsGreaterThan(0u, responsePacket.SessionTicket.Value);
    }
    private async Task<LoginResponsePacket> SendLoginPacketAsync(string username, string password, ushort opcode)
    {
        var endPoint = (IPEndPoint)_server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        var loginPacket = new LoginRequestPacket(username, password, SubOpcode: opcode);
        using var writer = new PacketWriter(loginPacket.Opcode);
        loginPacket.Serialize(writer);
        byte[] bytes = writer.Build();

        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        // Read response header
        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));

        // Read payload
        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);

        Assert.IsTrue(LoginResponsePacket.TryParse(payloadBuffer, responseHeader.Opcode, out var responsePacket));
        return responsePacket;
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException("Socket reached end of stream.");
            }
            totalRead += read;
        }
    }
}
