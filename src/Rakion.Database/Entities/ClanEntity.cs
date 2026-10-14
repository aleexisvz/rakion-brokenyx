namespace Rakion.Database.Entities;

/// <summary>
/// Represents a player clan / guild.
/// </summary>
public sealed class ClanEntity
{
    public int Id { get; set; }

    public int MasterId { get; set; }

    public string MasterName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Points { get; set; }

    public short MemberCount { get; set; }

    public uint Rank { get; set; }

    public short Country { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

