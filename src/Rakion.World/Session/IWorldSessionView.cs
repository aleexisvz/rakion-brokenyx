namespace Rakion.World.Session;

/// <summary>
/// Read-only view of a World session for features outside the session owner
/// (contract C-2 of the roadmap). Consumers read state here and never mutate
/// <see cref="WorldSession"/> directly.
/// </summary>
public interface IWorldSessionView
{
    Guid ConnectionId { get; }
    bool IsAuthenticated { get; }
    int? UserId { get; }
    string? Username { get; }
    int? CharacterId { get; }
    string? CharacterName { get; }
    byte? ChannelIndex { get; }

    /// <summary>
    /// True while the session sits in a field (room or match). Always false until the
    /// room system of phase 5 sets it.
    /// </summary>
    bool IsInField { get; }
}
