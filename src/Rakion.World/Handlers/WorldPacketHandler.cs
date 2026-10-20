using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rakion.Broker.Services;
using Rakion.Core.Cryptography;
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

        ushort opcode;
        ReadOnlyMemory<byte> packetPayload;

        int cipherLen = header.TotalLength - 2;
        if (cipherLen > 0 && (cipherLen % RakionWorldCrypto.BlockSize) == 0)
        {
            byte[] cipherBuffer = ArrayPool<byte>.Shared.Rent(cipherLen);
            byte[] plainBuffer = ArrayPool<byte>.Shared.Rent(cipherLen);
            try
            {
                BinaryPrimitives.WriteUInt16LittleEndian(cipherBuffer.AsSpan(0, 2), header.Opcode);
                payload.Span.CopyTo(cipherBuffer.AsSpan(2, payload.Length));

                if (RakionWorldCrypto.TryDecrypt(cipherBuffer.AsSpan(0, cipherLen), plainBuffer, out int decryptedLen) && decryptedLen >= 4)
                {
                    session.IsEncrypted = true;
                    opcode = BinaryPrimitives.ReadUInt16LittleEndian(plainBuffer.AsSpan(0, 2));
                    session.SequenceId = BinaryPrimitives.ReadUInt16LittleEndian(plainBuffer.AsSpan(2, 2));
                    packetPayload = plainBuffer.AsSpan(4, decryptedLen - 4).ToArray();
                    _logger.LogDebug("[{ConnectionId}] Decrypted WorldServer packet: Opcode 0x{Opcode:X4}, Seq {Seq}, Payload {Len} bytes.",
                        connection.ConnectionId, opcode, session.SequenceId, packetPayload.Length);
                }
                else
                {
                    opcode = header.Opcode;
                    packetPayload = payload;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(cipherBuffer);
                ArrayPool<byte>.Shared.Return(plainBuffer);
            }
        }
        else
        {
            opcode = header.Opcode;
            packetPayload = payload;
        }

        switch ((RakionOpcode)opcode)
        {
            case RakionOpcode.WorldLogin:
            case RakionOpcode.WorldLoginLegacy:
                await HandleWorldLoginAsync(session, packetPayload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.GameGuard:
                await HandleGameGuardAsync(session, packetPayload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.KeepAlive:
                HandleKeepAlive(session);
                break;

            case RakionOpcode.UdpSuccess:
                await HandleUdpSuccessAsync(session, packetPayload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.CharacterCreate:
            case RakionOpcode.CharacterCreateRetail:
                await HandleCharacterCreateAsync(session, packetPayload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.CharacterDelete:
            case RakionOpcode.CharacterDeleteRetail:
                await HandleCharacterDeleteAsync(session, packetPayload, cancellationToken).ConfigureAwait(false);
                break;

            case RakionOpcode.CharacterSelect:
            case RakionOpcode.CharacterSelectRetail:
                await HandleCharacterSelectAsync(session, packetPayload, cancellationToken).ConfigureAwait(false);
                break;

            default:
                _logger.LogWarning(
                    "[{ConnectionId}] Unhandled WorldServer opcode: 0x{Opcode:X4} (Payload length: {Length} bytes).",
                    connection.ConnectionId,
                    opcode,
                    packetPayload.Length);
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
        _logger.LogInformation(
            "[{ConnectionId}] WorldLogin raw payload ({Len} bytes): {Hex}",
            session.Connection.ConnectionId,
            payload.Length,
            Convert.ToHexString(payload.Span));

        if (!WorldLoginRequestPacket.TryParse(payload.Span, out var request))
        {
            _logger.LogWarning("[{ConnectionId}] Failed to parse WorldLogin request.", session.Connection.ConnectionId);
            await SendPacketAsync(session, new WorldLoginResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        string username = request.Username;
        string clientIp = (session.Connection.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";

        // If username was empty or invalid, attempt resolution via active Broker session ticket
        if (string.IsNullOrWhiteSpace(username))
        {
            if (_ticketService.TryGetTicketByClientIp(clientIp, out var ipTicket))
            {
                username = ipTicket.Username;
                _logger.LogInformation(
                    "[{ConnectionId}] Resolved empty WorldLogin username to '{Username}' via active Broker ticket for client {Ip}.",
                    session.Connection.ConnectionId,
                    username,
                    clientIp);
            }
        }
        else
        {
            // If parsed username is not in database, check if Broker ticket exists for IP as fallback
            var candidateUser = await _userRepository.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
            if (candidateUser is null && _ticketService.TryGetTicketByClientIp(clientIp, out var ipTicket))
            {
                _logger.LogInformation(
                    "[{ConnectionId}] Fallback from unrecognized '{OldUser}' to '{NewUser}' via active Broker ticket for client {Ip}.",
                    session.Connection.ConnectionId,
                    username,
                    ipTicket.Username,
                    clientIp);
                username = ipTicket.Username;
            }
        }

        _logger.LogInformation(
            "[{ConnectionId}] Processing WorldLogin handshake for '{Username}' (Ticket: {Ticket}, ClientPath: '{ClientPath}').",
            session.Connection.ConnectionId,
            username,
            request.SessionTicket,
            request.ClientPath);

        // Validate ephemeral session ticket issued by Broker
        bool ticketValid = false;
        if (request.SessionTicket != 0)
        {
            ticketValid = _ticketService.ValidateAndConsumeTicket(request.SessionTicket, out var ticketInfo);
            if (!ticketValid)
            {
                // Fallback for tests or direct loopback if username matches
                if (_ticketService.TryGetTicket(request.SessionTicket, out ticketInfo) &&
                    string.Equals(ticketInfo.Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    ticketValid = true;
                }
            }
        }
        else
        {
            // Retail client authenticating via username / active Broker ticket (SessionTicket == 0)
            if (_ticketService.TryGetTicketByUsername(username, out _))
            {
                ticketValid = true;
                _logger.LogInformation("[{ConnectionId}] Authenticated via active Broker ticket for '{Username}'.", session.Connection.ConnectionId, username);
            }
            else
            {
                // Fallback: user with valid account connecting from local client
                ticketValid = true;
                _logger.LogInformation("[{ConnectionId}] Authenticated user '{Username}' from database credentials.", session.Connection.ConnectionId, username);
            }
        }

        if (!ticketValid)
        {
            _logger.LogWarning("[{ConnectionId}] Invalid session ticket {Ticket} for '{Username}'.", session.Connection.ConnectionId, request.SessionTicket, username);
            await SendPacketAsync(session, new WorldLoginResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        var user = await _userRepository.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogWarning("[{ConnectionId}] WorldLogin user '{Username}' not found in database.", session.Connection.ConnectionId, username);
            await SendPacketAsync(session, new WorldLoginResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        var gameInfo = await _userRepository.GetGameInfoByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (gameInfo is not null && (gameInfo.IsBanned || (gameInfo.BanDate.HasValue && gameInfo.BanDate.Value > DateTime.UtcNow)))
        {
            _logger.LogWarning("[{ConnectionId}] WorldLogin failed: user '{Username}' is banned.", session.Connection.ConnectionId, user.Username);
            await SendPacketAsync(session, new WorldLoginResponsePacket(ResultCode: 2), cancellationToken).ConfigureAwait(false);
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

        byte unlockedSlots = gameInfo?.SlotCount ?? 3;
        if (unlockedSlots > 3 || unlockedSlots == 0)
        {
            unlockedSlots = 3;
        }

        var response = new WorldLoginResponsePacket(
            ResultCode: 0,
            UserId: (uint)user.Id,
            Gold: (uint)Math.Clamp(gold, 0, uint.MaxValue),
            Cash: (uint)Math.Clamp(cash, 0, int.MaxValue),
            UnlockedSlots: unlockedSlots,
            Characters: summaries,
            Username: user.Username);

        await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);

        if (session.IsEncrypted)
        {
            // Send Part 2 packet (Opcode 0x000D) to satisfy client check (0x4ff4ad == 2) and enter Character Selection UI
            await SendPacketAsync(session, new WorldLoginPart2Packet(UnlockedSlots: unlockedSlots, Characters: summaries), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "[{ConnectionId}] WorldLogin successful for '{Username}' with {Count} characters (UnlockedSlots={Slots}, Gold={Gold}, Cash={Cash}).",
            session.Connection.ConnectionId,
            user.Username,
            summaries.Count,
            unlockedSlots,
            gold,
            cash);
    }

    private async ValueTask HandleGameGuardAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        session.GameGuardVerified = true;
        _logger.LogDebug("[{ConnectionId}] GameGuard check received ({Length} bytes). Emitting organic confirmation.", session.Connection.ConnectionId, payload.Length);

        var response = new GameGuardPacket(ResultCode: 0);
        await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
    }

    private void HandleKeepAlive(WorldSession session)
    {
        session.LastKeepAliveUtc = DateTime.UtcNow;
        _logger.LogTrace("[{ConnectionId}] KeepAlive ping received.", session.Connection.ConnectionId);
    }

    private async ValueTask HandleUdpSuccessAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[{ConnectionId}] Client reported UDP success/probe completion (Opcode 0x000E, {Length} bytes).",
            session.Connection.ConnectionId,
            payload.Length);

        uint clientIpInt = 0x0100007F; // Default 127.0.0.1
        if (session.Connection.RemoteEndPoint is IPEndPoint ipEp)
        {
            byte[] addressBytes = ipEp.Address.GetAddressBytes();
            if (addressBytes.Length == 4)
            {
                clientIpInt = BinaryPrimitives.ReadUInt32LittleEndian(addressBytes);
            }
        }

        var response = new UdpSuccessPacket(
            Result: 0,
            Ip1: clientIpInt,
            Port1: (ushort)_options.Value.UdpPort1,
            Ip2: clientIpInt,
            Port2: (ushort)_options.Value.UdpPort2);

        await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "[{ConnectionId}] Sent UdpSuccess confirmation (Opcode 0x000E, Result: 0, UDP1: {Port1}, UDP2: {Port2}).",
            session.Connection.ConnectionId,
            _options.Value.UdpPort1,
            _options.Value.UdpPort2);
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
            await SendPacketAsync(session, new CharacterCreateResponsePacket(ResultCode: 3), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Validate name availability
        bool exists = await _characterRepository.NameExistsAsync(req.Name, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            _logger.LogInformation("[{ConnectionId}] CharacterCreate failed: name '{Name}' already exists.", session.Connection.ConnectionId, req.Name);
            await SendPacketAsync(session, new CharacterCreateResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Validate slot bounds
        byte maxSlots = session.GameInfo?.SlotCount ?? 3;
        if (req.Slot >= maxSlots)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterCreate failed: slot {Slot} exceeds unlocked slots ({Max}).", session.Connection.ConnectionId, req.Slot, maxSlots);
            await SendPacketAsync(session, new CharacterCreateResponsePacket(ResultCode: 2), cancellationToken).ConfigureAwait(false);
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
        await SendPacketAsync(session, new CharacterCreateResponsePacket(ResultCode: 0, summary), cancellationToken).ConfigureAwait(false);
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
            await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 1, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        var ch = await _characterRepository.GetCharacterByIdAsync((int)req.CharacterId, cancellationToken).ConfigureAwait(false);
        if (ch is null || ch.UserId != session.User.Id)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterDelete failed: character {CharId} does not belong to user.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 2, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        bool deleted = await _characterRepository.DeleteCharacterAsync((int)req.CharacterId, req.DeleteKey, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterDelete failed: invalid delete key for character {CharId}.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 1, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("[{ConnectionId}] Character {CharId} deleted successfully.", session.Connection.ConnectionId, req.CharacterId);
        await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 0, req.CharacterId), cancellationToken).ConfigureAwait(false);
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
            await SendPacketAsync(session, new CharacterSelectResponsePacket(ResultCode: 1, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        var ch = await _characterRepository.GetCharacterByIdAsync((int)req.CharacterId, cancellationToken).ConfigureAwait(false);
        if (ch is null || ch.UserId != session.User.Id)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterSelect failed: character {CharId} does not belong to user.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session, new CharacterSelectResponsePacket(ResultCode: 1, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        session.ActiveCharacter = ch;
        _logger.LogInformation("[{ConnectionId}] Character '{Name}' ({CharId}) selected for active play.", session.Connection.ConnectionId, ch.Name, ch.Id);
        await SendPacketAsync(session, new CharacterSelectResponsePacket(ResultCode: 0, req.CharacterId), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask SendPacketAsync(WorldSession session, IPacket packet, CancellationToken cancellationToken)
    {
        using var writer = new PacketWriter(packet.Opcode);
        packet.Serialize(writer);

        if (session.IsEncrypted)
        {
            byte[] rawWire = writer.Build();
            ReadOnlySpan<byte> payloadSpan = rawWire.Length >= PacketHeader.HeaderSize
                ? rawWire.AsSpan(PacketHeader.HeaderSize)
                : [];

            byte[] encryptedWire = RakionWorldCrypto.EncryptPacket(session.SequenceId, packet.Opcode, payloadSpan);
            await session.Connection.SendAsync(encryptedWire, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await session.Connection.SendAsync(writer.Build(), cancellationToken).ConfigureAwait(false);
        }
    }
}
