namespace Rakion.World.Features.Economy;

/// <summary>
/// Outcome weights (percent) for one reinforce attempt at a given item level. The order matches the client's
/// result codes 0..4: level up, no change, level -1, -2, -3. Destruction (code 5) is never rolled: the legacy server
/// turns it into -3 at level 3 or below and into "no change" above (RakionWorldServ.exe 0x40C7A9, Observed).
/// </summary>
public sealed record EnchantOdds(int LevelUp, int NoChange, int Down1, int Down2, int Down3)
{
    public int Total => LevelUp + NoChange + Down1 + Down2 + Down3;

    /// <summary>
    /// Inferred placeholder table: the legacy odds come from a per-stone configuration table that is not in the
    /// binary (UNKNOWN). Levels 0..3 always succeed; higher levels succeed less often and may lose one level.
    /// Replace with observed values when they are known.
    /// </summary>
    public static EnchantOdds DefaultFor(int level)
    {
        if (level <= 3)
        {
            return new EnchantOdds(100, 0, 0, 0, 0);
        }

        int levelUp = Math.Max(90 - (level - 4) * 8, 10);
        int down1 = (100 - levelUp) / 2;
        return new EnchantOdds(levelUp, 100 - levelUp - down1, down1, 0, 0);
    }

    /// <summary>
    /// Scales the three downgrade weights (rounded down) and moves what they lose to "no change", so the level-up
    /// chance stays the same. A scale of 1 returns the same odds.
    /// </summary>
    public EnchantOdds WithDowngradeScale(double scale)
    {
        scale = Math.Clamp(scale, 0, 1);
        int down1 = (int)(Down1 * scale);
        int down2 = (int)(Down2 * scale);
        int down3 = (int)(Down3 * scale);
        return new EnchantOdds(LevelUp, NoChange + (Down1 - down1) + (Down2 - down2) + (Down3 - down3), down1, down2, down3);
    }

    /// <summary>Picks an outcome code (0..4) from a roll in [0, Total).</summary>
    public byte Pick(int roll)
    {
        int[] weights = [LevelUp, NoChange, Down1, Down2, Down3];
        for (byte code = 0; code < weights.Length; code++)
        {
            if (roll < weights[code])
            {
                return code;
            }

            roll -= weights[code];
        }

        return 1;
    }
}
