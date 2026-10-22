namespace Rakion.Database.Repositories;

/// <summary>Result of a completed sale: the removed item row and the balances after being paid.</summary>
public sealed record SaleOutcome(int UserItemId, long Gold, int Cash);
