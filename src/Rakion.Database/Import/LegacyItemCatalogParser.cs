using System.Globalization;
using System.Text.RegularExpressions;
using Rakion.Core.Enums;
using Rakion.Database.Entities;

namespace Rakion.Database.Import;

/// <summary>
/// Reads item templates from a legacy MySQL dump (<c>INSERT INTO `iteminfo` VALUES (...)</c> rows).
/// The dump is read from the operator's local copy and is never shipped with the repository.
/// Column order Observed in the dump's CREATE TABLE: id, type, Class, level, shop, gold, cash, hit1..hit4,
/// chit, ap, hp, maxcp, power.
/// </summary>
public static partial class LegacyItemCatalogParser
{
    private const int ColumnCount = 16;

    public static IReadOnlyList<ItemEntity> Parse(TextReader reader)
    {
        var items = new List<ItemEntity>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = InsertRow().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var values = Value().Matches(match.Groups["values"].Value).Select(m => m.Groups[1].Value).ToArray();
            if (values.Length != ColumnCount || !values.All(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                continue;
            }

            int At(int i) => int.Parse(values[i], CultureInfo.InvariantCulture);
            items.Add(new ItemEntity
            {
                Id = At(0),
                Type = (byte)At(1),
                Class = (CharacterClass)At(2),
                RequiredLevel = (byte)At(3),
                Shop = (byte)At(4),
                GoldPrice = At(5),
                CashPrice = At(6),
                Hit1 = At(7),
                Hit2 = At(8),
                Hit3 = At(9),
                Hit4 = At(10),
                Chit = At(11),
                Ap = At(12),
                Hp = At(13),
                MaxCp = At(14),
                Power = At(15),
            });
        }

        return items;
    }

    [GeneratedRegex(@"^\s*INSERT INTO `iteminfo` VALUES \((?<values>.*)\);\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex InsertRow();

    [GeneratedRegex(@"'([^']*)'")]
    private static partial Regex Value();
}
