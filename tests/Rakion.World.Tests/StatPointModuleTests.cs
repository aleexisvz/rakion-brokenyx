using System.Net.Sockets;
using Dapper;
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
public sealed class StatPointModuleTests
{
    private SqliteTestConnectionFactory _dbFactory = null!;
    private CharacterRepository _characterRepository = null!;
    private RecordingSender _sender = null!;
    private WorldOpcodeRegistry _registry = null!;
    private int _userId;
    private int _characterId;

    [TestInitialize]
    public async Task Setup()
    {
        _dbFactory = new SqliteTestConnectionFactory($"Data Source=world_stat_test_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _characterRepository = new CharacterRepository(_dbFactory);
        _sender = new RecordingSender();
        _registry = new WorldOpcodeRegistry();
        new StatPointModule(_sender, _characterRepository, NullLogger<StatPointModule>.Instance).Register(_registry);

        _userId = await new UserRepository(_dbFactory).CreateUserAsync(new UserEntity { Username = "pointer", PasswordHash = "x", Email = "p@test" });
        _characterId = (await _characterRepository.CreateCharacterAsync(_userId, "Pointy", CharacterClass.Mage, 0)).Id;
        await using var connection = await _dbFactory.CreateConnectionAsync();
        await connection.ExecuteAsync("UPDATE characters SET level_point = 1 WHERE id = @Id", new { Id = _characterId });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _dbFactory.Dispose();
    }

    [TestMethod]
    public async Task Allocate_ThenOutOfPoints()
    {
        var first = await AllocateAsync(CharacterStat.Speed);
        var second = await AllocateAsync(CharacterStat.Speed);

        Assert.AreEqual(new InventoryAllocationPointResponsePacket(0, 0, 0, CharacterStat.Speed, 1), first);
        Assert.AreEqual(InventoryAllocationPointResponsePacket.ResultNoPoints, second.Result);
        Assert.AreEqual((byte)1, (await _characterRepository.GetCharacterByIdAsync(_characterId))!.Speed);
    }

    [TestMethod]
    public async Task Allocate_UnknownStat_IsRejected()
    {
        var response = await AllocateAsync((CharacterStat)10);

        Assert.AreEqual(InventoryAllocationPointResponsePacket.ResultUnknownStat, response.Result);
        Assert.AreEqual((byte)1, (await _characterRepository.GetCharacterByIdAsync(_characterId))!.LevelPoint);
    }

    private async Task<InventoryAllocationPointResponsePacket> AllocateAsync(CharacterStat stat)
    {
        var session = new WorldSession(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
        {
            User = new UserEntity { Id = _userId, Username = "pointer" },
            ActiveCharacter = new CharacterEntity { Id = _characterId, UserId = _userId, Name = "Pointy" },
        };

        Assert.IsTrue(_registry.TryGetHandler((ushort)ClientOpcode.InventoryAllocationPoint, out var handler));
        await handler(session, new[] { (byte)stat }, CancellationToken.None);
        return (InventoryAllocationPointResponsePacket)_sender.Sent.Last().Packet;
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
