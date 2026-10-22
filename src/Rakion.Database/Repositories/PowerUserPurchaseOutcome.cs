namespace Rakion.Database.Repositories;

/// <summary>What happened when buying PowerUser time.</summary>
public enum PowerUserPurchaseStatus
{
    Bought,
    NotEnoughCash,
    LimitReached,
}

/// <summary>Result of a PowerUser purchase and the account values after it.</summary>
public sealed record PowerUserPurchaseOutcome(
    PowerUserPurchaseStatus Status,
    long Gold,
    int Cash,
    long PowerTime,
    int PowerLevelPoints);
