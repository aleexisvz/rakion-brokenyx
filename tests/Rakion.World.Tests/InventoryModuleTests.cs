using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Core.Utilities;
using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Features.Economy;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class InventoryModuleTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 22, 15, 30);

    private RecordingSender _sender = null!;
    private WorldOpcodeRegistry _registry = null!;

    [TestInitialize]
    public void Setup()
    {
        _sender = new RecordingSender();
        _registry = new WorldOpcodeRegistry();
        new InventoryModule(_sender, new FixedTimeProvider(Now), NullLogger<InventoryModule>.Instance).Register(_registry);
    }

    [TestMethod]
    public void Register_BindsInventoryEnterAndLeave()
    {
        Assert.IsTrue(_registry.IsMapped(ClientOpcode.InventoryEnter));
        Assert.IsTrue(_registry.IsMapped(ClientOpcode.InventoryLeave));
        Assert.AreEqual(2, _registry.Count);
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
