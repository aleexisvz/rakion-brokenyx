using System.Globalization;
using System.Text.RegularExpressions;
using Rakion.Database.Entities;

namespace Rakion.Database.Import;

/// <summary>
/// Reads the creature level table from a legacy MySQL dump (<c>INSERT INTO `npcinfo` VALUES ('npc', 'level', 'exp', 'gold');</c>).
/// Column order Observed in the dump's CREATE TABLE: npc, level, exp, gold. Levels above 99 are skipped: the legacy
/// server caps creatures there. The dump stays on the operator's machine.
/// </summary>
public static partial class LegacyCreatureLevelParser
{
    public static IReadOnlyList<CreatureLevelEntity> Parse(TextReader reader)
    {
        var rows = new List<CreatureLevelEntity>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = InsertRow().Match(line);
            if (match.Success &&
                byte.TryParse(match.Groups["npc"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte npc) &&
                byte.TryParse(match.Groups["level"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte level) &&
                level <= Repositories.MatchResultRepository.MaxLevel &&
                int.TryParse(match.Groups["exp"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int exp))
            {
                rows.Add(new CreatureLevelEntity(npc, level, exp));
            }
        }

        return rows;
    }

    [GeneratedRegex(@"^\s*INSERT INTO `npcinfo` VALUES \('(?<npc>\d+)',\s*'(?<level>\d+)',\s*'(?<exp>\d+)',\s*'\d+'\);\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex InsertRow();
}
