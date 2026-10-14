using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Protocol;

[TestClass]
public sealed class PacketReaderTests
{
    [TestMethod]
    public void ReadPrimitives_LittleEndian_ReadsCorrectValues()
    {
        byte[] buffer = new byte[32];
        buffer[0] = 42;
        buffer[1] = 1; // bool true
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(2, 2), -1234);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), 5678);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(6, 4), -999999);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(10, 4), 123456789);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(14, 4), 3.14159f);

        var reader = new PacketReader(buffer);

        Assert.AreEqual((byte)42, reader.ReadByte());
        Assert.IsTrue(reader.ReadBoolean());
        Assert.AreEqual((short)-1234, reader.ReadInt16());
        Assert.AreEqual((ushort)5678, reader.ReadUInt16());
        Assert.AreEqual(-999999, reader.ReadInt32());
        Assert.AreEqual(123456789u, reader.ReadUInt32());
        Assert.AreEqual(3.14159f, reader.ReadSingle(), 0.0001f);
    }

    [TestMethod]
    public void ReadFixedString_PaddedWithNulls_ReturnsTrimmedString()
    {
        byte[] buffer = new byte[16];
        Encoding.Latin1.GetBytes("PlayerOne").CopyTo(buffer, 0);

        var reader = new PacketReader(buffer);
        string result = reader.ReadFixedString(16);

        Assert.AreEqual("PlayerOne", result);
        Assert.AreEqual(16, reader.Position);
    }

    [TestMethod]
    public void ReadLengthPrefixedString_ReadsCorrectLengthAndText()
    {
        string text = "WorldServer1";
        byte[] textBytes = Encoding.Latin1.GetBytes(text);
        byte[] buffer = new byte[2 + textBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)textBytes.Length);
        textBytes.CopyTo(buffer, 2);

        var reader = new PacketReader(buffer);
        string result = reader.ReadLengthPrefixedString();

        Assert.AreEqual(text, result);
        Assert.AreEqual(buffer.Length, reader.Position);
    }

    [TestMethod]
    public void ReadNullTerminatedString_ReadsUntilNull()
    {
        byte[] buffer = "Hello Rakion\0ExtraData"u8.ToArray();

        var reader = new PacketReader(buffer);
        string result = reader.ReadNullTerminatedString();

        Assert.AreEqual("Hello Rakion", result);
        Assert.AreEqual(13, reader.Position);
    }

    [TestMethod]
    public void Read_BeyondBufferBounds_ThrowsPacketParseException()
    {
        byte[] buffer = [0x01, 0x02];
        var reader = new PacketReader(buffer);

        reader.ReadByte();
        reader.ReadByte();

        bool threw = false;
        try
        {
            reader.ReadByte();
        }
        catch (PacketParseException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "Expected PacketParseException when reading past buffer bounds.");
    }
}
