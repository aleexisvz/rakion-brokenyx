using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Channels;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class WorldSessionViewTests
{
    [TestMethod]
    public void View_BeforeLogin_ExposesNoAccountState()
    {
        IWorldSessionView view = CreateSession();

        Assert.IsFalse(view.IsAuthenticated);
        Assert.IsNull(view.UserId);
        Assert.IsNull(view.Username);
        Assert.IsNull(view.CharacterId);
        Assert.IsNull(view.CharacterName);
        Assert.IsNull(view.ChannelIndex);
        Assert.IsFalse(view.IsInField);
    }

    [TestMethod]
    public void View_AfterSelectAndChannelEnter_ReflectsSessionState()
    {
        var session = CreateSession();
        session.User = new UserEntity { Id = 7, Username = "alice" };
        session.ActiveCharacter = new CharacterEntity { Id = 42, UserId = 7, Name = "Alicia" };
        session.ActiveChannel = new LobbyChannel(2, "Clan 01", ChannelType.Clan, 99, 100);

        IWorldSessionView view = session;

        Assert.IsTrue(view.IsAuthenticated);
        Assert.AreEqual(7, view.UserId);
        Assert.AreEqual("alice", view.Username);
        Assert.AreEqual(42, view.CharacterId);
        Assert.AreEqual("Alicia", view.CharacterName);
        Assert.AreEqual((byte)2, view.ChannelIndex);
        Assert.AreEqual(session.Connection.ConnectionId, view.ConnectionId);
    }

    [TestMethod]
    public async Task Sender_RejectsViewNotCreatedByWorld()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await WorldSessionSender.Instance.SendAsync(new ForeignView(), new ChannelExitResponsePacket(), CancellationToken.None));
    }

    private static WorldSession CreateSession() =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance));

    private sealed class ForeignView : IWorldSessionView
    {
        public Guid ConnectionId => Guid.Empty;
        public bool IsAuthenticated => false;
        public int? UserId => null;
        public string? Username => null;
        public int? CharacterId => null;
        public string? CharacterName => null;
        public byte? ChannelIndex => null;
        public bool IsInField => false;
    }
}
