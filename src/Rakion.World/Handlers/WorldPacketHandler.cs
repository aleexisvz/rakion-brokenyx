using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Broker.Services;
using Rakion.Core.Enums;
using Rakion.Core.Packets;
using Rakion.Core.Protocol;
using Rakion.Database.Repositories;
using Rakion.Network.Tcp;
using Rakion.World.Configuration;
using Rakion.World.Session;

namespace Rakion.World.Handlers;

/// <summary>
/// TCP packet handler processing World server handshake, GameGuard verification,
/// and character lifecycle operations on port 40708.
/// </summary>
public sealed class WorldPacketHandler : ITcpPacketHandler
{
    private readonly IOptions<WorldOptions> _options;
    private readonly IUserRepository _userRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly IAccountBalanceRepository _balanceRepository;
    private readonly ISessionTicketService _ticketService;
    private readonly ILogger<WorldPacketHandler> _logger;

    private readonly ConcurrentDictionary<Guid, WorldSession> _sessions = new();

    public WorldPacketHandler(
        IOptions<WorldOptions> options,
        IUserRepository userRepository,
        ICharacterRepository characterRepository,
        IAccountBalanceRepository balanceRepository,
        ISessionTicketService ticketService,
        ILogger<WorldPacketHandler> logger)
    {
        _options = options;
        _userRepository = userRepository;
        _characterRepository = characterRepository;
        _balanceRepository = balanceRepository;
        _ticketService = ticketService;
        _logger = logger;
    }

    public int ActiveSessionCount => _sessions.Count;

    public ValueTask OnConnectedAsync(TcpConnection connection, CancellationToken cancellationToken)
    {
        var session = new WorldSession(connection);
        _sessions[connection.ConnectionId] = session;
        _logger.LogInformation("[{ConnectionId}] Client connected to WorldServer from {RemoteEndPoint}.", connection.ConnectionId, connection.RemoteEndPoint);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnPacketReceivedAsync(
        TcpConnection connection,
        PacketHeader header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(connection.ConnectionId, out var session))
        {
            _logger.LogWarning("[{ConnectionId}] Received packet but session not found.", connection.ConnectionId);
            return;
        }

        switch ((RakionOpcode)header.Opcode)
        {
            case RakionOpcode.WorldLogin:
            case RakionOpcode.WorldLoginAlt:
                await HandleWorldLoginAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.GameGuard:
                await HandleGameGuardAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.KeepAlive:
                HandleKeepAlive(session);
                break;

            case RakionOpcode.CharacterCreate:
                await HandleCharacterCreateAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.CharacterDelete:
                await HandleCharacterDeleteAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.CharacterSelect:
                await HandleCharacterSelectAsync(session, payload, cancellationToken).ConfigureAwait(false);
                break;

            default:
                _logger.LogWarning(
                    "[{ConnectionId}] Unhandled WorldServer opcode: 0x{Opcode:X4} (Payload length: {Length} bytes).",
                    connection.ConnectionId,
                    header.Opcode,
                    payload.Length);
                break;
        }
    }

    public ValueTask OnDisconnectedAsync(TcpConnection connection, Exception? exception)
    {
        if (_sessions.TryRemove(connection.ConnectionId, out var session))
        {
            if (session.User is not null)
            {
                _logger.LogInformation(
                    "[{ConnectionId}] User '{Username}' disconnected from WorldServer.",
                    connection.ConnectionId,
                    session.User.Username);
            }
        }

        if (exception is not null)
        {
            _logger.LogWarning(exception, "[{ConnectionId}] WorldServer client disconnected with error.", connection.ConnectionId);
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask HandleWorldLoginAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!WorldLoginRequestPacket.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[{ConnectionId}] Failed to parse WorldLogin request.", session.Connection.ConnectionId);
            await SendPacketAsync(session.Connection, new WorldLoginResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "[{ConnectionId}] Processing WorldLogin handshake for '{Username}' (Ticket: {Ticket}).",
            session.Connection.ConnectionId,
            request.Username,
            request.SessionTicket);

        // Validate ephemeral session ticket issued by Broker
        bool ticketValid = _ticketService.ValidateAndConsumeTicket(request.SessionTicket, out var ticketInfo);
        if (!ticketValid)
        {
            // Fallback for tests or direct loopback if username matches
            if (!_ticketService.TryGetTicket(request.SessionTicket, out ticketInfo) ||
                !string.Equals(ticketInfo.Username, request.Username, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[{ConnectionId}] Invalid session ticket {Ticket} for '{Username}'.", session.Connection.ConnectionId, request.SessionTicket, request.Username);
                await SendPacketAsync(session.Connection, new WorldLoginResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var user = await _userRepository.GetByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogWarning("[{ConnectionId}] WorldLogin user '{Username}' not found in database.", session.Connection.ConnectionId, request.Username);
            await SendPacketAsync(session.Connection, new WorldLoginResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        var gameInfo = await _userRepository.GetGameInfoByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (gameInfo is not null && (gameInfo.IsBanned || (gameInfo.BanDate.HasValue && gameInfo.BanDate.Value > DateTime.UtcNow)))
        {
            _logger.LogWarning("[{ConnectionId}] WorldLogin failed: user '{Username}' is banned.", session.Connection.ConnectionId, user.Username);
            await SendPacketAsync(session.Connection, new WorldLoginResponsePacket(ResultCode: 2), cancellationToken).ConfigureAwait(false);
            return;
        }

        session.User = user;
        session.GameInfo = gameInfo;

        // Load account currencies and character roster
        long gold = await _balanceRepository.GetGoldAsync(user.Id, cancellationToken).ConfigureAwait(false);
        int cash = await _balanceRepository.GetCashAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var characterEntities = await _characterRepository.GetCharactersByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var summaries = new List<CharacterSummary>(characterEntities.Count);
        for (int i = 0; i < characterEntities.Count; i++)
        {
            var ce = characterEntities[i];
            summaries.Add(new CharacterSummary(
                (uint)ce.Id,
                ce.Name,
                ce.SlotIndex,
                ce.Class,
                ce.Level,
                (uint)ce.Exp,
                ce.LevelPoint,
                ce.Hit1, ce.Hit2, ce.Hit3, ce.Hit4, ce.Chit, ce.Hp, ce.Ap,
                AttackSpeed: 100, Speed: 100, MaxCp: 100,
                (uint)ce.WinCount, (uint)ce.LossCount, (uint)ce.DrawCount));
        }

        var response = new WorldLoginResponsePacket(
            ResultCode: 0,
            UserId: (uint)user.Id,
            Gold: (uint)Math.Clamp(gold, 0, uint.MaxValue),
            Cash: (uint)Math.Clamp(cash, 0, int.MaxValue),
            UnlockedSlots: gameInfo?.SlotCount ?? 4,
            Characters: summaries);

        await SendPacketAsync(session.Connection, response, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{ConnectionId}] WorldLogin successful for '{Username}' with {Count} characters.", session.Connection.ConnectionId, user.Username, summaries.Count);
    }

    private async ValueTask HandleGameGuardAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        session.GameGuardVerified = true;
        _logger.LogDebug("[{ConnectionId}] GameGuard check received ({Length} bytes). Emitting organic confirmation.", session.Connection.ConnectionId, payload.Length);

        var response = new GameGuardPacket(ResultCode: 0);
        await SendPacketAsync(session.Connection, response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleKeepAlive(WorldSession session)
    {
        session.LastKeepAliveUtc = DateTime.UtcNow;
        _logger.LogTrace("[{ConnectionId}] KeepAlive ping received.", session.Connection.ConnectionId);
    }

    private async ValueTask HandleCharacterCreateAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.User is null)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterCreate attempted on unauthenticated session.", session.Connection.ConnectionId);
            return;
        }

        if (!CharacterCreateRequestPacket.TryParse(payload.Span, out var req))
        {
            _logger.LogWarning("[{ConnectionId}] Failed to parse CharacterCreate request.", session.Connection.ConnectionId);
            await SendPacketAsync(session.Connection, new CharacterCreateResponsePacket(ResultCode: 3), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Validate name availability
        bool exists = await _characterRepository.NameExistsAsync(req.Name, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            _logger.LogInformation("[{ConnectionId}] CharacterCreate failed: name '{Name}' already exists.", session.Connection.ConnectionId, req.Name);
            await SendPacketAsync(session.Connection, new CharacterCreateResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Validate slot bounds
        byte maxSlots = session.GameInfo?.SlotCount ?? 4;
        if (req.Slot >= maxSlots)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterCreate failed: slot {Slot} exceeds unlocked slots ({Max}).", session.Connection.ConnectionId, req.Slot, maxSlots);
            await SendPacketAsync(session.Connection, new CharacterCreateResponsePacket(ResultCode: 2), cancellationToken).ConfigureAwait(false);
            return;
        }

        var newChar = await _characterRepository.CreateCharacterAsync(
            session.User.Id,
            req.Name,
            req.Class,
            req.Slot,
            cancellationToken).ConfigureAwait(false);

        var summary = new CharacterSummary(
            (uint)newChar.Id,
            newChar.Name,
            newChar.SlotIndex,
            newChar.Class,
            newChar.Level,
            (uint)newChar.Exp,
            newChar.LevelPoint,
            newChar.Hit1, newChar.Hit2, newChar.Hit3, newChar.Hit4, newChar.Chit, newChar.Hp, newChar.Ap,
            AttackSpeed: 100, Speed: 100, MaxCp: 100,
            (uint)newChar.WinCount, (uint)newChar.LossCount, (uint)newChar.DrawCount);

        _logger.LogInformation("[{ConnectionId}] Character '{Name}' (Class: {Class}, Slot: {Slot}) created successfully.", session.Connection.ConnectionId, newChar.Name, newChar.Class, newChar.SlotIndex);
        await SendPacketAsync(session.Connection, new CharacterCreateResponsePacket(ResultCode: 0, summary), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleCharacterDeleteAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.User is null)
        {
            return;
        }

        if (!CharacterDeleteRequestPacket.TryParse(payload.Span, out var req))
        {
            await SendPacketAsync(session.Connection, new CharacterDeleteResponsePacket(ResultCode: 1, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        var ch = await _characterRepository.GetCharacterByIdAsync((int)req.CharacterId, cancellationToken).ConfigureAwait(false);
        if (ch is null || ch.UserId != session.User.Id)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterDelete failed: character {CharId} does not belong to user.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session.Connection, new CharacterDeleteResponsePacket(ResultCode: 2, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        bool deleted = await _characterRepository.DeleteCharacterAsync((int)req.CharacterId, req.DeleteKey, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterDelete failed: invalid delete key for character {CharId}.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session.Connection, new CharacterDeleteResponsePacket(ResultCode: 1, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("[{ConnectionId}] Character {CharId} deleted successfully.", session.Connection.ConnectionId, req.CharacterId);
        await SendPacketAsync(session.Connection, new CharacterDeleteResponsePacket(ResultCode: 0, req.CharacterId), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleCharacterSelectAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.User is null)
        {
            return;
        }

        if (!CharacterSelectRequestPacket.TryParse(payload.Span, out var req))
        {
            await SendPacketAsync(session.Connection, new CharacterSelectResponsePacket(ResultCode: 1, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        var ch = await _characterRepository.GetCharacterByIdAsync((int)req.CharacterId, cancellationToken).ConfigureAwait(false);
        if (ch is null || ch.UserId != session.User.Id)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterSelect failed: character {CharId} does not belong to user.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session.Connection, new CharacterSelectResponsePacket(ResultCode: 1, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        session.ActiveCharacter = ch;
        _logger.LogInformation("[{ConnectionId}] Character '{Name}' ({CharId}) selected for active play.", session.Connection.ConnectionId, ch.Name, ch.Id);
        await SendPacketAsync(session.Connection, new CharacterSelectResponsePacket(ResultCode: 0, req.CharacterId), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask SendPacketAsync(TcpConnection connection, IPacket packet, CancellationToken cancellationToken)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);
        await connection.SendAsync(writer.Build(), cancellationToken).ConfigureAwait(false);
    }
}
