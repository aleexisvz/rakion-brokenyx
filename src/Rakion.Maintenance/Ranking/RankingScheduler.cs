using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Maintenance.AbuserCheck;

namespace Rakion.Maintenance.Ranking;

/// <summary>
/// Runs <see cref="RankingJob"/> and then <see cref="AbuserCheckJob"/>, as the legacy RankUpdate.exe did each
/// ProcessTime, at startup and then every <see cref="MaintenanceOptions.RankingIntervalHours"/>.
/// </summary>
public sealed class RankingScheduler : BackgroundService
{
    private readonly RankingJob _job;
    private readonly AbuserCheckJob? _abuserCheck;
    private readonly IOptions<MaintenanceOptions> _options;
    private readonly ILogger<RankingScheduler> _logger;

    public RankingScheduler(RankingJob job, IOptions<MaintenanceOptions> options, ILogger<RankingScheduler> logger, AbuserCheckJob? abuserCheck = null)
    {
        _job = job;
        _abuserCheck = abuserCheck;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        if (options.RunRankingAtStartup)
        {
            await RunSafelyAsync(stoppingToken).ConfigureAwait(false);
        }

        if (options.RankingIntervalHours <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.RankingIntervalHours));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunSafelyAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _job.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ranking update failed.");
        }

        if (_abuserCheck is null)
        {
            return;
        }

        try
        {
            await _abuserCheck.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Abuser check failed.");
        }
    }
}
