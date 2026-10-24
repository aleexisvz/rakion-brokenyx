namespace Rakion.World.Features.Admin;

/// <summary>
/// Runs the ranking update on demand (GM /rankupdate). The job lives in Rakion.Maintenance; the App connects the
/// two, so World does not reference Maintenance.
/// </summary>
public interface IRankingTrigger
{
    /// <summary>Returns how many characters were ranked.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
}
