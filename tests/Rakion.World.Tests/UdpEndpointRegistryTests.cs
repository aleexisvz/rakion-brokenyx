using Rakion.World.Session;

namespace Rakion.World.Tests;

[TestClass]
public sealed class UdpEndpointRegistryTests
{
    [TestMethod]
    public void Register_GivesDistinctIndexes_AndRecordsBothPorts()
    {
        var registry = new UdpEndpointRegistry();
        var (index1, key1) = registry.Register();
        var (index2, _) = registry.Register();

        Assert.AreNotEqual((ushort)0, index1);
        Assert.AreNotEqual(index1, index2);
        Assert.IsTrue(registry.TryRecord(key1, secondPort: false, 0x0100007F, 0x0009));
        Assert.IsTrue(registry.TryRecord(key1, secondPort: true, 0x0100007F, 0x0109));

        Assert.AreEqual(new UdpEndpoints(0x0100007F, 0x0009, 0x0100007F, 0x0109), registry.Get(index1));
        Assert.AreEqual(default, registry.Get(index2));
    }

    [TestMethod]
    public void TryRecord_RejectsWrongKeyAndReleasedIndex()
    {
        var registry = new UdpEndpointRegistry();
        var (index, key) = registry.Register();

        Assert.IsFalse(registry.TryRecord(key + 1, secondPort: false, 1, 1));
        registry.Release(index);
        Assert.IsFalse(registry.TryRecord(key, secondPort: false, 1, 1));
    }
}
