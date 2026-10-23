namespace Rakion.World.Configuration;

/// <summary>
/// Operator tuning on top of the legacy rules (section <c>World:Rewards</c>). The defaults keep the legacy behavior.
/// </summary>
public sealed class RewardOptions
{
    /// <summary>Multiplies the experience a round report adds, after the legacy limits and the PowerUser bonus.</summary>
    public int ExpMultiplier { get; set; } = 1;

    /// <summary>Multiplies the gold a round report adds, after the legacy limits.</summary>
    public int GoldMultiplier { get; set; } = 1;

    /// <summary>Scales the chance that a reinforce lowers the item (0.7 = 30 % less); the rest becomes "no change".</summary>
    public double EnchantDowngradeScale { get; set; } = 1.0;
}
