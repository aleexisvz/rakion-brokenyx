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

    [TestMethod]
    public void ServerTimeAndPowerUser_RoundTrip()
    {
        var packet = new WorldLoginResponsePacket(
            ResultCode: 0,
            UserId: 1,
            Gold: 95280,
            Cash: 30000,
            UnlockedSlots: 3,
            Username: "admin",
            TutorialCompleted: true,
            ServerTimeMinutes: 1_066_040_747,
            PowerUserExpiry: 1_066_083_947,
            PowerUserPoints: 5);

        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        byte[] bytes = writer.Build();

        Assert.AreEqual(1_066_040_747u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(PacketHeader.HeaderSize + 11)));
        Assert.IsTrue(WorldLoginResponsePacket.TryParse(bytes.AsSpan(PacketHeader.HeaderSize), out var parsed));
        Assert.AreEqual(1_066_040_747u, parsed.ServerTimeMinutes);
        Assert.AreEqual(1_066_083_947u, parsed.PowerUserExpiry);
        Assert.AreEqual((ushort)5, parsed.PowerUserPoints);
        Assert.AreEqual(95280u, parsed.Gold);
        Assert.AreEqual(30000u, parsed.Cash);
        Assert.IsTrue(parsed.TutorialCompleted);
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
