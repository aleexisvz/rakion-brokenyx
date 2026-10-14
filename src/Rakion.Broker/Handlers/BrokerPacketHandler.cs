using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Broker.Configuration;
using Rakion.Broker.Services;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Repositories;
using Rakion.Database.Security;
using Rakion.Network.Tcp;

namespace Rakion.Broker.Handlers;

/// <summary>
/// Packet handler processing client handshake, server list requests, and authentication on TCP port 40706.
/// </summary>
public sealed class BrokerPacketHandler : ITcpPacketHandler
{
    private readonly IOptions<BrokerOptions> _options;
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISessionTicketService _ticketService;
    private readonly ILogger<BrokerPacketHandler> _logger;

    public BrokerPacketHandler(
        IOptions<BrokerOptions> options,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ISessionTicketService ticketService,
        ILogger<BrokerPacketHandler> logger)
    {
        _options = options;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _ticketService = ticketService;
        _logger = logger;
    }

    public ValueTask OnConnectedAsync(TcpConnection connection, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{ConnectionId}] Client connected from {RemoteEndPoint}.", connection.ConnectionId, connection.RemoteEndPoint);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnPacketReceivedAsync(
        TcpConnection connection,
        PacketHeader header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[{ConnectionId}] Packet received: Opcode=0x{Opcode:X4}, Length={Length}, Hex={Hex}",
            connection.ConnectionId,
            header.Opcode,
            payload.Length,
            Convert.ToHexString(payload.Span));

        switch ((RakionOpcode)header.Opcode)
        {
            case RakionOpcode.ServerListRequest:
                if (payload.Length >= 2)
                {
                    ushort subOpcode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Span);
                    if (subOpcode == (ushort)RakionOpcode.LoginAlt2 || subOpcode == (ushort)RakionOpcode.LoginAlt3 || subOpcode == (ushort)RakionOpcode.LoginAlt4)
                    {
                        _logger.LogInformation(
                            "[{ConnectionId}] NyxLauncher secondary login handshake (0x{SubOpcode:X4}) received via 0xCDDF. Sending CL_AUTH_LOGIN_2 success.",
                            connection.ConnectionId,
                            subOpcode);

                        await SendLoginResponseAsync(connection, subOpcode, LoginResultCode.Success, null, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                }

                await HandleServerListRequestAsync(connection, payload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.Login:
            case RakionOpcode.LoginAlt2:
            case RakionOpcode.LoginAlt3:
            case RakionOpcode.LoginAlt4:
            case RakionOpcode.None:
                await HandleLoginRequestAsync(connection, header.Opcode, payload, cancellationToken).ConfigureAwait(false);
                break;

            default:
                _logger.LogWarning(
                    "[{ConnectionId}] Unknown opcode received: 0x{Opcode:X4} (Payload length: {Length} bytes).",
                    connection.ConnectionId,
                    header.Opcode,
                    payload.Length);
                break;
        }
    }

    public ValueTask OnDisconnectedAsync(TcpConnection connection, Exception? exception)
    {
        if (exception is not null)
        {
            _logger.LogWarning(exception, "[{ConnectionId}] Client disconnected with error.", connection.ConnectionId);
        }
        else
        {
            _logger.LogInformation("[{ConnectionId}] Client disconnected normally.", connection.ConnectionId);
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask HandleServerListRequestAsync(
        TcpConnection connection,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("[{ConnectionId}] Processing ServerListRequest (0xCDDF).", connection.ConnectionId);

        var configuredServers = _options.Value.GameServers;
        var entries = new List<GameServerEntry>(configuredServers.Count);

        for (int i = 0; i < configuredServers.Count; i++)
        {
            var cfg = configuredServers[i];
            IPAddress ip = IPAddress.TryParse(cfg.Ip, out var parsedIp) ? parsedIp : IPAddress.Loopback;

            entries.Add(new GameServerEntry(
                cfg.Id > 0 ? cfg.Id : (ushort)(i + 1),
                cfg.Name,
                ip,
                cfg.Port,
                UsedSalas: 0,
                MaxSalas: cfg.MaxSalas,
                UsedSlots: 0,
                MaxSlots: cfg.MaxSlots,
                IsOnline: cfg.IsOnline));
        }

        var responsePacket = new ServerListResponsePacket(entries);
        using var writer = new PacketWriter(responsePacket.Opcode);
        responsePacket.Serialize(writer);

        await connection.SendAsync(writer.Build(), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{ConnectionId}] Sent ServerListResponse (0x0101) with {Count} servers.", connection.ConnectionId, entries.Count);
    }

    private async ValueTask HandleLoginRequestAsync(
        TcpConnection connection,
        ushort headerOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!LoginRequestPacket.TryParse(payload.Span, headerOpcode, out var loginPacket))
        {
            _logger.LogWarning("[{ConnectionId}] Failed to parse login packet with opcode 0x{Opcode:X4}.", connection.ConnectionId, headerOpcode);
            await SendLoginResponseAsync(connection, headerOpcode, LoginResultCode.InvalidCredentials, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "[{ConnectionId}] Processing login request for user '{Username}' (ClientVersion: {Version}, SubOpcode: 0x{SubOpcode:X4}).",
            connection.ConnectionId,
            loginPacket.Username,
            loginPacket.ClientVersion,
            loginPacket.SubOpcode);

        // NyxLauncher SV_AUTH_LOGIN (0x1310) pre-authentication handshake
        if (loginPacket.IsLauncherHandshake)
        {
            if (!string.IsNullOrWhiteSpace(loginPacket.Username))
            {
                var launcherUser = await _userRepository.GetByUsernameAsync(loginPacket.Username, cancellationToken).ConfigureAwait(false);
                if (launcherUser is null)
                {
                    _logger.LogWarning("[{ConnectionId}] Launcher login failed: user '{Username}' not found.", connection.ConnectionId, loginPacket.Username);
                    await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.InvalidCredentials, null, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var launcherGameInfo = await _userRepository.GetGameInfoByUserIdAsync(launcherUser.Id, cancellationToken).ConfigureAwait(false);
                if (launcherGameInfo is not null && (launcherGameInfo.IsBanned || (launcherGameInfo.BanDate.HasValue && launcherGameInfo.BanDate.Value > DateTime.UtcNow)))
                {
                    _logger.LogWarning(
                        "[{ConnectionId}] Launcher login failed: account '{Username}' is banned (BanDate: {BanDateUtc}, Reason: {Reason}).",
                        connection.ConnectionId,
                        loginPacket.Username,
                        launcherGameInfo.BanDate,
                        launcherGameInfo.BanReason ?? "No reason specified");
                    await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.AccountBanned, null, cancellationToken).ConfigureAwait(false);
                    return;
                }

                string launcherClientIp = connection.RemoteEndPoint?.ToString() ?? loginPacket.ClientIp;
                uint launcherTicket = _ticketService.IssueTicket(launcherUser.Username, launcherClientIp);

                _logger.LogInformation(
                    "[{ConnectionId}] Launcher SV_AUTH_LOGIN (0x1310) accepted for '{Username}'. Issued session ticket: {Ticket}. Sending CL_AUTH_LOGIN (0x1312) success.",
                    connection.ConnectionId,
                    loginPacket.Username,
                    launcherTicket);
            }

            await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.Success, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        // NyxLauncher Start Game SV_AUTH_LOGIN_4 (0x4000) for "Rakion"
        if (loginPacket.SubOpcode == (ushort)RakionOpcode.LoginAlt4 &&
            (string.IsNullOrEmpty(loginPacket.Password) || loginPacket.Username.Equals("Rakion", StringComparison.OrdinalIgnoreCase)))
        {
            string startGameClientIp = connection.RemoteEndPoint?.ToString() ?? loginPacket.ClientIp;
            uint startGameTicket = _ticketService.IssueTicket("admin", startGameClientIp);

            _logger.LogInformation(
                "[{ConnectionId}] NyxLauncher Start Game SV_AUTH_LOGIN_4 (0x4000) accepted for target '{Target}'. Issued session ticket: {Ticket}. Sending CL_AUTH_LOGIN_4 (0x4001).",
                connection.ConnectionId,
                loginPacket.Username,
                startGameTicket);

            await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.Success, startGameTicket, cancellationToken).ConfigureAwait(false);
            return;
        }

        var user = await _userRepository.GetByUsernameAsync(loginPacket.Username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogWarning("[{ConnectionId}] Login failed: user '{Username}' not found.", connection.ConnectionId, loginPacket.Username);
            await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.InvalidCredentials, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_passwordHasher.VerifyPassword(loginPacket.Password, user.PasswordHash, out bool needsRehash))
        {
            _logger.LogWarning("[{ConnectionId}] Login failed: invalid password for user '{Username}'.", connection.ConnectionId, loginPacket.Username);
            await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.InvalidCredentials, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (needsRehash)
        {
            string upgradedHash = _passwordHasher.HashPassword(loginPacket.Password);
            await _userRepository.UpdatePasswordHashAsync(user.Id, upgradedHash, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("[{ConnectionId}] Upgraded legacy password hash to BCrypt for user '{Username}'.", connection.ConnectionId, user.Username);
        }

        var gameInfo = await _userRepository.GetGameInfoByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (gameInfo is not null && (gameInfo.IsBanned || (gameInfo.BanDate.HasValue && gameInfo.BanDate.Value > DateTime.UtcNow)))
        {
            _logger.LogWarning(
                "[{ConnectionId}] Login failed: account '{Username}' is banned (BanDate: {BanDateUtc}, Reason: {Reason}).",
                connection.ConnectionId,
                loginPacket.Username,
                gameInfo.BanDate,
                gameInfo.BanReason ?? "No reason specified");
            await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.AccountBanned, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Authentication successful: update last connect and issue one-time session ticket for World server
        await _userRepository.UpdateLastConnectAsync(user.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);

        string clientIp = connection.RemoteEndPoint?.ToString() ?? loginPacket.ClientIp;
        uint ticket = _ticketService.IssueTicket(user.Username, clientIp);

        _logger.LogInformation(
            "[{ConnectionId}] User '{Username}' authenticated successfully. Issued session ticket: {Ticket}.",
            connection.ConnectionId,
            user.Username,
            ticket);

        await SendLoginResponseAsync(connection, loginPacket.SubOpcode, LoginResultCode.Success, ticket, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask SendLoginResponseAsync(
        TcpConnection connection,
        ushort requestSubOpcode,
        LoginResultCode result,
        uint? sessionTicket,
        CancellationToken cancellationToken)
    {
        var response = LoginResponsePacket.Create(requestSubOpcode, result, sessionTicket);
        using var writer = new PacketWriter(response.HeaderOpcode);
        response.Serialize(writer);

        await connection.SendAsync(writer.Build(), cancellationToken).ConfigureAwait(false);
    }
}
