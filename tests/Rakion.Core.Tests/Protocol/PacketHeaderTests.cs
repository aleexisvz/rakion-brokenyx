using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Protocol;

[TestClass]
public sealed class PacketHeaderTests
{
    [TestMethod]
    public void TryParse_WithValidBuffer_ParsesLengthAndOpcode()
    {
        // TotalLength: 0x0010 (16), Opcode: 0xCDDF
        byte[] buffer = [0x10, 0x00, 0xDF, 0xCD];

        bool success = PacketHeader.TryParse(buffer, out PacketHeader header);

        Assert.IsTrue(success);
        Assert.AreEqual((ushort)16, header.TotalLength);
        Assert.AreEqual((ushort)RakionOpcode.ServerListRequest, header.Opcode);
        Assert.AreEqual(12, header.PayloadLength);
    }

    [TestMethod]
    public void TryParse_WithBufferShorterThan4Bytes_ReturnsFalse()
    {
        byte[] shortBuffer = [0x01, 0x02, 0x03];

        bool success = PacketHeader.TryParse(shortBuffer, out PacketHeader header);

        Assert.IsFalse(success);
        Assert.AreEqual(default, header);
    }

    [TestMethod]
    public void WriteTo_SerializesCorrectLittleEndianBytes()
    {
        var header = new PacketHeader(100, (ushort)RakionOpcode.Login);
        Span<byte> destination = stackalloc byte[4];

        header.WriteTo(destination);

        Assert.AreEqual(100, destination[0] | (destination[1] << 8));
        Assert.AreEqual((ushort)RakionOpcode.Login, (ushort)(destination[2] | (destination[3] << 8)));
    }
}
