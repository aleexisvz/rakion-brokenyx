using Rakion.Maintenance.Ranking;
using Rakion.World.Features.Admin;

namespace Rakion.App.HostedServices;

/// <summary>Connects the GM /rankupdate command (World) to the ranking job (Maintenance).</summary>
public sealed class RankingTrigger : IRankingTrigger
{
    private readonly RankingJob _job;

    public RankingTrigger(RankingJob job)
    {
        _job = job;
    }

    public Task<int> RunAsync(CancellationToken cancellationToken) => _job.RunAsync(cancellationToken);
}
