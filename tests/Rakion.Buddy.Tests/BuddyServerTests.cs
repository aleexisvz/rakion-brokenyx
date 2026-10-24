using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rakion.Core.Buddy;
using Rakion.Core.Protocol;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Buddy.Tests;

[TestClass]
public sealed class BuddyServerTests
{
    private SqliteTestConnectionFactory _factory = null!;
    private BuddyServer _server = null!;
    private int _port;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new SqliteTestConnectionFactory($"BuddyDb_{Guid.NewGuid():N}");
        var users = new UserRepository(_factory);
        await users.CreateUserAsync(new UserEntity { Username = "alice", PasswordHash = "x", Email = "a@test" });
        await users.CreateUserAsync(new UserEntity { Username = "bob", PasswordHash = "x", Email = "b@test" });
        var options = Options.Create(new BuddyOptions { ListenIp = "127.0.0.1", Port = 0 });
        var handler = new BuddyPacketHandler(new BuddyRepository(_factory), NullLogger<BuddyPacketHandler>.Instance);
        _server = new BuddyServer(options, handler, NullLogger<BuddyServer>.Instance);
        _server.Start();
        _port = ((IPEndPoint)_server.LocalEndPoint!).Port;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _server.DisposeAsync();
        _factory.Dispose();
    }

    [TestMethod]
    public void Crypto_RoundTripsTheCredentialAndTheMarkerBlocks()
    {
        byte[] credential = BuddyCrypto.CreateCredential("alice", 0x12345678);
        Assert.IsTrue(BuddyCrypto.TryReadCredential(credential, out string account, out uint seed));
        Assert.AreEqual(("alice", 0x12345678u), (account, seed));

        byte[] key = BuddyCrypto.SessionKey("alice", 0x12345678);
        byte[] data = Enumerable.Range(0, BuddyCrypto.LoginDataLength).Select(i => (byte)i).ToArray();
        byte[] cipher = BuddyCrypto.EncryptBlocks(data, key);
        Assert.HasCount(176, cipher);
        Assert.IsTrue(BuddyCrypto.TryDecryptBlocks(cipher, key, out byte[] clear));
        CollectionAssert.AreEqual(data, clear);
        Assert.IsFalse(BuddyCrypto.TryDecryptBlocks(cipher, BuddyCrypto.SessionKey("bob", 1), out _), "wrong key fails the marker");
    }

    [TestMethod]
    public async Task Login_ListsFriends_AndUdpRegistrationAnnouncesPresence()
    {
        await new BuddyRepository(_factory).AddFriendAsync("alice", "bob");

        using var alice = await LoginAsync("alice");
        var (aliceToken, aliceFriends) = ReadLogin(alice.Login);
        Assert.AreEqual(1, aliceFriends);
        Assert.AreEqual("bob", Encoding.Latin1.GetString(alice.Login.AsSpan(8, 3)));
        await RegisterUdpAsync(aliceToken);
        Assert.AreEqual((ushort)BuddyOpcode.VipIpPortNotice, (await alice.ReadAsync()).Opcode);

        using var bob = await LoginAsync("bob");
        var (bobToken, _) = ReadLogin(bob.Login);
        await RegisterUdpAsync(bobToken);

        // Bob gets his own endpoint and alice's online state; alice's notice can arrive before or after.
        var bobPackets = new[] { await bob.ReadAsync(), await bob.ReadAsync() };
        Assert.IsTrue(bobPackets.Any(p => p.Opcode == (ushort)BuddyOpcode.VipIpPortNotice));
        var bobSees = bobPackets.Single(p => p.Opcode == (ushort)BuddyOpcode.UserStateNotice);
        Assert.AreEqual("alice", Encoding.Latin1.GetString(bobSees.Payload.AsSpan(2, 5)), "bob learns alice is online");
        Assert.AreEqual((byte)1, bobSees.Payload[2 + 20]);

        var aliceSees = await alice.ReadAsync();
        Assert.AreEqual((ushort)BuddyOpcode.UserStateNotice, aliceSees.Opcode, "alice learns bob came online");
        Assert.AreEqual("bob", Encoding.Latin1.GetString(aliceSees.Payload.AsSpan(2, 3)));
    }

    [TestMethod]
    public async Task Login_RefusesAWrongSeed()
    {
        using var client = new BuddyClient();
        await client.ConnectAsync(_port);
        await client.SendAsync(BuddyOpcode.PreCredentialRequest, []);
        await client.ReadAsync();
        byte[] login = new byte[BuddyCrypto.LoginPayloadLength];
        BuddyCrypto.CreateCredential("alice", 1).CopyTo(login, 0);
        await client.SendAsync(BuddyOpcode.LoginRequest, login);

        var answer = await client.ReadAsync();
        Assert.AreEqual((ushort)BuddyOpcode.LoginResponse, answer.Opcode);
        Assert.AreEqual(BuddyLoginResponsePacket.BadCredential, BinaryPrimitives.ReadUInt16LittleEndian(answer.Payload));
    }

    private async Task<LoggedIn> LoginAsync(string account)
    {
        var client = new BuddyClient();
        await client.ConnectAsync(_port);
        await client.SendAsync(BuddyOpcode.PreCredentialRequest, []);
        var pre = await client.ReadAsync();
        uint seed = BinaryPrimitives.ReadUInt32LittleEndian(pre.Payload);

        byte[] data = new byte[BuddyCrypto.LoginDataLength];
        BinaryPrimitives.WriteUInt32LittleEndian(data, BuddyCrypto.LoginDataHeader);
        new byte[] { 127, 0, 0, 1 }.CopyTo(data, 0x18);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0x1C), 5000);
        byte[] login = [.. BuddyCrypto.CreateCredential(account, seed), .. BuddyCrypto.EncryptBlocks(data, BuddyCrypto.SessionKey(account, seed))];
        await client.SendAsync(BuddyOpcode.LoginRequest, login);
        var answer = await client.ReadAsync();
        Assert.AreEqual((ushort)BuddyOpcode.LoginResponse, answer.Opcode);
        Assert.AreEqual(BuddyLoginResponsePacket.Success, BinaryPrimitives.ReadUInt16LittleEndian(answer.Payload));
        return new LoggedIn(client, answer.Payload);
    }

    private static (uint Token, int Friends) ReadLogin(byte[] payload) =>
        (BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2)), BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6)));

    private async Task RegisterUdpAsync(uint token)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        byte[] datagram = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(datagram, token);
        await udp.SendAsync(datagram, new IPEndPoint(IPAddress.Loopback, _port));
    }

    private sealed record LoggedIn(BuddyClient Client, byte[] Login) : IDisposable
    {
        public Task<(ushort Opcode, byte[] Payload)> ReadAsync() => Client.ReadAsync();

        public void Dispose() => Client.Dispose();
    }

    private sealed class BuddyClient : IDisposable
    {
        private readonly TcpClient _tcp = new();
        private NetworkStream _stream = null!;

        public async Task ConnectAsync(int port)
        {
            await _tcp.ConnectAsync(IPAddress.Loopback, port);
            _stream = _tcp.GetStream();
        }

        public async Task SendAsync(BuddyOpcode opcode, byte[] payload)
        {
            using var writer = new PacketWriter((ushort)opcode);
            writer.WriteBytes(payload);
            await _stream.WriteAsync(writer.Build());
        }

        public async Task<(ushort Opcode, byte[] Payload)> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            byte[] header = new byte[4];
            await _stream.ReadExactlyAsync(header, timeout.Token);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(header);
            byte[] payload = new byte[size - 4];
            await _stream.ReadExactlyAsync(payload, timeout.Token);
            return (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)), payload);
        }

        public void Dispose() => _tcp.Dispose();
    }
}
