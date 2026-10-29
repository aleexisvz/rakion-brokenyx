using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Rakion.Core.Enums;
using Rakion.Network.Tcp;
using Rakion.World.Channels;
using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class LobbyChannelListIdTests
{
    private readonly List<Socket> _sockets = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var socket in _sockets)
        {
            socket.Dispose();
        }
    }

    [TestMethod]
    public void ListIds_AreUniqueInTheChannel_WhatEverTheServerWideIndex()
    {
        // 300 connections on the server: their UDP indexes wrap past a byte (5 and 261 share the low byte),
        // but the channel hands out its own ids.
        var channel = new LobbyChannel(0, "Free 01", ChannelType.Free, 99, 255);
        var first = NewSession(udpIndex: 5);
        var wrapped = NewSession(udpIndex: 261);

        Assert.IsTrue(channel.TryAdd(first));
        Assert.IsTrue(channel.TryAdd(wrapped));
        Assert.AreNotEqual(first.ChannelListId, wrapped.ChannelListId);

        var ids = new HashSet<byte> { first.ChannelListId, wrapped.ChannelListId };
        for (int i = 2; i < 255; i++)
        {
            var session = NewSession((ushort)(1000 + i));
            Assert.IsTrue(channel.TryAdd(session));
            Assert.IsTrue(ids.Add(session.ChannelListId), $"id {session.ChannelListId} given twice");
        }

        Assert.DoesNotContain((byte)0, ids, "0 is the own-exit answer of 0x20");
        Assert.IsFalse(channel.TryAdd(NewSession(9999)), "255 ids taken: the channel is full");
    }

    [TestMethod]
    public void FreedIds_AreNotReusedAtOnce()
    {
        var channel = new LobbyChannel(0, "Free 01", ChannelType.Free, 99, 255);
        var leaver = NewSession(1);
        Assert.IsTrue(channel.TryAdd(leaver));
        byte freed = leaver.ChannelListId;

        Assert.IsTrue(channel.Remove(leaver));
        Assert.AreEqual(freed, leaver.ChannelListId, "kept so the others can still be told to drop it");

        var newcomer = NewSession(2);
        Assert.IsTrue(channel.TryAdd(newcomer));
        Assert.AreNotEqual(freed, newcomer.ChannelListId, "a late 0x20 for the leaver must not remove the newcomer");
    }

    private WorldSession NewSession(ushort udpIndex)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _sockets.Add(socket);
        return new WorldSession(new TcpConnection(socket, null!, NullLogger.Instance)) { UdpIndex = udpIndex };
    }
}
