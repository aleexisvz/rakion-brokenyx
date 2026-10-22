using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Packets;
using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Fields;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class GameFieldMatchTests
{
    [TestMethod]
    public void StartGame_FreeMode_HostAlone_StartsAndMarksPlayersInGame()
    {
        var (field, host) = CreateField(mode: 0);

        Assert.AreEqual(FieldGameStartResponsePacket.Started, field.TryStartGame(host));
        Assert.IsTrue(field.IsInProgress);
        Assert.IsTrue(host.IsInGame);
        Assert.IsNull(field.TryStartGame(host), "a running match ignores another start");
    }

    [TestMethod]
    public void StartGame_TeamMode_NeedsBalancedTeams()
    {
        var (field, host) = CreateField(mode: 1);

        Assert.AreEqual(FieldGameStartResponsePacket.TeamsNotBalanced, field.TryStartGame(host));
        Assert.IsFalse(field.IsInProgress);
    }

    [TestMethod]
    public void StartGame_NeedsEveryGuestReady_AndOnlyTheHost()
    {
        var (field, host) = CreateField(mode: 0);
        Assert.IsTrue(field.TryAddPlayer(CreateSession(2), string.Empty, out var guest, out _));

        Assert.IsNull(field.TryStartGame(guest!));
        Assert.AreEqual(FieldGameStartResponsePacket.NotAllReady, field.TryStartGame(host));

        guest!.IsReady = true;
        Assert.AreEqual(FieldGameStartResponsePacket.Started, field.TryStartGame(host));
        Assert.IsTrue(guest.IsInGame);
    }

    [TestMethod]
    public void ExitGame_LastPlayerOut_EndsTheMatch()
    {
        var (field, host) = CreateField(mode: 0);
        Assert.IsTrue(field.TryAddPlayer(CreateSession(2), string.Empty, out var guest, out _));
        guest!.IsReady = true;
        field.TryStartGame(host);

        Assert.IsFalse(field.ExitGame(guest));
        Assert.IsFalse(guest.IsReady);
        Assert.AreEqual(FieldGameEnterResponsePacket.Entered, field.TryEnterGame(guest));
        Assert.IsFalse(field.ExitGame(host));
        Assert.IsTrue(field.ExitGame(guest));

        field.EndGame();
        Assert.IsFalse(field.IsInProgress);
        Assert.AreEqual(FieldGameEnterResponsePacket.NotRunning, field.TryEnterGame(guest));
    }

    private static (GameField Field, FieldPlayer Host) CreateField(byte mode)
    {
        var session = CreateSession(1);
        var field = new GameField(1, 0, "room", string.Empty, mode, 0, 8, 10, 1, 0, 0, session);
        Assert.IsTrue(field.TryGetPlayer(session.Connection.ConnectionId, out var host));
        return (field, host!);
    }

    private static WorldSession CreateSession(int id) =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = id, Username = $"user{id}" },
            ActiveCharacter = new CharacterEntity { Id = id, UserId = id, Name = $"Char{id}" },
        };
}
