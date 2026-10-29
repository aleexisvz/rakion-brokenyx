using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Database.Repositories;

namespace Rakion.Maintenance.AbuserCheck;

/// <summary>
/// Daily abuser check (roadmap 7.2), the second job of the legacy RankUpdate.exe: flag the characters whose logged
/// round results over the last days reach a threshold, for a GM to review. Nobody is banned automatically.
/// </summary>
public sealed class AbuserCheckJob
{
    private readonly IAbuserRepository _repository;
    private readonly IOptions<MaintenanceOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AbuserCheckJob> _logger;

    public AbuserCheckJob(IAbuserRepository repository, IOptions<MaintenanceOptions> options, TimeProvider timeProvider, ILogger<AbuserCheckJob> logger)
    {
        _repository = repository;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Runs the check and returns the characters flagged this time.</summary>
    public async Task<IReadOnlyList<AbuserFlag>> RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var since = _timeProvider.GetLocalNow().DateTime.AddDays(-Math.Max(options.AbuserCheckDays, 1));
        var flags = await _repository
            .RunCheckAsync(since, options.AbuserWinSum, options.AbuserExpSum, options.AbuserGoldSum, cancellationToken)
            .ConfigureAwait(false);
        foreach (var flag in flags)
        {
            _logger.LogWarning(
                "Abuser check: '{Character}' (id {Id}{PowerUser}) reached W {Wins} / L {Losses}, exp {Exp}, gold {Gold} since {Since:yyyy-MM-dd HH:mm}.",
                flag.CharacterName,
                flag.CharacterId,
                flag.PowerUser ? ", PowerUser" : string.Empty,
                flag.Wins,
                flag.Losses,
                flag.Exp,
                flag.Gold,
                since);
        }

        _logger.LogInformation("Abuser check done: {Count} characters flagged.", flags.Count);
        return flags;
    }
}
