namespace Rakion.Core.Enums;

/// <summary>
/// Match game modes supported in Rakion fields.
/// Evidence: Observed in WorldServer FieldMode logging and StageInfo.
/// </summary>
public enum GameMode : byte
{
    SoloDeathmatch = 0,
    TeamDeathmatch = 1,
    LeaderWars = 2,
    Stage = 3,
    GolemWars = 4,
    BossRaid = 5,
}
