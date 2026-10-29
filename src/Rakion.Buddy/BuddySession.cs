using Rakion.Core.Buddy;
using Rakion.Network.Tcp;

namespace Rakion.Buddy;

/// <summary>State of one Messenger connection.</summary>
public sealed class BuddySession
{
    public BuddySession(TcpConnection connection)
    {
        Connection = connection;
    }

    public TcpConnection Connection { get; }

    /// <summary>Seed sent in the pre-credential answer; the login credential must carry it.</summary>
    public uint Seed { get; set; }

    public int UserId { get; set; }

    public string? Account { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>One-use token the client sends by UDP to register its endpoint.</summary>
    public uint UdpToken { get; set; }

    /// <summary>The endpoint the server saw the UDP registration come from.</summary>
    public BuddyEndpoint? PublicEndpoint { get; set; }

    /// <summary>The local endpoint the client reported in its login.</summary>
    public BuddyEndpoint? LocalEndpoint { get; set; }

    public bool IsAuthenticated => Account is not null;

    public bool IsOnline => IsAuthenticated && PublicEndpoint is not null;
}
