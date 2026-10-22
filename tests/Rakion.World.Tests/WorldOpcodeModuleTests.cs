using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rakion.Broker.Services;
using Rakion.Core.Enums;
using Rakion.Core.Protocol;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.Network.Tcp;
using Rakion.World.Channels;
using Rakion.World.Configuration;
using Rakion.World.Handlers;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class WorldOpcodeModuleTests
{
    // Not bound by any built-in World handler.
    private const ClientOpcode TestOpcode = (ClientOpcode)0x7FFE;

    private SqliteTestConnectionFactory _dbFactory = null!;
    private IOptions<WorldOptions> _options = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_module_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _options = Options.Create(new WorldOptions { ServerName = "TestWorld" });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public void Registry_MapSameOpcodeTwice_Throws()
    {
        var registry = new WorldOpcodeRegistry();
        registry.Map(TestOpcode, (_, _, _) => ValueTask.CompletedTask);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.Map(TestOpcode, (_, _, _) => ValueTask.CompletedTask));
    }

    [TestMethod]
    public void Registry_TryGetHandler_ReturnsOnlyMappedOpcodes()
    {
        var registry = new WorldOpcodeRegistry();
        registry.Map(TestOpcode, (_, _, _) => ValueTask.CompletedTask);

        Assert.IsTrue(registry.TryGetHandler((ushort)TestOpcode, out _));
        Assert.IsFalse(registry.TryGetHandler(0x7FFD, out _));
        Assert.AreEqual(1, registry.Count);
    }

    [TestMethod]
    public void Handler_ModuleMappingBuiltInOpcode_ThrowsAtConstruction()
    {
        var module = new RecordingModule(ClientOpcode.ChannelChat);

        Assert.ThrowsExactly<InvalidOperationException>(() => CreateHandler(module));
    }

    [TestMethod]
    public async Task Handler_DispatchesUnclaimedOpcodeToModule()
    {
        var module = new RecordingModule(TestOpcode);
        var handler = CreateHandler(module);
        var connection = new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), handler, NullLogger.Instance);
        await handler.OnConnectedAsync(connection, CancellationToken.None);

        // 3-byte payload keeps the frame off the AES block size, so it is read as plaintext.
        byte[] payload = [0x01, 0x02, 0x03];
        var header = new PacketHeader((ushort)(PacketHeader.HeaderSize + payload.Length), (ushort)TestOpcode);
        await handler.OnPacketReceivedAsync(connection, header, payload, CancellationToken.None);

        Assert.AreEqual(1, module.Calls);
        CollectionAssert.AreEqual(payload, module.LastPayload);
        Assert.AreSame(connection, module.LastSession!.Connection);
    }

    private WorldPacketHandler CreateHandler(IWorldOpcodeModule module) =>
        new(
            _options,
            new UserRepository(_dbFactory),
            new CharacterRepository(_dbFactory),
            new AccountBalanceRepository(_dbFactory),
            new SessionTicketService(),
            new ChannelManager(_options, NullLogger<ChannelManager>.Instance),
            [module],
            WorldSessionSender.Instance,
            NullLogger<WorldPacketHandler>.Instance);

    private sealed class RecordingModule(ClientOpcode opcode) : IWorldOpcodeModule
    {
        public int Calls { get; private set; }
        public byte[]? LastPayload { get; private set; }
        public WorldSession? LastSession { get; private set; }

        public void Register(WorldOpcodeRegistry registry)
        {
            registry.Map(opcode, (session, payload, _) =>
            {
                Calls++;
                LastPayload = payload.ToArray();
                LastSession = session;
                return ValueTask.CompletedTask;
            });
        }
    }
}
