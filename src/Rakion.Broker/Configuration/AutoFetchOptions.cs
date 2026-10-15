namespace Rakion.Broker.Configuration;

/// <summary>
/// Options for the embedded HTTP AutoFetch service that provides version information to NyxLauncher.
/// Replaces the legacy Apache/PHP fetch.php script.
/// </summary>
public sealed class AutoFetchOptions
{
    public const string SectionName = "AutoFetch";

    public bool Enabled { get; set; } = true;
    public string ListenIp { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public ushort TargetVersion { get; set; } = 258;
    public string NoticeUrl { get; set; } = "http://127.0.0.1/notice.html";
    public string PatchUrl { get; set; } = "http://127.0.0.1/patches";
}
