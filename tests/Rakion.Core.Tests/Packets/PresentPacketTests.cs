using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class PresentPacketTests
{
    [TestMethod]
    public void PresentPackets_MatchTheClientLayouts()
    {
        CollectionAssert.AreEqual(new byte[] { 0, 7, 0, 0, 0, 0xE1, 0x2E, 0, 0, 0, 0 }, PayloadOf(new PresentPeekResponsePacket(0, 7, 12001)));
        CollectionAssert.AreEqual(new byte[] { 3, 0, 0, 0, 0 }, PayloadOf(new PresentAcceptResponsePacket(PresentAcceptResponsePacket.SlotTaken)));
        CollectionAssert.AreEqual(new byte[] { 1 }, PayloadOf(new PresentDisposeResponsePacket(1)));
        Assert.AreEqual((ushort)ServerOpcode.PresentPeek, new PresentPeekResponsePacket(1).Opcode);

        Assert.IsTrue(PresentAcceptRequestPacket.TryParse([7, 0, 0, 0, 12, 0], out var accept));
        Assert.AreEqual(new PresentAcceptRequestPacket(7, 12), accept);
        Assert.IsTrue(PresentDisposeRequestPacket.TryParse([9, 0, 0, 0], out var dispose));
        Assert.AreEqual(9u, dispose.PendingId);
        Assert.IsFalse(PresentAcceptRequestPacket.TryParse([7, 0, 0], out _));
    }

    private static byte[] PayloadOf(IPacket packet)
    {
        using var writer = new PacketWriter();
        packet.Serialize(writer);
        return writer.WrittenSpan.ToArray();
    }
}
