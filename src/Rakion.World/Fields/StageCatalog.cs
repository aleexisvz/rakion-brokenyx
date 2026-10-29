namespace Rakion.World.Fields;

/// <summary>Limits of one Stage: how many players the room holds and the character levels allowed.</summary>
public sealed record StageLimits(byte MaxPlayers, byte MinLevel, byte MaxLevel)
{
    /// <summary>Same range check as the legacy server (RakionWorldServ.exe 0x40A8C0): MinLevel &lt;= level &lt;= MaxLevel.</summary>
    public bool AllowsLevel(int level) => level >= MinLevel && level <= MaxLevel;
}

/// <summary>
/// The Stage table (legacy StageInfo, loaded by RakionWorldServ.exe InitStageInfo from
/// "SELECT id,maxcharacters,minlevel,maxlevel FROM StageInfo"). Values are the legacy table's rows (Observed); the
/// stage id is the first rule byte of the room (<see cref="FieldRules.Kind"/>, legacy room +0x118).
/// </summary>
public static class StageCatalog
{
    private static readonly StageLimits?[] Stages = Build(
        (1, 1, 1, 10), (2, 1, 1, 10), (3, 1, 2, 13), (4, 1, 2, 13), (5, 1, 3, 14), (6, 3, 3, 14),
        (7, 2, 4, 15), (8, 2, 4, 15), (9, 1, 5, 16), (10, 3, 5, 16), (11, 1, 6, 17), (12, 1, 6, 17),
        (13, 2, 7, 18), (14, 3, 7, 18), (15, 1, 8, 19), (16, 3, 8, 19), (17, 3, 9, 20), (18, 1, 9, 20),
        (19, 4, 10, 21), (20, 4, 10, 21), (21, 2, 11, 22), (22, 4, 11, 22), (23, 1, 12, 23), (24, 2, 12, 23),
        (25, 2, 13, 24), (26, 2, 13, 24), (27, 2, 14, 25), (28, 2, 14, 25), (29, 3, 15, 26), (30, 2, 15, 26),
        (31, 1, 16, 27), (32, 1, 16, 27), (33, 3, 17, 28), (34, 3, 17, 28), (35, 4, 18, 29), (36, 1, 18, 29),
        (37, 3, 19, 30), (38, 2, 19, 30), (39, 2, 20, 31), (40, 4, 20, 31), (41, 2, 21, 32), (42, 1, 21, 32),
        (43, 2, 22, 33), (44, 2, 22, 33), (45, 2, 23, 34), (46, 2, 23, 34), (47, 2, 24, 35), (48, 3, 24, 35));

    /// <summary>The limits of a stage, or null when the id is not in the table.</summary>
    public static StageLimits? Get(byte stageId) => stageId < Stages.Length ? Stages[stageId] : null;

    private static StageLimits?[] Build(params (byte Id, byte MaxPlayers, byte MinLevel, byte MaxLevel)[] rows)
    {
        var stages = new StageLimits?[rows.Max(r => r.Id) + 1];
        foreach (var row in rows)
        {
            stages[row.Id] = new StageLimits(row.MaxPlayers, row.MinLevel, row.MaxLevel);
        }

        return stages;
    }
}
