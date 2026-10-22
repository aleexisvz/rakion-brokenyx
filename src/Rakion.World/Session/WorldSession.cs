using Rakion.Database.Entities;
using Rakion.Network.Tcp;
using Rakion.World.Channels;

namespace Rakion.World.Session;

/// <summary>
/// State tracking for an active client connection on the World Server.
/// </summary>
public sealed class WorldSession : IWorldSessionView
{
    public TcpConnection Connection { get; }
    public DateTime ConnectedAtUtc { get; }
    public DateTime LastKeepAliveUtc { get; set; }
    public UserEntity? User { get; set; }
    public UserGameInfoEntity? GameInfo { get; set; }
    public CharacterEntity? ActiveCharacter { get; set; }
    public LobbyChannel? ActiveChannel { get; set; }
    public bool IsAuthenticated => User is not null;
    public bool GameGuardVerified { get; set; }
    public bool IsEncrypted { get; set; }
    public ushort SequenceId { get; set; }
    public bool IsInField { get; set; }

    /// <summary>UDP probe identity given at WorldLogin (see <see cref="UdpEndpointRegistry"/>); 0 when none.</summary>
    public ushort UdpIndex { get; set; }

    public uint UdpKey { get; set; }

    Guid IWorldSessionView.ConnectionId => Connection.ConnectionId;
    int? IWorldSessionView.UserId => User?.Id;
    string? IWorldSessionView.Username => User?.Username;
    int? IWorldSessionView.CharacterId => ActiveCharacter?.Id;
    string? IWorldSessionView.CharacterName => ActiveCharacter?.Name;
    byte? IWorldSessionView.ChannelIndex => ActiveChannel?.Index;

    public WorldSession(TcpConnection connection)
    {
        Connection = connection;
        ConnectedAtUtc = DateTime.UtcNow;
        LastKeepAliveUtc = DateTime.UtcNow;
    }
}

