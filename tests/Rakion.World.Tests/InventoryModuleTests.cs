using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Core.Utilities;
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
    private static readonly DateTime Now = new(2026, 10, 21, 22, 15, 30);

    private SqliteTestConnectionFactory _dbFactory = null!;
    private InventoryRepository _inventoryRepository = null!;
    private RecordingSender _sender = null!;
    private WorldOpcodeRegistry _registry = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_inventory_module_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _inventoryRepository = new InventoryRepository(_dbFactory);
        _sender = new RecordingSender();
        _registry = new WorldOpcodeRegistry();
        new InventoryModule(_sender, _inventoryRepository, new FixedTimeProvider(Now), NullLogger<InventoryModule>.Instance).Register(_registry);
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
        Assert.IsTrue(_registry.IsMapped(ClientOpcode.InventoryMove));
        Assert.IsTrue(_registry.IsMapped(ClientOpcode.FieldGamePotion));
        Assert.AreEqual(4, _registry.Count);
    }

    [TestMethod]
    public async Task InventoryEnter_WithActiveCharacter_RepliesSuccessWithServerTime()
    {
        var session = CreateSession();
        session.User = new UserEntity { Id = 5, Username = "bob" };
        session.ActiveCharacter = new CharacterEntity { Id = 9, UserId = 5, Name = "Bobby" };

        await DispatchAsync(session, ClientOpcode.InventoryEnter);

        Assert.HasCount(1, _sender.Sent);
        Assert.AreSame(session, _sender.Sent[0].Session);
        var response = (InventoryEnterResponsePacket)_sender.Sent[0].Packet;
        Assert.AreEqual((ushort)ServerOpcode.InventoryEnter, response.Opcode);
        Assert.AreEqual(InventoryEnterResponsePacket.ResultSuccess, response.Result);
        Assert.AreEqual(LegacyGameClock.ToMinutes(Now), response.ServerTimeMinutes);
    }

    [TestMethod]
    public async Task InventoryEnter_WithoutActiveCharacter_SendsNothing()
    {
        var session = CreateSession();
        session.User = new UserEntity { Id = 5, Username = "bob" };

        await DispatchAsync(session, ClientOpcode.InventoryEnter);

        Assert.IsEmpty(_sender.Sent);
    }

    [TestMethod]
    public async Task InventoryLeave_RepliesSuccess()
    {
        var session = CreateSession();

        await DispatchAsync(session, ClientOpcode.InventoryLeave);

        Assert.HasCount(1, _sender.Sent);
        var response = (InventoryLeaveResponsePacket)_sender.Sent[0].Packet;
        Assert.AreEqual((ushort)ServerOpcode.InventoryLeave, response.Opcode);
        Assert.AreEqual(InventoryLeaveResponsePacket.ResultSuccess, response.Result);
    }

    [TestMethod]
    public async Task InventoryMove_BoxToBox_SwapsAndReportsBothSlots()
    {
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 1002, Level = 5, SlotIndex = 1 });
        var session = CreatePlayingSession();

        await MoveAsync(session, new InventoryMoveRequestPacket(InventoryLocation.AccountBox, 1, InventoryLocation.AccountBox, 10));

        var response = (InventoryMoveResponsePacket)_sender.Sent.Single().Packet;
        Assert.AreEqual(InventoryMoveResponsePacket.ResultSuccess, response.Result);
        Assert.AreEqual(new InventorySlotState(InventoryLocation.AccountBox, 1, 0, 0, 0), response.Source);
        Assert.AreEqual(new InventorySlotState(InventoryLocation.AccountBox, 10, 1002, 5, 0), response.Destination);
        Assert.AreEqual((byte)10, (await _inventoryRepository.GetUserItemsAsync(5)).Single().SlotIndex);
    }

    [TestMethod]
    public async Task InventoryMove_FromEmptySlot_ReportsSourceEmpty()
    {
        await MoveAsync(CreatePlayingSession(), new InventoryMoveRequestPacket(InventoryLocation.AccountBox, 3, InventoryLocation.AccountBox, 4));

        var response = (InventoryMoveResponsePacket)_sender.Sent.Single().Packet;
        Assert.AreEqual(InventoryMoveResponsePacket.ResultSourceEmpty, response.Result);
    }

    [TestMethod]
    public async Task InventoryMove_BoxToEquipment_EquipsOnActiveCharacter()
    {
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 1001, Level = 3, SlotIndex = 0 });

        await MoveAsync(CreatePlayingSession(), new InventoryMoveRequestPacket(InventoryLocation.AccountBox, 0, InventoryLocation.Equipment, 2));

        var response = (InventoryMoveResponsePacket)_sender.Sent.Single().Packet;
        Assert.AreEqual(InventoryMoveResponsePacket.ResultSuccess, response.Result);
        Assert.AreEqual(new InventorySlotState(InventoryLocation.Equipment, 2, 1001, 3, 0), response.Destination);
        var item = (await _inventoryRepository.GetUserItemsAsync(5)).Single();
        Assert.AreEqual((9, (byte)2), (item.CharacterId, item.SlotIndex));
    }

    [TestMethod]
    public async Task InventoryMove_OutOfRange_IsRejectedWithoutChanges()
    {
        await _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = 5, CharacterId = 0, ItemId = 1001, SlotIndex = 0 });

        await MoveAsync(CreatePlayingSession(), new InventoryMoveRequestPacket(InventoryLocation.AccountBox, 0, InventoryLocation.Equipment, 19));

        Assert.AreEqual(InventoryMoveResponsePacket.ResultNotAllowed, ((InventoryMoveResponsePacket)_sender.Sent.Single().Packet).Result);
        Assert.AreEqual((byte)0, (await _inventoryRepository.GetUserItemsAsync(5)).Single().SlotIndex);
    }

    private async Task MoveAsync(WorldSession session, InventoryMoveRequestPacket request)
    {
        using var writer = new PacketWriter(request.Opcode);
        request.Serialize(writer);
        byte[] payload = writer.Build()[PacketHeader.HeaderSize..];

        Assert.IsTrue(_registry.TryGetHandler((ushort)ClientOpcode.InventoryMove, out var handler));
        await handler(session, payload, CancellationToken.None);
    }

    private static WorldSession CreatePlayingSession()
    {
        var session = CreateSession();
        session.User = new UserEntity { Id = 5, Username = "bob" };
        session.ActiveCharacter = new CharacterEntity { Id = 9, UserId = 5, Name = "Bobby" };
        return session;
    }

    private async Task DispatchAsync(WorldSession session, ClientOpcode opcode)
    {
        Assert.IsTrue(_registry.TryGetHandler((ushort)opcode, out var handler));
        await handler(session, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
    }

    private static WorldSession CreateSession() =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance));

    private sealed class RecordingSender : ISessionSender
    {
        public List<(IWorldSessionView Session, IPacket Packet)> Sent { get; } = [];

        public ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken)
        {
            Sent.Add((session, packet));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
