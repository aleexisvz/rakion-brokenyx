using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Core.Enums;
using Rakion.World.Configuration;
using Rakion.World.Session;

namespace Rakion.World.Channels;

/// <summary>
/// Thread-safe manager for World Server lobby channels.
/// </summary>
public sealed class ChannelManager : IChannelManager
{
    private readonly ConcurrentDictionary<byte, LobbyChannel> _channels = new();
    private readonly ILogger<ChannelManager> _logger;

    public ChannelManager(
        IOptions<WorldOptions> options,
        ILogger<ChannelManager> logger)
    {
        _logger = logger;

        var configuredChannels = options.Value.Channels;
        if (configuredChannels is null || configuredChannels.Count == 0)
        {
            configuredChannels = new List<ChannelConfig>
            {
                new() { Index = 0, Name = "Beginner 01", Type = ChannelType.Beginner, MaxLevel = 15, MaxPlayers = 100 },
                new() { Index = 1, Name = "Free 01", Type = ChannelType.Free, MaxLevel = 99, MaxPlayers = 100 },
                new() { Index = 2, Name = "Clan 01", Type = ChannelType.Clan, MaxLevel = 99, MaxPlayers = 100 }
            };
        }

        foreach (var cfg in configuredChannels)
        {
            var channel = new LobbyChannel(cfg.Index, cfg.Name, cfg.Type, cfg.MaxLevel, cfg.MaxPlayers);
            _channels[cfg.Index] = channel;
            _logger.LogInformation("Initialized lobby channel [{Index}] '{Name}' (Type: {Type}, MaxLevel: {MaxLevel}, MaxPlayers: {MaxPlayers}).",
                cfg.Index, cfg.Name, cfg.Type, cfg.MaxLevel, cfg.MaxPlayers);
        }
    }

    public IReadOnlyList<LobbyChannel> GetChannels()
    {
        return _channels.Values.OrderBy(c => c.Index).ToList();
    }

    public bool TryGetChannel(byte index, out LobbyChannel? channel)
    {
        return _channels.TryGetValue(index, out channel);
    }

    public bool TryEnterChannel(byte index, WorldSession session, string password, out byte failureReason)
    {
        failureReason = 0;

        if (!_channels.TryGetValue(index, out var targetChannel))
        {
            _logger.LogWarning("[{ConnectionId}] Channel enter failed: channel index {Index} not found.", session.Connection.ConnectionId, index);
            failureReason = 1;
            return false;
        }

        if (session.ActiveCharacter is null)
        {
            _logger.LogWarning("[{ConnectionId}] Channel enter failed: no active character selected.", session.Connection.ConnectionId);
            failureReason = 1;
            return false;
        }

        // Validate Beginner level restriction
        if (targetChannel.Type == ChannelType.Beginner && session.ActiveCharacter.Level > targetChannel.MaxLevel)
        {
            _logger.LogInformation(
                "[{ConnectionId}] Character '{Name}' (Level {Level}) rejected from Beginner channel [{Index}] (MaxLevel: {MaxLevel}).",
                session.Connection.ConnectionId,
                session.ActiveCharacter.Name,
                session.ActiveCharacter.Level,
                index,
                targetChannel.MaxLevel);
            failureReason = 1;
            return false;
        }

        // Validate capacity
        if (targetChannel.CurrentPlayers >= targetChannel.MaxPlayers)
        {
            _logger.LogWarning(
                "[{ConnectionId}] Channel [{Index}] '{Name}' is full ({Current}/{Max}).",
                session.Connection.ConnectionId,
                index,
                targetChannel.Name,
                targetChannel.CurrentPlayers,
                targetChannel.MaxPlayers);
            failureReason = 2;
            return false;
        }

        // Leave existing channel if player was previously in one
        LeaveChannel(session);

        // Add to new channel
        if (!targetChannel.TryAdd(session))
        {
            failureReason = 2;
            return false;
        }

        session.ActiveChannel = targetChannel;

        _logger.LogInformation(
            "[{ConnectionId}] Character '{Name}' entered lobby channel [{Index}] '{ChannelName}' ({Current}/{Max} players).",
            session.Connection.ConnectionId,
            session.ActiveCharacter.Name,
            index,
            targetChannel.Name,
            targetChannel.CurrentPlayers,
            targetChannel.MaxPlayers);

        return true;
    }

    public void LeaveChannel(WorldSession session)
    {
        if (session.ActiveChannel is not null)
        {
            var channel = session.ActiveChannel;
            channel.Remove(session);
            session.ActiveChannel = null;

            _logger.LogInformation(
                "[{ConnectionId}] Character '{Name}' exited lobby channel [{Index}] '{ChannelName}'.",
                session.Connection.ConnectionId,
                session.ActiveCharacter?.Name ?? "Unknown",
                channel.Index,
                channel.Name);
        }
    }
}
