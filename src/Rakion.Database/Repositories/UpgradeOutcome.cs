namespace Rakion.Database.Repositories;

/// <summary>Result of a bag or potion slot purchase with the balances and the count after it.</summary>
public sealed record UpgradeOutcome(UpgradeStatus Status, long Gold, int Cash, byte Count);
