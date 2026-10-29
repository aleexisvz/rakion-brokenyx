using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Entities;
using Rakion.Database.Repositories;
using Rakion.Database.Tests.TestUtilities;
using Rakion.Network.Tcp;
using Rakion.World.Features.Economy;
using Rakion.World.Modules;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class BlacksmithModuleTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private InventoryRepository _inventoryRepository = null!;
    private RecordingSender _sender = null!;
    private int _userId;
    private int _nextRoll;

    [TestInitialize]
    public async Task Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_blacksmith_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _inventoryRepository = new InventoryRepository(_dbFactory);
        _sender = new RecordingSender();
        _userId = await new UserRepository(_dbFactory).CreateUserAsync(new UserEntity { Username = "smith", PasswordHash = "x", Email = "s@test" });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task Reinforce_Success_RaisesLevelAndConsumesStone()
    {
        await AddAsync(1001, slot: 0, level: 2);
        await AddAsync(13002, slot: 1);

        var response = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 1, []));

        Assert.AreEqual(new EnchantReinforceResponsePacket(EnchantReinforceResponsePacket.LevelUp, 0, 1, []), response);
        var item = (await _inventoryRepository.GetUserItemsAsync(_userId)).Single();
        Assert.AreEqual((byte)3, item.Level);
    }

    [TestMethod]
    public async Task Reinforce_RolledDowngrade_LowersLevel()
    {
        await AddAsync(1001, slot: 0, level: 8);
        await AddAsync(13002, slot: 1);
        var odds = new EnchantOdds(0, 0, 100, 0, 0);

        var response = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 1, []), _ => odds);

        Assert.AreEqual(EnchantReinforceResponsePacket.LevelDown1, response.Result);
        Assert.AreEqual((byte)7, (await _inventoryRepository.GetUserItemsAsync(_userId)).Single().Level);
    }

    [TestMethod]
    public async Task Reinforce_Refusals_ConsumeNothing()
    {
        await AddAsync(1001, slot: 0, level: 6);
        await AddAsync(13001, slot: 1);     // low-level stone, item already +6
        await AddAsync(8001, slot: 2);      // not equipment
        await AddAsync(1002, slot: 3);      // not a stone
        await AddAsync(13002, slot: 4);

        var wrongStone = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 1, []));
        var notEquipment = await ReinforceAsync(new EnchantReinforceRequestPacket(2, 1, []));
        var notAStone = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 3, []));
        var withMaterial = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 4, [3]));

        Assert.AreEqual(EnchantReinforceResponsePacket.StoneNotForThisLevel, wrongStone.Result);
        Assert.AreEqual(EnchantReinforceResponsePacket.NotReinforceable, notEquipment.Result);
        Assert.AreEqual(EnchantReinforceResponsePacket.InvalidStoneOrMaterial, notAStone.Result);
        Assert.AreEqual(EnchantReinforceResponsePacket.InvalidStoneOrMaterial, withMaterial.Result);
        Assert.HasCount(5, await _inventoryRepository.GetUserItemsAsync(_userId));
    }

    [TestMethod]
    public async Task Reinforce_WithCatalysts_UsesTheirCounts_AndConsumesThem()
    {
        await AddAsync(1001, slot: 0, level: 5);
        await AddAsync(13002, slot: 1);
        await AddAsync(14003, slot: 2);
        await AddAsync(14003, slot: 3);
        await AddAsync(14001, slot: 4);
        EnchantAttempt? seen = null;

        var response = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 1, [2, 3, 4]), attempt =>
        {
            seen = attempt;
            return EnchantOdds.Legacy(attempt.Level, attempt.StoneId, attempt.DestroyGuards, attempt.DownGuards, attempt.Charms);
        });

        Assert.AreEqual(new EnchantAttempt(5, 13002, DestroyGuards: 1, DownGuards: 0, Charms: 2), seen);
        Assert.AreEqual(EnchantReinforceResponsePacket.LevelUp, response.Result);
        var left = await _inventoryRepository.GetUserItemsAsync(_userId);
        Assert.AreEqual((byte)6, left.Single().Level, "stone and the three catalysts are consumed");
    }

    [TestMethod]
    public void LegacyOdds_FollowTheLegacyFormula()
    {
        // Level 0: the stone's base chance; each charm blends in 5 % of sure success.
        Assert.AreEqual(700_000, EnchantOdds.Legacy(0, 13001).LevelUp);
        Assert.AreEqual(857_500, EnchantOdds.Legacy(0, 13002, charms: 1).LevelUp);

        // Each level i multiplies by (1 - i * decay): 13002 loses 6 % per level step.
        Assert.AreEqual((int)Math.Round(0.85 * 0.94 * 0.88 * 1_000_000), EnchantOdds.Legacy(2, 13002).LevelUp);

        for (int level = 0; level < BlacksmithModule.MaxLevel; level++)
        {
            Assert.AreEqual(EnchantOdds.Scale, EnchantOdds.Legacy(level, 13003).Total, 2, $"level {level} sums to one");
        }

        // Destruction is never rolled: at +3 or below it becomes -3, above it becomes "no change".
        var low = EnchantOdds.Legacy(3, 13002);
        var high = EnchantOdds.Legacy(10, 13002);
        Assert.AreEqual(Fifteenths(low, 2), low.Down3, 2);
        Assert.AreEqual(Fifteenths(high, 1), high.Down3, 2);

        // 14001 moves the destroyed share to "no change": it only matters at +3 or below, where destruction would be -3.
        Assert.IsLessThan(EnchantOdds.Legacy(3, 13002).Down3, EnchantOdds.Legacy(3, 13002, destroyGuards: 3).Down3);
        Assert.AreEqual(EnchantOdds.Legacy(10, 13002), EnchantOdds.Legacy(10, 13002, destroyGuards: 3));

        // 14002 grows the -1..-3 shares, as the legacy formula does.
        Assert.IsGreaterThan(EnchantOdds.Legacy(10, 13002).Down1, EnchantOdds.Legacy(10, 13002, downGuards: 1).Down1);

        static double Fifteenths(EnchantOdds odds, int fifteenths) => (EnchantOdds.Scale - odds.LevelUp) * fifteenths / 15.0;
    }

    private Task AddAsync(int itemId, byte slot, byte level = 1) =>
        _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = _userId, CharacterId = 0, ItemId = itemId, SlotIndex = slot, Level = level });

    private async Task<EnchantReinforceResponsePacket> ReinforceAsync(EnchantReinforceRequestPacket request, Func<EnchantAttempt, EnchantOdds>? odds = null)
    {
        var registry = new WorldOpcodeRegistry();
        odds ??= attempt => EnchantOdds.Legacy(attempt.Level, attempt.StoneId, attempt.DestroyGuards, attempt.DownGuards, attempt.Charms);
        new BlacksmithModule(_sender, _inventoryRepository, odds, _ => _nextRoll, NullLogger<BlacksmithModule>.Instance)
            .Register(registry);
        var session = new WorldSession(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = _userId, Username = "smith" },
            ActiveCharacter = new CharacterEntity { Id = 9, UserId = _userId, Name = "Smithy" },
        };

        using var writer = new PacketWriter(request.Opcode);
        request.Serialize(writer);
        Assert.IsTrue(registry.TryGetHandler((ushort)ClientOpcode.EnchantReinforce, out var handler));
        await handler(session, writer.Build()[PacketHeader.HeaderSize..], CancellationToken.None);
        return (EnchantReinforceResponsePacket)_sender.Sent.Last().Packet;
    }

    [TestMethod]
    public void WithDowngradeScale_MovesTheLostChanceToNoChange()
    {
        var odds = new EnchantOdds(42, 29, 29, 0, 0).WithDowngradeScale(0.7);

        Assert.AreEqual(new EnchantOdds(42, 38, 20, 0, 0), odds);
        Assert.AreEqual(100, odds.Total);
        Assert.AreEqual(EnchantOdds.Legacy(12, 13002), EnchantOdds.Legacy(12, 13002).WithDowngradeScale(1));
    }

    private sealed class RecordingSender : ISessionSender
    {
        public List<(IWorldSessionView Session, IPacket Packet)> Sent { get; } = [];

        public ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken)
        {
            Sent.Add((session, packet));
            return ValueTask.CompletedTask;
        }
    }
}
