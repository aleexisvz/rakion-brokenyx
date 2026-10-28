using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Rakion.Core.Buddy;
using Rakion.Core.Protocol;
using Rakion.Database.Repositories;
using Rakion.Network.Tcp;
using Rakion.Network.Udp;

namespace Rakion.Buddy;

/// <summary>
/// Messenger server (first stage): login, friend list, adding and removing friends, nickname, empty group list and
/// online / offline presence through the UDP registration. Messages, groups and the TCP tunnel are not implemented.
/// The legacy login does not prove the password (the credential only carries the account and the seed), so the
/// port must stay on a trusted network until the World issues a token for it.
/// </summary>
public sealed class BuddyPacketHandler : ITcpPacketHandler, IUdpPacketHandler
{
    private readonly IBuddyRepository _repository;
    private readonly ILogger<BuddyPacketHandler> _logger;
    private readonly ConcurrentDictionary<Guid, BuddySession> _sessions = new();
    private readonly ConcurrentDictionary<string, BuddySession> _online = new(StringComparer.OrdinalIgnoreCase);

    public BuddyPacketHandler(IBuddyRepository repository, ILogger<BuddyPacketHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public ValueTask OnConnectedAsync(TcpConnection connection, CancellationToken cancellationToken)
    {
        _sessions[connection.ConnectionId] = new BuddySession(connection);
        _logger.LogDebug("[Buddy] Connection from {Remote}.", connection.RemoteEndPoint);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnDisconnectedAsync(TcpConnection connection, Exception? exception)
    {
        if (!_sessions.TryRemove(connection.ConnectionId, out var session) || session.Account is null)
        {
            return;
        }

        if (_online.TryGetValue(session.Account, out var current) && ReferenceEquals(current, session))
        {
            _online.TryRemove(session.Account, out _);
            await NotifyFriendsAsync(session, new BuddyUserState(session.Account, Online: false), CancellationToken.None).ConfigureAwait(false);
        }

        _logger.LogInformation("[Buddy] '{Account}' left the Messenger.", session.Account);
    }

    public async ValueTask OnPacketReceivedAsync(TcpConnection connection, PacketHeader header, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(connection.ConnectionId, out var session))
        {
            return;
        }

        var command = (BuddyOpcode)header.Opcode;
        _logger.LogDebug("[Buddy] '{Account}' sent {Command} ({Length} bytes).", session.Account ?? "?", command, payload.Length);
        switch (command)
        {
            case BuddyOpcode.PreCredentialRequest:
                session.Seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
                await SendAsync(session, new BuddyPreCredentialPacket(session.Seed, (uint)RandomNumberGenerator.GetInt32(int.MaxValue)), cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.LoginRequest:
                await LoginAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.AddBuddyRequest when session.IsAuthenticated:
                await AddFriendAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.RemoveBuddyRequest when session.IsAuthenticated:
                await RemoveFriendAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.UserStateRequest when session.IsAuthenticated:
                await SendUserStateAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.TunnelRequest when session.IsAuthenticated:
                await RelayTunnelAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.SetNickRequest when session.IsAuthenticated:
                await SetNickAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.SetGuildRequest when session.IsAuthenticated:
                await SendAsync(session, new BuddyResultPacket(BuddyOpcode.SetGuildResponse, 0), cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.SetExtUserRequest when session.IsAuthenticated:
                await SendAsync(session, new BuddyResultPacket(BuddyOpcode.SetExtUserResponse, 0), cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.GroupListRequest when session.IsAuthenticated:
                var groups = await _repository.GetGroupsAsync(session.Account!, cancellationToken).ConfigureAwait(false);
                await SendAsync(session, new BuddyGroupListPacket(groups.Select(g => new BuddyGroup((ushort)g.Id, g.Name, (ushort)g.Flags)).ToList()), cancellationToken).ConfigureAwait(false);
                break;
            case BuddyOpcode.GroupAddRequest when session.IsAuthenticated:
                await AddGroupAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;
            default:
                _logger.LogInformation("[Buddy] Unhandled command 0x{Command:X4} from '{Account}' ({Length} bytes).", header.Opcode, session.Account ?? "?", payload.Length);
                break;
        }
    }

    /// <summary>
    /// UDP registration: after the login the client sends its 4-byte token to the same port. The server records the
    /// endpoint it came from, tells the client (0x101F) and announces it to its online friends.
    /// </summary>
    public async ValueTask HandleDatagramAsync(UdpServer server, ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        if (datagram.Length != 4)
        {
            return;
        }

        uint token = BinaryPrimitives.ReadUInt32LittleEndian(datagram.Span);
        var session = _sessions.Values.FirstOrDefault(s => s.IsAuthenticated && s.UdpToken != 0 && s.UdpToken == token);
        if (session is null)
        {
            return;
        }

        session.UdpToken = 0;
        session.PublicEndpoint = new BuddyEndpoint(remoteEndPoint.Address, (ushort)remoteEndPoint.Port);
        _logger.LogInformation("[Buddy] '{Account}' registered UDP {Endpoint}.", session.Account, remoteEndPoint);
        await SendAsync(session, new BuddyVipIpPortPacket(session.PublicEndpoint.Value), cancellationToken).ConfigureAwait(false);

        var friends = await _repository.GetFriendsAsync(session.Account!, cancellationToken).ConfigureAwait(false);
        var onlineFriends = friends
            .Select(f => _online.TryGetValue(f.Account, out var s) && s.IsOnline ? s : null)
            .OfType<BuddySession>()
            .ToList();
        if (onlineFriends.Count > 0)
        {
            var states = onlineFriends.Select(StateOf).ToList();
            await SendAsync(session, new BuddyUserStatePacket(states), cancellationToken).ConfigureAwait(false);
        }

        await NotifyFriendsAsync(session, StateOf(session), cancellationToken).ConfigureAwait(false);
    }

    private async Task LoginAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (session.Seed == 0 || payload.Length != BuddyCrypto.LoginPayloadLength ||
            !BuddyCrypto.TryReadCredential(payload.Span, out string accountName, out uint seed) || seed != session.Seed)
        {
            _logger.LogWarning("[Buddy] Login refused from {Remote}: bad credential ({Length} bytes).", session.Connection.RemoteEndPoint, payload.Length);
            await SendAsync(session, new BuddyLoginResponsePacket(BuddyLoginResponsePacket.BadCredential), ct).ConfigureAwait(false);
            return;
        }

        var key = BuddyCrypto.SessionKey(accountName, seed);
        if (!BuddyCrypto.TryDecryptBlocks(payload.Span[BuddyCrypto.CredentialLength..], key, out byte[] data) ||
            data.Length < BuddyCrypto.LoginDataLength ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != BuddyCrypto.LoginDataHeader)
        {
            _logger.LogWarning("[Buddy] Login refused for '{Account}': the login data does not decrypt.", accountName);
            await SendAsync(session, new BuddyLoginResponsePacket(BuddyLoginResponsePacket.BadCredential), ct).ConfigureAwait(false);
            return;
        }

        var account = await _repository.GetAccountAsync(accountName, ct).ConfigureAwait(false);
        if (account is null)
        {
            await SendAsync(session, new BuddyLoginResponsePacket(BuddyLoginResponsePacket.UnknownAccount), ct).ConfigureAwait(false);
            return;
        }

        // Login data (Buddy2.dll 0x100078C5, Observed offsets): local IPv4 at +0x18 and port at +0x1C, both as the
        // socket reported them (network byte order).
        var localAddress = new IPAddress(data.AsSpan(0x18, 4));
        ushort localPort = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0x1C, 2));

        session.UserId = account.UserId;
        session.Account = account.Account;
        session.DisplayName = account.DisplayName;
        session.LocalEndpoint = new BuddyEndpoint(localAddress, localPort);
        session.UdpToken = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        if (_online.TryGetValue(account.Account, out var previous) && !ReferenceEquals(previous, session))
        {
            await previous.Connection.CloseAsync().ConfigureAwait(false);
        }

        _online[account.Account] = session;
        var friends = await _repository.GetFriendsAsync(account.Account, ct).ConfigureAwait(false);
        var records = friends.Select(f => new BuddyFriend(f.Account, f.DisplayName, f.GroupName)).ToList();
        await SendAsync(session, new BuddyLoginResponsePacket(BuddyLoginResponsePacket.Success, session.UdpToken, records), ct).ConfigureAwait(false);
        _logger.LogInformation("[Buddy] '{Account}' ({Name}) logged in with {Count} friends.", account.Account, account.DisplayName, records.Count);
    }

    private async Task AddFriendAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length < BuddyText.AccountBytes)
        {
            return;
        }

        string friendName = BuddyText.ReadAccount(payload.Span[..BuddyText.AccountBytes]);
        bool added = await _repository.AddFriendAsync(session.Account!, friendName, ct).ConfigureAwait(false);
        var friend = added ? await _repository.GetAccountAsync(friendName, ct).ConfigureAwait(false) : null;
        if (friend is null)
        {
            _logger.LogInformation("[Buddy] '{Account}' could not add '{Friend}'.", session.Account, friendName);
            await SendAsync(session, new BuddyAddResponsePacket(1), ct).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("[Buddy] '{Account}' added '{Friend}'.", session.Account, friend.Account);
        await SendAsync(session, new BuddyAddResponsePacket(0, new BuddyFriend(friend.Account, friend.DisplayName, string.Empty)), ct).ConfigureAwait(false);
        if (_online.TryGetValue(friend.Account, out var friendSession))
        {
            await SendAsync(friendSession, new BuddyAddResponsePacket(0, new BuddyFriend(session.Account!, session.DisplayName, string.Empty)), ct).ConfigureAwait(false);
            if (friendSession.IsOnline && session.IsOnline)
            {
                await SendAsync(session, new BuddyUserStatePacket([StateOf(friendSession)]), ct).ConfigureAwait(false);
                await SendAsync(friendSession, new BuddyUserStatePacket([StateOf(session)]), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RemoveFriendAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length < BuddyText.AccountBytes)
        {
            return;
        }

        string friendName = BuddyText.ReadAccount(payload.Span[..BuddyText.AccountBytes]);
        bool removed = await _repository.RemoveFriendAsync(session.Account!, friendName, ct).ConfigureAwait(false);
        _logger.LogInformation("[Buddy] '{Account}' removed '{Friend}': {Removed}.", session.Account, friendName, removed);
        await SendAsync(session, new BuddyRemoveResponsePacket(removed ? (ushort)0 : (ushort)1, friendName), ct).ConfigureAwait(false);
        if (removed && _online.TryGetValue(friendName, out var friendSession))
        {
            await SendAsync(friendSession, new BuddyRemoveResponsePacket(0, session.Account!), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Stores a group the client created (0x3152 [44-byte group]); the client only logs the 0x3153 result.</summary>
    private async Task AddGroupAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        bool saved = BuddyGroup.TryParse(payload.Span, out var group) &&
            await _repository.SaveGroupAsync(session.Account!, new BuddyGroupRow(group.Id, group.Name, group.Flags), ct).ConfigureAwait(false);
        _logger.LogInformation("[Buddy] '{Account}' created group {Id} '{Name}': {Saved}.", session.Account, group?.Id, group?.Name, saved);
        await SendAsync(session, new BuddyResultPacket(BuddyOpcode.GroupAddResponse, saved ? (ushort)0 : (ushort)1), ct).ConfigureAwait(false);
    }

    private async Task SetNickAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length >= BuddyText.WideBytes)
        {
            string nick = BuddyText.ReadWide(payload.Span[..BuddyText.WideBytes]);
            if (nick.Length > 0)
            {
                session.DisplayName = nick;
                await _repository.SetDisplayNameAsync(session.UserId, nick, ct).ConfigureAwait(false);
                _logger.LogInformation("[Buddy] '{Account}' is now shown as '{Nick}'.", session.Account, nick);
            }
        }

        await SendAsync(session, new BuddyResultPacket(BuddyOpcode.SetNickResponse, 0), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// State of one account (0x3010 [char account[20]], sent by Buddy2.dll 0x10002590 when it cannot reach a player,
    /// for example before a friend request; Observed). The client reads the answer as a normal 0x3FFF notice (no
    /// 0x3011 reader exists in the DLL), and then contacts the player directly or through the tunnel.
    /// </summary>
    private async Task SendUserStateAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length < BuddyText.AccountBytes)
        {
            return;
        }

        string account = BuddyText.ReadAccount(payload.Span[..BuddyText.AccountBytes]);
        var state = _online.TryGetValue(account, out var target) && target.IsOnline
            ? StateOf(target)
            : new BuddyUserState(account, Online: false);
        _logger.LogInformation("[Buddy] '{Account}' asked the state of '{Target}': online {Online}.", session.Account, account, state.Online);
        await SendAsync(session, new BuddyUserStatePacket([state]), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Relays a client-to-client message to each online recipient (0x2020 -> 0x2021). Between players who are not
    /// friends only the friend request and its answer pass.
    /// </summary>
    private async Task RelayTunnelAsync(BuddySession session, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!BuddyTunnelRequest.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[Buddy] Malformed tunnel packet from '{Account}' ({Length} bytes).", session.Account, payload.Length);
            return;
        }

        var friends = (await _repository.GetFriendsAsync(session.Account!, ct).ConfigureAwait(false))
            .Select(f => f.Account)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool friendRequest = request.InnerOpcode is BuddyTunnelRequest.AddBuddyAsk or BuddyTunnelRequest.AddBuddyAnswer;
        var notice = new BuddyTunnelNoticePacket(session.Account!, session.DisplayName, string.Empty, request.InnerOpcode, request.Inner);
        foreach (string recipient in request.Recipients)
        {
            if (!friendRequest && !friends.Contains(recipient))
            {
                continue;
            }

            if (_online.TryGetValue(recipient, out var target))
            {
                await SendAsync(target, notice, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "[Buddy] '{Account}' -> '{Recipient}' tunnel 0x{Inner:X4} ({Length} bytes), delivered {Delivered}.",
                session.Account,
                recipient,
                request.InnerOpcode,
                request.Inner.Length,
                target is not null);
        }
    }

    private async Task NotifyFriendsAsync(BuddySession session, BuddyUserState state, CancellationToken ct)
    {
        var friends = await _repository.GetFriendsAsync(session.Account!, ct).ConfigureAwait(false);
        var notice = new BuddyUserStatePacket([state]);
        foreach (var friend in friends)
        {
            if (_online.TryGetValue(friend.Account, out var friendSession) && friendSession.IsAuthenticated)
            {
                await SendAsync(friendSession, notice, ct).ConfigureAwait(false);
            }
        }
    }

    private static BuddyUserState StateOf(BuddySession session) =>
        new(session.Account!, Online: true, session.PublicEndpoint, session.LocalEndpoint);

    private async Task SendAsync(BuddySession session, IPacket packet, CancellationToken ct)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        await session.Connection.SendAsync(writer.Build(), ct).ConfigureAwait(false);
    }
}
