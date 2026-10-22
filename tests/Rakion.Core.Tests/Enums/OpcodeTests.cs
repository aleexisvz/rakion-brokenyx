using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Enums;

namespace Rakion.Core.Tests.Enums;

[TestClass]
public sealed class OpcodeTests
{
    // Aliases make ToString() and logs ambiguous, which is what C-0 removed.
    [TestMethod]
    public void ClientOpcode_HasNoDuplicateValues()
    {
        AssertNoDuplicateValues<ClientOpcode>();
    }

    [TestMethod]
    public void ServerOpcode_HasNoDuplicateValues()
    {
        AssertNoDuplicateValues<ServerOpcode>();
    }

    private static void AssertNoDuplicateValues<TEnum>()
        where TEnum : struct, Enum
    {
        var duplicates = Enum.GetNames<TEnum>()
            .GroupBy(name => Convert.ToUInt16(Enum.Parse<TEnum>(name)))
            .Where(group => group.Count() > 1)
            .Select(group => $"0x{group.Key:X4}: {string.Join(", ", group)}")
            .ToList();

        Assert.IsEmpty(duplicates, string.Join("; ", duplicates));
    }
}
