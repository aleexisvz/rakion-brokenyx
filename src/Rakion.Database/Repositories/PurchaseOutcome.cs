namespace Rakion.Database.Repositories;

/// <summary>Result of a completed purchase: the new item row and the balances after paying.</summary>
public sealed record PurchaseOutcome(int UserItemId, long Gold, int Cash);
