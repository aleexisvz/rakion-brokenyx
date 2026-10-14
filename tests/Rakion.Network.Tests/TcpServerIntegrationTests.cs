using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Core.Protocol;
using Rakion.Network.Tcp;

namespace Rakion.Network.Tests;

[TestClass]
public sealed class TcpServerIntegrationTests
{
    private sealed class TestPacketHandler : ITcpPacketHandler
    {
        public TaskCompletionSource<TcpConnection> ConnectedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<(PacketHeader Header, byte[] Payload)> ReceivedPackets { get; } = [];
        public TaskCompletionSource<int> PacketsExpectedTcs { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisconnectedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExpectedPacketCount { get; set; } = 1;

        public ValueTask OnConnectedAsync(TcpConnection connection, CancellationToken cancellationToken)
        {
            ConnectedTcs.TrySetResult(connection);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnPacketReceivedAsync(
            TcpConnection connection,
            PacketHeader header,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            lock (ReceivedPackets)
            {
                ReceivedPackets.Add((header, payload.ToArray()));
                if (ReceivedPackets.Count >= ExpectedPacketCount)
                {
                    PacketsExpectedTcs.TrySetResult(ReceivedPackets.Count);
                }
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask OnDisconnectedAsync(TcpConnection connection, Exception? exception)
        {
            DisconnectedTcs.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    public async Task TcpServer_ClientSendsPacket_ServerReceivesFramedPacketSuccessfully()
    {
        var handler = new TestPacketHandler();
        var server = new TcpServer(new IPEndPoint(IPAddress.Loopback, 0), handler, NullLogger.Instance);
        server.Start();

        try
        {
            var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
            using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await clientSocket.ConnectAsync(serverEndPoint);

            // Wait for server connection callback
            TcpConnection serverConn = await handler.ConnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsNotNull(serverConn);

            // Build a packet using PacketWriter: Opcode 0xCDDF with 4 bytes payload
            using var writer = new PacketWriter((ushort)RakionOpcode.ServerListRequest);
            writer.WriteInt32(123456);
            byte[] packetBytes = writer.Build();

            // Send packet from client
            await clientSocket.SendAsync(packetBytes, SocketFlags.None);

            // Wait for server to receive and frame packet
            await handler.PacketsExpectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.HasCount(1, handler.ReceivedPackets);
            var (header, payload) = handler.ReceivedPackets[0];

            Assert.AreEqual((ushort)RakionOpcode.ServerListRequest, header.Opcode);
            Assert.AreEqual(packetBytes.Length, (int)header.TotalLength);
            Assert.HasCount(4, payload);

            var reader = new PacketReader(payload);
            Assert.AreEqual(123456, reader.ReadInt32());
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [TestMethod]
    public async Task TcpServer_FragmentedAndPipelinedPackets_ReconstructedAccurately()
    {
        var handler = new TestPacketHandler { ExpectedPacketCount = 2 };
        var server = new TcpServer(new IPEndPoint(IPAddress.Loopback, 0), handler, NullLogger.Instance);
        server.Start();

        try
        {
            var serverEndPoint = (IPEndPoint)server.LocalEndPoint!;
            using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await clientSocket.ConnectAsync(serverEndPoint);

            await handler.ConnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Packet 1: 0x1310 (Login), Total length: 4 (header) + 4 (int) = 8 bytes
            using var writer1 = new PacketWriter((ushort)RakionOpcode.Login);
            writer1.WriteInt32(777);
            byte[] packet1 = writer1.Build();

            // Packet 2: 0x0101 (ServerListResponse), Total length: 4 (header) + 2 (short) = 6 bytes
            using var writer2 = new PacketWriter((ushort)RakionOpcode.ServerListResponse);
            writer2.WriteInt16(99);
            byte[] packet2 = writer2.Build();

            // Combine both into one buffer to test pipelining and fragmentation
            byte[] combined = [.. packet1, .. packet2];

            // Fragment: Send first 3 bytes (incomplete header of packet 1)
            await clientSocket.SendAsync(combined.AsMemory(0, 3), SocketFlags.None);
            await Task.Delay(50);

            // Send remainder (which contains rest of packet 1 and entire packet 2)
            await clientSocket.SendAsync(combined.AsMemory(3), SocketFlags.None);

            // Wait for both packets to be received
            await handler.PacketsExpectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.HasCount(2, handler.ReceivedPackets);

            // Verify packet 1
            var (h1, p1) = handler.ReceivedPackets[0];
            Assert.AreEqual((ushort)RakionOpcode.Login, h1.Opcode);
            var r1 = new PacketReader(p1);
            Assert.AreEqual(777, r1.ReadInt32());

            // Verify packet 2
            var (h2, p2) = handler.ReceivedPackets[1];
            Assert.AreEqual((ushort)RakionOpcode.ServerListResponse, h2.Opcode);
            var r2 = new PacketReader(p2);
            Assert.AreEqual((short)99, r2.ReadInt16());
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
