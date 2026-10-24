using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Database.Repositories;

namespace Rakion.Maintenance.Ranking;

/// <summary>
/// One ranking run, the job the legacy RankUpdate.exe did once per <c>ProcessTime</c> hours: read the active
/// characters, compute the ranks and store them with the ranking snapshots.
/// </summary>
public sealed class RankingJob
{
    /// <summary>Character auth values left out of the ranking (RankUpdate.exe 0x402202: 2, 10 and 0x34; Observed).</summary>
    public static readonly IReadOnlyList<int> ExcludedAuth = [2, 10, 0x34];

    private readonly IRankingRepository _repository;
    private readonly IOptions<MaintenanceOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RankingJob> _logger;

    public RankingJob(IRankingRepository repository, IOptions<MaintenanceOptions> options, TimeProvider timeProvider, ILogger<RankingJob> logger)
    {
        _repository = repository;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the ranking and returns how many characters were ranked.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var activeSince = _timeProvider.GetLocalNow().DateTime.AddMonths(-Math.Max(_options.Value.RankingActiveMonths, 1));
        var candidates = await _repository.GetCandidatesAsync(activeSince, ExcludedAuth, cancellationToken).ConfigureAwait(false);
        var entries = RankingCalculator.Compute(candidates);
        await _repository.SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Ranking updated: {Count} characters active since {Since:yyyy-MM-dd}.", entries.Count, activeSince);
        return entries.Count;
    }
}
