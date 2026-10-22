namespace Rakion.Core.Utilities;

/// <summary>
/// Time unit used by the v258 protocol for server time and item rental limits:
/// minutes since MySQL day zero, computed as <c>(TO_DAYS(now())*24 + HOUR(now()))*60 + MINUTE(now())</c>.
/// Evidence: Observed SQL in RakionWorldServ DBCommandInventoryEnter (string 0x4489EC).
/// </summary>
public static class LegacyGameClock
{
    // MySQL TO_DAYS(0001-01-01) is 366 because it counts the proleptic year 0.
    private const int ToDaysOffset = 366;

    public static int ToDays(DateTime date) => (int)(date.Date - DateTime.MinValue).TotalDays + ToDaysOffset;

    /// <summary>
    /// Converts a server-local wall-clock time to legacy minutes. MySQL <c>now()</c> is server local time.
    /// </summary>
    public static uint ToMinutes(DateTime localTime) =>
        checked((uint)((((long)ToDays(localTime) * 24) + localTime.Hour) * 60 + localTime.Minute));
}
