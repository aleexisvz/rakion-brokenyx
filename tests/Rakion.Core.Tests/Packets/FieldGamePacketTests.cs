using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class FieldGamePacketTests
{
    [TestMethod]
    public void GameStartResponse_IsOneResultByte()
    {
        var packet = new FieldGameStartResponsePacket(FieldGameStartResponsePacket.NotAllReady);

        byte[] payload = PayloadOf(packet);

        CollectionAssert.AreEqual(new byte[] { 3 }, payload);
        Assert.IsTrue(FieldGameStartResponsePacket.TryParse(payload, out var parsed));
        Assert.AreEqual(packet, parsed);
        Assert.AreEqual((ushort)0x0043, packet.Opcode);
    }

    [TestMethod]
    public void GameEnterResponse_IsResultThenSlot()
    {
        var packet = new FieldGameEnterResponsePacket(FieldGameEnterResponsePacket.Entered, 11);

        byte[] payload = PayloadOf(packet);

        CollectionAssert.AreEqual(new byte[] { 0, 11 }, payload);
        Assert.IsTrue(FieldGameEnterResponsePacket.TryParse(payload, out var parsed));
        Assert.AreEqual(packet, parsed);
        Assert.AreEqual((ushort)0x0045, packet.Opcode);
    }

    [TestMethod]
    public void GameExitAndEnd_RoundTripOneByte()
    {
        Assert.IsTrue(FieldGameExitRequestPacket.TryParse(PayloadOf(new FieldGameExitRequestPacket(2)), out var exitRequest));
        Assert.AreEqual((byte)2, exitRequest.Value);
        Assert.IsTrue(FieldGameExitBroadcastPacket.TryParse(PayloadOf(new FieldGameExitBroadcastPacket(2)), out var exit));
        Assert.AreEqual((byte)2, exit.Value);
        Assert.IsTrue(FieldGameEndBroadcastPacket.TryParse(PayloadOf(new FieldGameEndBroadcastPacket(1)), out var end));
        Assert.AreEqual((byte)1, end.Value);
        Assert.AreEqual((ushort)ServerOpcode.FieldGameEnd, end.Opcode);
    }

    [TestMethod]
    public void TryParse_RejectsShortPayloads()
    {
        Assert.IsFalse(FieldGameStartResponsePacket.TryParse([], out _));
        Assert.IsFalse(FieldGameEnterResponsePacket.TryParse([0], out _));
        Assert.IsFalse(FieldGameExitRequestPacket.TryParse([], out _));
    }

    private static byte[] PayloadOf(IPacket packet)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        return writer.Build()[PacketHeader.HeaderSize..].ToArray();
    }
}
