namespace Rakion.Database.Entities;

/// <summary>
/// Represents real-money cash balance for a user account.
/// </summary>
public sealed class CashEntity
{
    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public int CashAmount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

