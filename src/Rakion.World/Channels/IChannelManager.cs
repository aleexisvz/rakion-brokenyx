using Rakion.World.Session;

namespace Rakion.World.Channels;

/// <summary>
/// Service managing lobby channels, player entrance/exit, and channel state.
/// </summary>
public interface IChannelManager
{
    /// <summary>
    /// Gets all active lobby channels.
    /// </summary>
    IReadOnlyList<LobbyChannel> GetChannels();

    /// <summary>
    /// Attempts to retrieve a channel by its index.
    /// </summary>
    bool TryGetChannel(byte index, out LobbyChannel? channel);

    /// <summary>
    /// Attempts to place an authenticated session with an active character into a channel.
    /// </summary>
    /// <param name="index">Target channel index.</param>
    /// <param name="session">Player session.</param>
    /// <param name="password">Channel password (if any).</param>
    /// <param name="failureReason">0 on success, or non-zero error code.</param>
    /// <returns>True if entered successfully; otherwise false.</returns>
    bool TryEnterChannel(byte index, WorldSession session, string password, out byte failureReason);

    /// <summary>
    /// Removes a session from whatever channel it currently occupies.
    /// </summary>
    void LeaveChannel(WorldSession session);
}
