using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Packets;
using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Fields;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class GameFieldRoundTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void MarkLoaded_StartsTheRoundWhenEveryoneLoaded()
    {
        var (field, host, guest) = CreateRunningMatch(mode: 1, timeLimit: 300);

        Assert.IsNull(field.MarkLoaded(host, Now), "waits for the guest");
        var start = field.MarkLoaded(guest, Now);

        Assert.IsNotNull(start);
        Assert.IsTrue(start.Value.ToAllLoaded);
        Assert.AreEqual(new FieldGameRoundStartPacket(1, 303, 0, 0, FieldGameRoundStartPacket.NoLeader, FieldGameRoundStartPacket.NoLeader), start.Value.Packet);
        Assert.HasCount(2, field.LoadedPlayers);
        Assert.IsNull(field.MarkLoaded(guest, Now), "a second report is ignored");
    }

    [TestMethod]
    public void MarkLoaded_LateJoinerGetsTheRunningRoundOnly()
    {
        var (field, host, guest) = CreateRunningMatch(mode: 1, timeLimit: 300);
        field.MarkLoaded(host, Now);
        field.MarkLoaded(guest, Now);
        field.ExitGame(guest);
        field.TryEnterGame(guest);

        var start = field.MarkLoaded(guest, Now.AddSeconds(100));

        Assert.IsFalse(start!.Value.ToAllLoaded);
        Assert.AreEqual((ushort)203, start.Value.Packet.SecondsLeft);
    }

    [TestMethod]
    public void MarkLoaded_LeaderWarsPicksTeamLeaders()
    {
        var (field, host, guest) = CreateRunningMatch(mode: 4, timeLimit: 300);
        field.MarkLoaded(host, Now);

        var start = field.MarkLoaded(guest, Now)!.Value.Packet;

        Assert.AreEqual(host.SlotIndex, start.RedLeaderSlot);
        Assert.AreEqual(guest.SlotIndex, start.BlueLeaderSlot);
    }

    [TestMethod]
    public void ApplyDefaultSlots_PvpKeepsSixPerTeam()
    {
        var field = new GameField(1, 0, "room", string.Empty, 0, 0, 1, 0, 0, 0, 0, CreateSession(1))
        {
            Rules = FieldRules.Default with { Mode = 1 },
        };

        field.ApplyDefaultSlots();
        var slots = field.SnapshotSlots();

        Assert.AreEqual(12, slots.Count(s => s.State != FieldSlotInfo.Closed));
        Assert.AreEqual(FieldSlotInfo.Closed, slots[6].State);
        Assert.AreEqual(FieldSlotInfo.Empty, slots[15].State);
        Assert.AreEqual(FieldSlotInfo.Closed, slots[16].State);
    }

    [TestMethod]
    public void AddPlayerRequest_ParsesBothForms_AndRejectsOversizedData()
    {
        Assert.IsTrue(FieldGameAddPlayerRequestPacket.TryParse([3, 0, 1, 2, 3], hasTarget: false, out var all));
        Assert.IsNull(all.TargetSlot);
        Assert.AreEqual(3, all.Data.Length);

        Assert.IsTrue(FieldGameAddPlayerRequestPacket.TryParse([10, 1, 0, 9], hasTarget: true, out var one));
        Assert.AreEqual((byte)10, one.TargetSlot);

        Assert.IsFalse(FieldGameAddPlayerRequestPacket.TryParse([201, 0], hasTarget: false, out _));
        Assert.IsFalse(FieldGameAddPlayerRequestPacket.TryParse([20, 0, 0], hasTarget: true, out _));
    }

    private static (GameField Field, FieldPlayer Host, FieldPlayer Guest) CreateRunningMatch(byte mode, ushort timeLimit)
    {
        var hostSession = CreateSession(1, level: 20);
        var field = new GameField(1, 0, "room", string.Empty, 0, 0, mode, 0, 0, 0, 0, hostSession)
        {
            Rules = FieldRules.Default with { Mode = mode, TimeLimit = timeLimit },
        };
        Assert.IsTrue(field.TryGetPlayer(hostSession.Connection.ConnectionId, out var host));
        Assert.IsTrue(field.TryAddPlayer(CreateSession(2, level: 10), string.Empty, out var guest, out _));
        guest!.IsReady = true;
        Assert.AreEqual(FieldGameStartResponsePacket.Started, field.TryStartGame(host!));
        return (field, host!, guest);
    }

    private static WorldSession CreateSession(int id, byte level = 1) =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = id, Username = $"user{id}" },
            ActiveCharacter = new CharacterEntity { Id = id, UserId = id, Name = $"Char{id}", Level = level },
        };
}
