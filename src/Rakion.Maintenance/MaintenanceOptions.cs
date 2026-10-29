namespace Rakion.Maintenance;

/// <summary>
/// Scheduled jobs (section <c>Maintenance</c>). The defaults are the legacy RankUpdate.exe Config.ini values
/// (ProcessTime = 24, RankMonth = 2; Observed).
/// </summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>Hours between ranking runs; 0 turns the scheduled run off.</summary>
    public int RankingIntervalHours { get; set; } = 24;

    /// <summary>Only characters of accounts connected within this many months are ranked.</summary>
    public int RankingActiveMonths { get; set; } = 2;

    /// <summary>Runs the ranking once when the server starts.</summary>
    public bool RunRankingAtStartup { get; set; } = true;

    /// <summary>Abuser check (legacy [ABUSER] CheckDay = 1, CheckWinSum = 200, CheckExpSum = 6000, CheckGoldSum = 12000; Observed).</summary>
    public int AbuserCheckDays { get; set; } = 1;

    public int AbuserWinSum { get; set; } = 200;

    public long AbuserExpSum { get; set; } = 6000;

    public long AbuserGoldSum { get; set; } = 12000;
}
