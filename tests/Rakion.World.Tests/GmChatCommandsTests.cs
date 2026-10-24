using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Chat;
using Rakion.World.Features.Admin;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class GmChatCommandsTests
{
    [TestMethod]
    public async Task OnlyGmsRunCommands_AndHelpIsAnswered()
    {
        var sender = new RecordingSender();
        var commands = new GmChatCommands(sender, null!, null!, null!, null!, NullLogger<GmChatCommands>.Instance);

        Assert.IsFalse(await commands.TryHandleAsync(ContextFor(authority: 0), "/gm", CancellationToken.None), "a player's /gm stays chat");
        Assert.IsFalse(await commands.TryHandleAsync(ContextFor(authority: GmPolicy.GmAuthority), "hola", CancellationToken.None));
        Assert.IsFalse(await commands.TryHandleAsync(ContextFor(authority: GmPolicy.GmAuthority), "/unknown", CancellationToken.None));
        Assert.IsTrue(await commands.TryHandleAsync(ContextFor(authority: GmPolicy.GmAuthority), "/gm", CancellationToken.None));
        CollectionAssert.AreEqual(GmChatCommands.Help.Select(line => new SystemNoticePacket(line)).ToArray(), sender.Sent.ToArray());
        Assert.IsTrue(GmChatCommands.Help.All(line => line.Length <= AdminNoticeRequestPacket.MaxTextLength));
    }

    [TestMethod]
    public async Task KickOut_ReportsAnOfflineCharacter()
    {
        var sender = new RecordingSender();
        var commands = new GmChatCommands(sender, null!, null!, null!, null!, NullLogger<GmChatCommands>.Instance);

        Assert.IsTrue(await commands.TryHandleAsync(ContextFor(authority: GmPolicy.GmAuthority), "/kickout Nadie", CancellationToken.None));
        Assert.AreEqual(new SystemNoticePacket("'Nadie' no está conectado."), sender.Sent.Single());
    }

    private static ChatCommandContext ContextFor(int authority) =>
        new(
            new WorldSession(new TcpConnection(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null!, NullLogger.Instance))
            {
                User = new UserEntity { Id = 1, Username = "gm", Authority = authority },
            },
            _ => null,
            (_, _) => ValueTask.CompletedTask,
            (_, _) => ValueTask.FromResult(false));

    private sealed class RecordingSender : ISessionSender
    {
        public List<IPacket> Sent { get; } = [];

        public ValueTask SendAsync(IWorldSessionView session, IPacket packet, CancellationToken cancellationToken)
        {
            Sent.Add(packet);
            return ValueTask.CompletedTask;
        }
    }
}
