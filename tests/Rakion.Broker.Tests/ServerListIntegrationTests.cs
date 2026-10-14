using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;
using Rakion.Broker.Handlers;
using Rakion.Broker.Hosting;
using Rakion.Broker.Services;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Repositories;
using Rakion.Database.Security;
using Rakion.Database.Tests.TestUtilities;

namespace Rakion.Broker.Tests;

[TestClass]
public sealed class ServerListIntegrationTests
{
    [TestMethod]
    public async Task BrokerServer_Receives0xCDDF_RespondsWith0x0101ServerList()
    {
        using var dbFactory = new SqliteTestConnectionFactory("Data Source=broker_serverlist_test;Mode=Memory;Cache=Shared");
        var userRepo = new UserRepository(dbFactory);
        var hasher = new PasswordHasher();
        var ticketService = new SessionTicketService();

        var brokerOptions = Options.Create(new BrokerOptions
        {
            ListenIp = "127.0.0.1",
            Port = 0, // Random available port
            GameServers =
            [
                new GameServerConfig
                {
                    Id = 1,
                    Name = "WorldServer 1",
                    Ip = "192.168.1.5",
                    Port = 40708,
                    MaxSlots = 1000,
                    MaxSalas = 2000,
                    IsOnline = true
                },
                new GameServerConfig
                {
                    Id = 2,
                    Name = "WorldServer 2",
                    Ip = "192.168.1.6",
                    Port = 40708,
                    MaxSlots = 500,
                    MaxSalas = 1000,
                    IsOnline = false
                }
            ]
        });

        var handler = new BrokerPacketHandler(brokerOptions, userRepo, hasher, ticketService, NullLogger<BrokerPacketHandler>.Instance);
        await using var server = new BrokerServer(brokerOptions, handler, NullLogger<BrokerServer>.Instance);
        server.Start();

        var endPoint = (IPEndPoint)server.LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port);
        NetworkStream stream = client.GetStream();

        // Send ServerListRequest (0xCDDF)
        var request = new ServerListRequestPacket(ClientVersion: 1026);
        using var writer = new PacketWriter(request.Opcode);
        request.Serialize(writer);
        byte[] requestBytes = writer.Build();
        await stream.WriteAsync(requestBytes);
        await stream.FlushAsync();

        // Read response header (4 bytes)
        byte[] headerBuffer = new byte[PacketHeader.HeaderSize];
        await ReadExactAsync(stream, headerBuffer);
        Assert.IsTrue(PacketHeader.TryParse(headerBuffer, out var responseHeader));

        Assert.AreEqual((ushort)RakionOpcode.ServerListResponse, responseHeader.Opcode);
        Assert.IsGreaterThan((int)PacketHeader.HeaderSize, responseHeader.TotalLength);

        // Read response payload
        byte[] payloadBuffer = new byte[responseHeader.PayloadLength];
        await ReadExactAsync(stream, payloadBuffer);

        Assert.IsTrue(ServerListResponsePacket.TryParse(payloadBuffer, out var responsePacket));
        Assert.HasCount(2, responsePacket.Servers);

        var s1 = responsePacket.Servers[0];
        Assert.AreEqual(IPAddress.Parse("192.168.1.5"), s1.IpAddress);
        Assert.AreEqual((ushort)40708, s1.Port);
        Assert.AreEqual((ushort)1000, s1.MaxSlots);
        Assert.AreEqual((ushort)2000, s1.MaxSalas);
        Assert.IsTrue(s1.IsOnline);

        var s2 = responsePacket.Servers[1];
        Assert.IsFalse(s2.IsOnline);

        await server.StopAsync();
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException("Socket reached end of stream before receiving expected byte count.");
            }
            totalRead += read;
        }
    }
}
