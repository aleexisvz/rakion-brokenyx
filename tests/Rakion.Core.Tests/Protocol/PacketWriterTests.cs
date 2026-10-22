using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Protocol;

[TestClass]
public sealed class PacketWriterTests
{
    [TestMethod]
    public void Build_WithOpcode_AutomaticallyPrefixesCorrectHeader()
    {
        using var writer = new PacketWriter((ushort)ServerOpcode.ServerListResponse);
        writer.WriteByte(1); // 1 server
        writer.WriteUInt16(40708); // port

        byte[] packet = writer.Build();

        // 4 bytes header + 1 byte + 2 bytes = 7 bytes total
        Assert.HasCount(7, packet);

        ushort totalLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));

        Assert.AreEqual((ushort)7, totalLength);
        Assert.AreEqual((ushort)ServerOpcode.ServerListResponse, opcode);
        Assert.AreEqual((byte)1, packet[4]);
        Assert.AreEqual((ushort)40708, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(5, 2)));
    }

    [TestMethod]
    public void Roundtrip_WriterAndReader_PreservesAllData()
    {
        using var writer = new PacketWriter((ushort)ClientOpcode.Login);
        writer.WriteByte(5);
        writer.WriteBoolean(true);
        writer.WriteInt16(-456);
        writer.WriteUInt16(999);
        writer.WriteInt32(-100000);
        writer.WriteUInt32(4000000);
        writer.WriteSingle(123.456f);
        writer.WriteFixedString("TestUser", 12);
        writer.WriteLengthPrefixedString("Password123");
        writer.WriteNullTerminatedString("NullString");

        byte[] packet = writer.Build();

        // Parse header
        Assert.IsTrue(PacketHeader.TryParse(packet, out var header));
        Assert.AreEqual((ushort)ClientOpcode.Login, header.Opcode);
        Assert.AreEqual(packet.Length, (int)header.TotalLength);

        // Read payload
        var reader = new PacketReader(packet.AsSpan(PacketHeader.HeaderSize));
        Assert.AreEqual((byte)5, reader.ReadByte());
        Assert.IsTrue(reader.ReadBoolean());
        Assert.AreEqual((short)-456, reader.ReadInt16());
        Assert.AreEqual((ushort)999, reader.ReadUInt16());
        Assert.AreEqual(-100000, reader.ReadInt32());
        Assert.AreEqual(4000000u, reader.ReadUInt32());
        Assert.AreEqual(123.456f, reader.ReadSingle(), 0.001f);
        Assert.AreEqual("TestUser", reader.ReadFixedString(12));
        Assert.AreEqual("Password123", reader.ReadLengthPrefixedString());
        Assert.AreEqual("NullString", reader.ReadNullTerminatedString());
        Assert.AreEqual(0, reader.Remaining);
    }

    [TestMethod]
    public void Write_ExpandingBuffer_GrowsDynamically()
    {
        using var writer = new PacketWriter(initialCapacity: 16);
        byte[] largeData = new byte[1024];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }

        writer.WriteBytes(largeData);

        byte[] result = writer.Build();
        Assert.HasCount(1024, result);
        CollectionAssert.AreEqual(largeData, result);
    }
}
