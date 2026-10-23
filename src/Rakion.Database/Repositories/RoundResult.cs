namespace Rakion.Database.Repositories;

/// <summary>
/// A player's outcome for one round, as the legacy server counts it (RakionWorldServ.exe 0x40BB60, Observed):
/// 1 adds a win, 2 a loss, 3 a draw. <see cref="None"/> leaves the counters alone.
/// </summary>
public static class RoundResult
{
    public const byte None = 0;
    public const byte Win = 1;
    public const byte Loss = 2;
    public const byte Draw = 3;
}
