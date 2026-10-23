using System.Globalization;
using System.Text.RegularExpressions;
using Rakion.Database.Entities;

namespace Rakion.Database.Import;

/// <summary>
/// Reads the level table from a legacy MySQL dump (<c>INSERT INTO `classlevelinfo` VALUES ('class', 'level', 'exp');</c>).
/// Column order Observed in the dump's CREATE TABLE: Class, level, exp. The dump stays on the operator's machine.
/// </summary>
public static partial class LegacyClassLevelParser
{
    public static IReadOnlyList<ClassLevelEntity> Parse(TextReader reader)
    {
        var rows = new List<ClassLevelEntity>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = InsertRow().Match(line);
            if (match.Success &&
                byte.TryParse(match.Groups["class"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte cls) &&
                byte.TryParse(match.Groups["level"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte level) &&
                int.TryParse(match.Groups["exp"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int exp))
            {
                rows.Add(new ClassLevelEntity(cls, level, exp));
            }
        }

        return rows;
    }

    [GeneratedRegex(@"^\s*INSERT INTO `classlevelinfo` VALUES \('(?<class>\d+)',\s*'(?<level>\d+)',\s*'(?<exp>\d+)'\);\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex InsertRow();
}
