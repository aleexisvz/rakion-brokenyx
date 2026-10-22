using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;

namespace Rakion.Core.Tests.Packets;

[TestClass]
public sealed class WorldLoginTutorialFlagTests
{
    [TestMethod]
    public void TutorialCompleted_ChangesOnlyTheByteAfterUnlockedSlots()
    {
        byte[] pending = Serialize(tutorialCompleted: false);
        byte[] done = Serialize(tutorialCompleted: true);

        Assert.HasCount(pending.Length, done);
        int[] diffs = Enumerable.Range(0, pending.Length).Where(i => pending[i] != done[i]).ToArray();
        Assert.HasCount(1, diffs);

        int flag = diffs[0];
        Assert.AreEqual((byte)0, pending[flag]);
        Assert.AreEqual((byte)1, done[flag]);
        Assert.AreEqual((byte)3, done[flag - 1]); // UnlockedSlots precedes the tutorial flag.
    }

    private static byte[] Serialize(bool tutorialCompleted)
    {
        var packet = new WorldLoginResponsePacket(
            ResultCode: 0,
            UserId: 1,
            Gold: 100,
            Cash: 50,
            UnlockedSlots: 3,
            Username: "admin",
            TutorialCompleted: tutorialCompleted);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        return writer.Build();
    }
}
