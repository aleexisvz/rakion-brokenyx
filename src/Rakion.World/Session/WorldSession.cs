using Rakion.Database.Entities;
using Rakion.Network.Tcp;

namespace Rakion.World.Session;

/// <summary>
/// State tracking for an active client connection on the World Server.
/// </summary>
public sealed class WorldSession
{
    public TcpConnection Connection { get; }
    public DateTime ConnectedAtUtc { get; }
    public DateTime LastKeepAliveUtc { get; set; }
    public UserEntity? User { get; set; }
    public UserGameInfoEntity? GameInfo { get; set; }
    public CharacterEntity? ActiveCharacter { get; set; }
    public bool IsAuthenticated => User is not null;
    public bool GameGuardVerified { get; set; }

    public WorldSession(TcpConnection connection)
    {
        Connection = connection;
        ConnectedAtUtc = DateTime.UtcNow;
        LastKeepAliveUtc = DateTime.UtcNow;
    }
}

