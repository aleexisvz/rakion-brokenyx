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

        var wrongStone = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 1, []));
        var notEquipment = await ReinforceAsync(new EnchantReinforceRequestPacket(2, 1, []));
        var notAStone = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 3, []));
        var withMaterial = await ReinforceAsync(new EnchantReinforceRequestPacket(0, 1, [3]));

        Assert.AreEqual(EnchantReinforceResponsePacket.StoneNotForThisLevel, wrongStone.Result);
        Assert.AreEqual(EnchantReinforceResponsePacket.NotReinforceable, notEquipment.Result);
        Assert.AreEqual(EnchantReinforceResponsePacket.InvalidStoneOrMaterial, notAStone.Result);
        Assert.AreEqual(EnchantReinforceResponsePacket.InvalidStoneOrMaterial, withMaterial.Result);
        Assert.HasCount(4, await _inventoryRepository.GetUserItemsAsync(_userId));
    }

    [TestMethod]
    public void DefaultOdds_AlwaysSucceedUpToLevel3_AndSumTo100()
    {
        Assert.AreEqual(100, EnchantOdds.DefaultFor(3).LevelUp);
        for (int level = 0; level < BlacksmithModule.MaxLevel; level++)
        {
            Assert.AreEqual(100, EnchantOdds.DefaultFor(level).Total, $"level {level}");
        }
    }

    private Task AddAsync(int itemId, byte slot, byte level = 1) =>
        _inventoryRepository.AddUserItemAsync(new UserItemEntity { UserId = _userId, CharacterId = 0, ItemId = itemId, SlotIndex = slot, Level = level });

    private async Task<EnchantReinforceResponsePacket> ReinforceAsync(EnchantReinforceRequestPacket request, Func<int, EnchantOdds>? odds = null)
    {
        var registry = new WorldOpcodeRegistry();
        new BlacksmithModule(_sender, _inventoryRepository, odds ?? EnchantOdds.DefaultFor, _ => _nextRoll, NullLogger<BlacksmithModule>.Instance)
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
