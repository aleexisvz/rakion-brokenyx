using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Packets;
using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Fields;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class GameFieldJoinTests
{
    [TestMethod]
    public void Rules_FollowLegacyRoomFields()
    {
        var create = new FieldCreateRequestPacket("room", string.Empty, "map", 0, 1, 5, 600, 20, 1, 99, 0);

        var rules = FieldRules.FromCreate(create);
        var changed = rules.With(new FieldChangeRuleRequestPacket("room", string.Empty, "map2", 0, 6, 900, 30, 3));

        Assert.AreEqual((byte)1, rules.Mode);
        Assert.AreEqual((ushort)600, rules.TimeLimit);
        Assert.AreEqual((byte)99, rules.MaxLevel);
        Assert.AreEqual((byte)3, changed.Mode);
        Assert.AreEqual((ushort)900, changed.TimeLimit);
        Assert.AreEqual("map2", changed.MapName);
        Assert.AreEqual((byte)99, changed.MaxLevel, "the rule change keeps the level range");
    }

    [TestMethod]
    public void Join_UsesLegacyResultCodes()
    {
        var field = CreateField(new FieldRules(0, 1, 0, 600, 20, 10, 20, 0, "map"), password: "pw");

        Assert.IsFalse(field.TryAddPlayer(CreateSession(2, level: 5), "pw", out _, out byte lowLevel));
        Assert.IsFalse(field.TryAddPlayer(CreateSession(3, level: 15), "bad", out _, out byte badPassword));
        Assert.IsTrue(field.TryAddPlayer(CreateSession(4, level: 15), "pw", out var guest, out _));
        guest!.IsReady = true;
        Assert.AreEqual(FieldGameStartResponsePacket.Started, field.TryStartGame(field.Players.Single(p => p.IsMaster)));
        Assert.IsFalse(field.TryAddPlayer(CreateSession(5, level: 15), "pw", out _, out byte running));

        Assert.AreEqual(FieldPlayerEnterPacket.LevelOutOfRange, lowLevel);
        Assert.AreEqual(FieldPlayerEnterPacket.WrongPassword, badPassword);
        Assert.AreEqual(FieldPlayerEnterPacket.InProgress, running);
        Assert.AreEqual((byte)10, guest!.SlotIndex);
    }

    [TestMethod]
    public void StageRoom_OpensTheStagePlayerCount_AndChecksItsLevelRange()
    {
        // Stage 19: 4 players, levels 10..21 (legacy StageInfo).
        var field = CreateField(new FieldRules(19, 0, 0, 0, 0, 0, 0, 0, "map"), password: string.Empty);
        field.ApplyDefaultSlots();

        var slots = field.SnapshotSlots();
        Assert.AreEqual(4, slots.Count(s => s.State != FieldSlotInfo.Closed), "the host and three open slots");

        Assert.IsFalse(field.TryAddPlayer(CreateSession(2, level: 9), string.Empty, out _, out byte tooLow));
        Assert.IsFalse(field.TryAddPlayer(CreateSession(3, level: 22), string.Empty, out _, out byte tooHigh));
        Assert.AreEqual(FieldPlayerEnterPacket.LevelOutOfRange, tooLow);
        Assert.AreEqual(FieldPlayerEnterPacket.LevelOutOfRange, tooHigh);

        var withPass = CreateSession(4, level: 40);
        withPass.StageLevelFreeUntilMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 + 60;
        Assert.IsTrue(field.TryAddPlayer(withPass, string.Empty, out _, out _), "the level pass skips the range");

        var expired = CreateSession(5, level: 40);
        expired.StageLevelFreeUntilMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 - 1;
        Assert.IsFalse(field.TryAddPlayer(expired, string.Empty, out _, out _));
    }

    [TestMethod]
    public void StageRankClearItem_FollowsTheLegacyLevelBands()
    {
        Assert.IsNull(Rakion.World.Features.Economy.AccountUpgradeModule.StageRankClearItem(9), "free below level 10");
        Assert.AreEqual((10011, 2900), Rakion.World.Features.Economy.AccountUpgradeModule.StageRankClearItem(10));
        Assert.AreEqual((10011, 2900), Rakion.World.Features.Economy.AccountUpgradeModule.StageRankClearItem(20));
        Assert.AreEqual((10012, 6400), Rakion.World.Features.Economy.AccountUpgradeModule.StageRankClearItem(21));
        Assert.AreEqual((10013, 9900), Rakion.World.Features.Economy.AccountUpgradeModule.StageRankClearItem(41));
    }

    [TestMethod]
    public void StageCatalog_HoldsTheLegacyTable()
    {
        Assert.AreEqual(new StageLimits(1, 1, 10), StageCatalog.Get(1));
        Assert.AreEqual(new StageLimits(3, 24, 35), StageCatalog.Get(48));
        Assert.IsNull(StageCatalog.Get(0));
        Assert.IsNull(StageCatalog.Get(49));
    }

    [TestMethod]
    public void SnapshotSlots_ReportsWireStates()
    {
        var field = CreateField(FieldRules.Default, password: string.Empty);
        Assert.IsTrue(field.TryAddPlayer(CreateSession(2, level: 1), string.Empty, out var guest, out _));
        guest!.IsReady = true;

        var slots = field.SnapshotSlots();

        Assert.HasCount(20, slots);
        Assert.AreEqual(FieldSlotInfo.Waiting, slots[0].State, "the host shows as waiting");
        Assert.AreEqual(FieldSlotInfo.Ready, slots[guest.SlotIndex].State);
        Assert.AreEqual(0, field.MasterSlot);
    }

    private static GameField CreateField(FieldRules rules, string password)
    {
        var field = new GameField(1, 0, "room", password, 0, 0, 20, 10, 1, 0, 0, CreateSession(1, level: 15)) { Rules = rules };
        return field;
    }

    private static WorldSession CreateSession(int id, byte level) =>
        new(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = id, Username = $"user{id}" },
            ActiveCharacter = new CharacterEntity { Id = id, UserId = id, Name = $"Char{id}", Level = level },
        };
}
