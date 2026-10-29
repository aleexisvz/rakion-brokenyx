namespace Rakion.World.Features.Economy;

/// <summary>
/// Outcome weights for one reinforce attempt. The order matches the client's result codes 0..4: level up, no change,
/// level -1, -2, -3. Destruction (code 5) is never rolled: the legacy server turns it into -3 at level 3 or below and
/// into "no change" above (RakionWorldServ.exe 0x40C7A9, Observed).
/// </summary>
public sealed record EnchantOdds(int LevelUp, int NoChange, int Down1, int Down2, int Down3)
{
    /// <summary>Weights are parts per million of the legacy probabilities.</summary>
    public const int Scale = 1_000_000;

    // Per stone: base success percent and the percent it loses per item level (RakionWorldServ.exe 0x41E6F4..0x41E791,
    // Observed): 13001 70/6, 13002 85/6, 13003 90/5, 13004 75/4, 13005 95/3.
    private static readonly Dictionary<int, (int Success, int DecayPerLevel)> Stones = new()
    {
        [13001] = (70, 6),
        [13002] = (85, 6),
        [13003] = (90, 5),
        [13004] = (75, 4),
        [13005] = (95, 3),
    };

    public int Total => LevelUp + NoChange + Down1 + Down2 + Down3;

    /// <summary>
    /// The legacy odds (RakionWorldServ.exe 0x40C404..0x40C790, Observed). With u = 0.05 per charm (14003):
    /// p = (1 - u) * success + u, then for each level i = 1..level, p = p * (1 - i * decay) * (1 - u) + u. The rest,
    /// 1 - p, is shared in fifteenths: no change (4 + 0.2 a + 1.2 b), -1 (3 + 0.6 b), -2 (2 + 0.4 b), -3 (1 + 0.1 b) and
    /// destroyed (1 - 0.2 a), with a = 14001 and b = 14002 catalysts, negatives as 0; what is left over is "no change"
    /// (legacy code 6, which the database leaves alone, 0x419642). The client describes 14002 as lowering the level-down
    /// chance, but this formula raises the -1..-3 shares; it is kept as observed.
    /// </summary>
    public static EnchantOdds Legacy(int level, int stoneId, int destroyGuards = 0, int downGuards = 0, int charms = 0)
    {
        var (success, decay) = Stones.TryGetValue(stoneId, out var stone) ? stone : Stones[13001];
        double u = charms * 0.05;
        double p = (1 - u) * (success * 0.01) + u;
        for (int i = 1; i <= level; i++)
        {
            p = p * (1 - (i * decay * 0.01)) * (1 - u) + u;
        }

        p = Math.Max(p, 0);
        double rest = 1 - p;
        double noChange = Share(4 + (0.2 * destroyGuards) + (1.2 * downGuards));
        double down1 = Share(3 + (0.6 * downGuards));
        double down2 = Share(2 + (0.4 * downGuards));
        double down3 = Share(1 + (0.1 * downGuards));
        double destroyed = Share(1 - (0.2 * destroyGuards));
        double leftOver = Math.Max(1 - p - noChange - down1 - down2 - down3 - destroyed, 0);

        if (level <= 3)
        {
            down3 += destroyed;
        }
        else
        {
            noChange += destroyed;
        }

        return new EnchantOdds(Ppm(p), Ppm(noChange + leftOver), Ppm(down1), Ppm(down2), Ppm(down3));

        double Share(double fifteenths) => Math.Max(rest * fifteenths / 15, 0);
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

    private static int Ppm(double probability) => (int)Math.Round(Math.Clamp(probability, 0, 1) * Scale);
}
