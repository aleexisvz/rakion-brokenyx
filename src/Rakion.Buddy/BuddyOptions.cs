namespace Rakion.Buddy;

/// <summary>
/// Messenger server settings (section <c>Buddy</c>). The client reads the address from BuddyIP / BuddyPort in
/// DataSetup/Locale.ini (8500 in the v258 client, Observed); TCP and UDP share the port.
/// </summary>
public sealed class BuddyOptions
{
    public const string SectionName = "Buddy";

    public bool Enabled { get; set; } = true;

    public string ListenIp { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 8500;
}
