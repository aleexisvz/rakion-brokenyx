using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.Network.Tcp;
using Rakion.World.Features.Economy;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class InventoryModuleTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private InventoryRepository _inventoryRepository = null!;
    private ListLogger _logger = null!;
    private WorldOpcodeRegistry _registry = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_inventory_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _inventoryRepository = new InventoryRepository(_dbFactory);
        _logger = new ListLogger();
        _registry = new WorldOpcodeRegistry();
        new InventoryModule(_inventoryRepository, _logger).Register(_registry);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public void Register_BindsInventoryEnterAndLeave()
    {
        Assert.IsTrue(_registry.IsMapped(ClientOpcode.InventoryEnter));
        Assert.IsTrue(_registry.IsMapped(ClientOpcode.InventoryLeave));
        Assert.AreEqual(2, _registry.Count);
    }

    [TestMethod]
    public async Task InventoryEnter_WithActiveCharacter_LoadsAccountItems()
    {
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 100, SlotIndex = 1 });
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = 5, CharacterId = 9, ItemId = 101, SlotIndex = 2 });
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = 6, CharacterId = 0, ItemId = 102, SlotIndex = 1 });

        var session = CreateSession();
        session.User = new UserEntity { Id = 5, Username = "bob" };
        session.ActiveCharacter = new CharacterEntity { Id = 9, UserId = 5, Name = "Bobby" };

        await DispatchAsync(session, ClientOpcode.InventoryEnter);

        Assert.IsTrue(
            _logger.Messages.Any(m => m.Contains("'Bobby': 2 items loaded")),
            string.Join(Environment.NewLine, _logger.Messages));
    }

    [TestMethod]
    public async Task InventoryEnter_WithoutActiveCharacter_IsRejected()
    {
        var session = CreateSession();
        session.User = new UserEntity { Id = 5, Username = "bob" };

        await DispatchAsync(session, ClientOpcode.InventoryEnter);

        Assert.IsTrue(_logger.Messages.Any(m => m.Contains("InventoryEnter rejected")));
        Assert.IsFalse(_logger.Messages.Any(m => m.Contains("items loaded")));
    }

    private async Task DispatchAsync(WorldSession session, ClientOpcode opcode)
    {
        Assert.IsTrue(_registry.TryGetHandler((ushort)opcode, out var handler));
        await handler(session, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
    }

    private static WorldSession CreateSession() =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance));

    private sealed class ListLogger : ILogger<InventoryModule>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
