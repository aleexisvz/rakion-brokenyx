using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rakion.Core.Utilities;

namespace Rakion.Core.Tests.Utilities;

[TestClass]
public sealed class LegacyGameClockTests
{
    // Reference values from the MySQL TO_DAYS() documentation.
    [TestMethod]
    [DataRow(1995, 5, 1, 728779)]
    [DataRow(2007, 10, 7, 733321)]
    public void ToDays_MatchesMySqlToDays(int year, int month, int day, int expected)
    {
        Assert.AreEqual(expected, LegacyGameClock.ToDays(new DateTime(year, month, day)));
    }

    [TestMethod]
    public void ToMinutes_CombinesDaysHoursAndMinutes()
    {
        var time = new DateTime(2007, 10, 7, 13, 45, 59);

        uint expected = (uint)(((733321L * 24) + 13) * 60 + 45);
        Assert.AreEqual(expected, LegacyGameClock.ToMinutes(time));
    }
}
