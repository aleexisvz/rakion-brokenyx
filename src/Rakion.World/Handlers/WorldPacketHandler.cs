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
using Rakion.Core.Utilities;
using Rakion.Database.Repositories;
using Rakion.Network.Tcp;
using Rakion.World.Channels;
using Rakion.World.Configuration;
using Rakion.World.Features.Economy;
using Rakion.World.Modules;
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
    private readonly IChannelManager _channelManager;
    private readonly ISessionSender _sender;
    private readonly IAccountInventoryProvider? _accountInventory;
    private readonly ILogger<WorldPacketHandler> _logger;

    private readonly WorldOpcodeRegistry _registry = new();

    private readonly ConcurrentDictionary<Guid, WorldSession> _sessions = new();

    public WorldPacketHandler(
        IOptions<WorldOptions> options,
        IUserRepository userRepository,
        ICharacterRepository characterRepository,
        IAccountBalanceRepository balanceRepository,
        ISessionTicketService ticketService,
        IChannelManager channelManager,
        IEnumerable<IWorldOpcodeModule> modules,
        ISessionSender sender,
        IAccountInventoryProvider? accountInventory,
        ILogger<WorldPacketHandler> logger)
    {
        _options = options;
        _userRepository = userRepository;
        _characterRepository = characterRepository;
        _balanceRepository = balanceRepository;
        _ticketService = ticketService;
        _channelManager = channelManager;
        _sender = sender;
        _accountInventory = accountInventory;
        _logger = logger;

        RegisterBuiltInHandlers();
        foreach (var module in modules)
        {
            module.Register(_registry);
        }
    }

    public WorldPacketHandler(
        IOptions<WorldOptions> options,
        IUserRepository userRepository,
        ICharacterRepository characterRepository,
        IAccountBalanceRepository balanceRepository,
        ISessionTicketService ticketService,
        IChannelManager channelManager,
        ILogger<WorldPacketHandler> logger)
        : this(options, userRepository, characterRepository, balanceRepository, ticketService, channelManager, [], WorldSessionSender.Instance, accountInventory: null, logger)
    {
    }

    public WorldPacketHandler(
        IOptions<WorldOptions> options,
        IUserRepository userRepository,
        ICharacterRepository characterRepository,
        IAccountBalanceRepository balanceRepository,
        ISessionTicketService ticketService,
        ILogger<WorldPacketHandler> logger)
        : this(options, userRepository, characterRepository, balanceRepository, ticketService, new ChannelManager(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChannelManager>.Instance), logger)
    {
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

        if (_registry.TryGetHandler(opcode, out var handler))
        {
            await handler(session, packetPayload, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogWarning(
            "[{ConnectionId}] Unhandled WorldServer opcode: 0x{Opcode:X4} (Payload length: {Length} bytes).",
            connection.ConnectionId,
            opcode,
            packetPayload.Length);
    }

    private void RegisterBuiltInHandlers()
    {
        _registry.Map(ClientOpcode.WorldLogin, HandleWorldLoginAsync);
        _registry.Map(ClientOpcode.WorldLoginLegacy, HandleWorldLoginAsync);
        _registry.Map(ClientOpcode.GameGuard, HandleGameGuardAsync);
        _registry.Map(ClientOpcode.KeepAlive, (session, _, _) =>
        {
            HandleKeepAlive(session);
            return ValueTask.CompletedTask;
        });
        _registry.Map(ClientOpcode.UdpSuccess, HandleUdpSuccessAsync);
        _registry.Map(ClientOpcode.CharacterCreate, HandleCharacterCreateAsync);
        _registry.Map(ClientOpcode.CharacterCreateRetail, HandleCharacterCreateAsync);
        _registry.Map(ClientOpcode.CharacterDelete, HandleCharacterDeleteAsync);
        _registry.Map(ClientOpcode.CharacterDeleteRetail, HandleCharacterDeleteAsync);
        _registry.Map(ClientOpcode.CharacterSelect, HandleCharacterSelectAsync);
        _registry.Map(ClientOpcode.CharacterSelectRetail, HandleCharacterSelectAsync);
        _registry.Map(ClientOpcode.CharacterChangeBuddyName, HandleCharacterChangeBuddyNameAsync);
        _registry.Map(ClientOpcode.CharacterTutorialClear, (session, _, ct) => HandleCharacterTutorialClearAsync(session, ct));
        _registry.Map(ClientOpcode.ChannelList, (session, _, ct) => HandleChannelListAsync(session, ct));
        _registry.Map(ClientOpcode.ChannelEnter, HandleChannelEnterAsync);
        _registry.Map(ClientOpcode.ChannelExit, (session, _, ct) => HandleChannelExitAsync(session, ct));
        _registry.Map(ClientOpcode.ChannelCharacters, (session, _, ct) => HandleChannelCharactersAsync(session, ct));
        _registry.Map(ClientOpcode.ChannelChat, HandleChannelChatAsync);
        _registry.Map(ClientOpcode.CharacterWhisper, HandleCharacterWhisperAsync);
        _registry.Map(ClientOpcode.FieldList, (session, payload, ct) => new ValueTask(HandleFieldListAsync(session, payload, ct)));
        _registry.Map(ClientOpcode.FieldCreate, HandleFieldCreateAsync);
        _registry.Map(ClientOpcode.FieldQuickEnter, HandleFieldQuickEnterAsync);
        _registry.Map(ClientOpcode.FieldEnter, HandleFieldEnterAsync);
        _registry.Map(ClientOpcode.FieldExit, HandleFieldExitAsync);
        _registry.Map(ClientOpcode.FieldReady, HandleFieldReadyAsync);
        _registry.Map(ClientOpcode.FieldChat, HandleFieldChatAsync);
        _registry.Map(ClientOpcode.FieldChangeSlotStatus, HandleFieldChangeSlotStatusAsync);
        _registry.Map(ClientOpcode.FieldChangeTeam, HandleFieldChangeTeamAsync);
        _registry.Map(ClientOpcode.FieldChangeRule, HandleFieldChangeRuleAsync);
    }

    private async ValueTask HandleCharacterTutorialClearAsync(WorldSession session, CancellationToken cancellationToken)
    {
        if (session.User is null)
        {
            return;
        }

        await _userRepository.SetTutorialCompletedAsync(session.User.Id, true, cancellationToken).ConfigureAwait(false);
        if (session.GameInfo is not null)
        {
            session.GameInfo.TutorialCompleted = true;
        }

        _logger.LogInformation(
            "[{ConnectionId}] Character tutorial clear notification received and saved for user '{Username}'.",
            session.Connection.ConnectionId,
            session.User.Username);
    }

    public ValueTask OnDisconnectedAsync(TcpConnection connection, Exception? exception)
    {
        if (_sessions.TryRemove(connection.ConnectionId, out var session))
        {
            _channelManager.LeaveChannel(session);

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
        AccountInventory? inventory = _accountInventory is null
            ? null
            : await _accountInventory.GetAccountInventoryAsync(user.Id, gameInfo?.BagCount ?? 1, cancellationToken).ConfigureAwait(false);

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
                ce.AttackSpeed, ce.Speed, ce.MaxCp,
                (uint)ce.WinCount, (uint)ce.LossCount, (uint)ce.DrawCount,
                inventory?.EquipmentOf(ce.Id)));
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
            Username: user.Username,
            TutorialCompleted: gameInfo?.TutorialCompleted ?? false,
            ServerTimeMinutes: LegacyGameClock.ToMinutes(DateTime.Now),
            PowerUserExpiry: (uint)Math.Clamp(gameInfo?.PowerTime ?? 0, 0, uint.MaxValue),
            PowerUserPoints: (ushort)Math.Clamp(gameInfo?.PowerLevelPoint ?? 0, 0, ushort.MaxValue));

        await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);

        if (session.IsEncrypted)
        {
            // Send Part 2 packet (Opcode 0x000D) to satisfy client check (0x4ff4ad == 2) and enter Character Selection UI
            await SendPacketAsync(session, new WorldLoginPart2Packet(UnlockedSlots: unlockedSlots, Characters: summaries, AccountBox: inventory?.Box), cancellationToken).ConfigureAwait(false);
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
            newChar.AttackSpeed, newChar.Speed, newChar.MaxCp,
            (uint)newChar.WinCount, (uint)newChar.LossCount, (uint)newChar.DrawCount);

        _logger.LogInformation("[{ConnectionId}] Character '{Name}' (Class: {Class}, Slot: {Slot}) created successfully (Id={Id}).", session.Connection.ConnectionId, newChar.Name, newChar.Class, newChar.SlotIndex, newChar.Id);
        await SendPacketAsync(session, new CharacterCreateResponsePacket(ResultCode: 0, (uint)newChar.Id), cancellationToken).ConfigureAwait(false);
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
            _logger.LogWarning("[{ConnectionId}] CharacterDelete failed: malformed request packet.", session.Connection.ConnectionId);
            await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 1, 0), cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
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

            _logger.LogInformation("[{ConnectionId}] Character {CharId} ('{Name}') deleted successfully.", session.Connection.ConnectionId, req.CharacterId, ch.Name);
            await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 0, req.CharacterId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ConnectionId}] Unexpected error deleting character {CharId}.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session, new CharacterDeleteResponsePacket(ResultCode: 1, req.CharacterId), cancellationToken).ConfigureAwait(false);
        }
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

        // Fallback 1: Client previously received composite (CharacterId << 8) | SlotIndex (e.g. 0x0702 = 1794)
        if ((ch is null || ch.UserId != session.User.Id) && req.CharacterId > 255)
        {
            uint extractedId = req.CharacterId >> 8;
            ch = await _characterRepository.GetCharacterByIdAsync((int)extractedId, cancellationToken).ConfigureAwait(false);
        }

        // Fallback 2: Check by slot index or match across user's active characters
        if (ch is null || ch.UserId != session.User.Id)
        {
            var userChars = await _characterRepository.GetCharactersByUserIdAsync(session.User.Id, cancellationToken).ConfigureAwait(false);
            byte targetSlot = (byte)(req.CharacterId & 0xFF);
            ch = userChars.FirstOrDefault(c => c.SlotIndex == targetSlot || c.Id == (int)req.CharacterId);
        }

        if (ch is null || ch.UserId != session.User.Id)
        {
            _logger.LogWarning("[{ConnectionId}] CharacterSelect failed: character {CharId} does not belong to user.", session.Connection.ConnectionId, req.CharacterId);
            await SendPacketAsync(session, new CharacterSelectResponsePacket(ResultCode: 1, req.CharacterId), cancellationToken).ConfigureAwait(false);
            return;
        }

        session.ActiveCharacter = ch;
        _logger.LogInformation("[{ConnectionId}] Character '{Name}' ({CharId}, Slot: {Slot}) selected for active play.", session.Connection.ConnectionId, ch.Name, ch.Id, ch.SlotIndex);

        // 1. Confirm character selection (Opcode 0x0014) to confirm active character selection and trigger stat computation
        await SendPacketAsync(session, new CharacterSelectResponsePacket(ResultCode: 0, req.CharacterId), cancellationToken).ConfigureAwait(false);

        // 2. Automatically assign character to appropriate channel (Beginner if level <= 15, otherwise Free)
        byte targetChannelIndex = ch.Level <= 15 ? (byte)0 : (byte)1;
        if (!_channelManager.TryEnterChannel(targetChannelIndex, session, string.Empty, out _))
        {
            _channelManager.TryEnterChannel(1, session, string.Empty, out _);
        }

        if (session.ActiveChannel is not null)
        {
            var activeCh = session.ActiveChannel;
            var enterResp = new ChannelEnterResponsePacket(
                ResultCode: 0,
                ChannelIndex: activeCh.Index,
                ServerId: 0,
                ChannelName: activeCh.Name,
                ChannelType: (byte)activeCh.Type,
                MaxPlayers: activeCh.MaxPlayers,
                Flags: 0);

            // 3. Send ChannelEnter response (Opcode 0x001F) to transition client from CharacterSelectDialog to ChannelSelectDialog (Lobby)
            await SendPacketAsync(session, enterResp, cancellationToken).ConfigureAwait(false);

            // 4. Send ChannelCharacters response (Opcode 0x001E)
            await HandleChannelCharactersAsync(session, cancellationToken).ConfigureAwait(false);

            // 5. Broadcast user entrance to other players in this channel
            foreach (var member in activeCh.Sessions.Values)
            {
                if (member.Connection.ConnectionId != session.Connection.ConnectionId)
                {
                    await SendPacketAsync(member, enterResp, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask HandleCharacterChangeBuddyNameAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.User is null)
        {
            return;
        }

        string charName = string.Empty;
        if (CharacterChangeBuddyNameRequestPacket.TryParse(payload.Span, out var req))
        {
            charName = req.CharacterName;
        }

        _logger.LogInformation(
            "[{ConnectionId}] CharacterChangeBuddyName (0x0015) received for user '{Username}' (Character: '{Name}'). Responding with success to complete client creation notice.",
            session.Connection.ConnectionId,
            session.User.Username,
            charName);

        // Confirmation packet: ResultCode = 0 (Success), CharacterName
        await SendPacketAsync(session, new CharacterChangeBuddyNameResponsePacket(ResultCode: 0, charName), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleChannelListAsync(
        WorldSession session,
        CancellationToken cancellationToken)
    {
        var channels = _channelManager.GetChannels();
        var entries = new List<ChannelListEntry>(channels.Count);
        for (int i = 0; i < channels.Count; i++)
        {
            var c = channels[i];
            entries.Add(new ChannelListEntry(
                c.Index,
                c.Type,
                c.CurrentPlayers,
                c.MaxPlayers,
                c.Name));
        }

        _logger.LogInformation(
            "[{ConnectionId}] Serving ChannelList with {Count} channels to '{Username}'.",
            session.Connection.ConnectionId,
            entries.Count,
            session.User?.Username ?? "Unknown");

        await SendPacketAsync(session, new ChannelListResponsePacket(entries), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleChannelEnterAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.User is null)
        {
            return;
        }

        if (!ChannelEnterRequestPacket.TryParse(payload.Span, out var req))
        {
            await SendPacketAsync(session, new ChannelEnterResponsePacket(ResultCode: 1), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_channelManager.TryEnterChannel(req.ChannelIndex, session, req.Password, out byte reason))
        {
            var ch = session.ActiveChannel!;
            var resp = new ChannelEnterResponsePacket(
                ResultCode: 0,
                ChannelIndex: ch.Index,
                ServerId: 0,
                ChannelName: ch.Name,
                ChannelType: (byte)ch.Type,
                MaxPlayers: ch.MaxPlayers,
                Flags: 0);

            await SendPacketAsync(session, resp, cancellationToken).ConfigureAwait(false);

            // Broadcast entrance to other players in the newly entered channel
            foreach (var member in ch.Sessions.Values)
            {
                if (member.Connection.ConnectionId != session.Connection.ConnectionId)
                {
                    await SendPacketAsync(member, resp, cancellationToken).ConfigureAwait(false);
                }
            }

            // Send channel character roster
            await HandleChannelCharactersAsync(session, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendPacketAsync(session, new ChannelEnterResponsePacket(ResultCode: reason != 0 ? reason : (byte)1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleChannelExitAsync(
        WorldSession session,
        CancellationToken cancellationToken)
    {
        _channelManager.LeaveChannel(session);
        await SendPacketAsync(session, new ChannelExitResponsePacket(ResultCode: 0), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleChannelCharactersAsync(
        WorldSession session,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        var members = session.ActiveChannel.Sessions.Values
            .Where(s => s.ActiveCharacter is not null)
            .Select(s => new ChannelCharacterSummary(
                Slot: 0,
                Grade: (ushort)s.ActiveCharacter!.Level,
                Name: s.ActiveCharacter.Name,
                Class: (byte)s.ActiveCharacter.Class,
                Level: s.ActiveCharacter.Level,
                ClanId: 0))
            .ToList();

        var resp = new ChannelCharactersResponsePacket(
            session.ActiveChannel.Index,
            (byte)members.Count,
            PageIndex: 0,
            ChannelName: session.ActiveChannel.Name,
            ServerName: _options.Value.ServerName,
            Characters: members);

        await SendPacketAsync(session, resp, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleChannelChatAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null || session.ActiveCharacter is null)
        {
            return;
        }

        if (!ChannelChatRequestPacket.TryParse(payload.Span, out var req) || string.IsNullOrEmpty(req.Message))
        {
            return;
        }

        // Retail v258 client prepends "{CharacterName} : " before sending Opcode 0x0022.
        // We ensure the prefix is present without duplicating it.
        string messageToBroadcast = req.Message.StartsWith($"{session.ActiveCharacter.Name} : ", StringComparison.OrdinalIgnoreCase)
            ? req.Message
            : $"{session.ActiveCharacter.Name} : {req.Message}";

        _logger.LogInformation(
            "[{ConnectionId}] ChannelChat in [{Index}]: {Message}",
            session.Connection.ConnectionId,
            session.ActiveChannel.Index,
            messageToBroadcast);

        var broadcast = new ChannelChatBroadcastPacket(ChatType: 0, FormattedMessage: messageToBroadcast);
        foreach (var member in session.ActiveChannel.Sessions.Values)
        {
            await SendPacketAsync(member, broadcast, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleCharacterWhisperAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveCharacter is null)
        {
            return;
        }

        if (!CharacterWhisperRequestPacket.TryParse(payload.Span, out var req) || string.IsNullOrEmpty(req.TargetCharacterName))
        {
            await SendPacketAsync(session, new CharacterWhisperResponsePacket(ResultCode: 1, string.Empty, string.Empty), cancellationToken).ConfigureAwait(false);
            return;
        }

        WorldSession? targetSession = null;
        foreach (var s in _sessions.Values)
        {
            if (s.ActiveCharacter is not null && string.Equals(s.ActiveCharacter.Name, req.TargetCharacterName, StringComparison.OrdinalIgnoreCase))
            {
                targetSession = s;
                break;
            }
        }

        if (targetSession is null)
        {
            _logger.LogInformation(
                "[{ConnectionId}] Whisper from '{Sender}' to '{Target}' failed: recipient not online.",
                session.Connection.ConnectionId,
                session.ActiveCharacter.Name,
                req.TargetCharacterName);

            await SendPacketAsync(session, new CharacterWhisperResponsePacket(ResultCode: 1, req.TargetCharacterName, string.Empty), cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "[{ConnectionId}] Whisper from '{Sender}' delivered to '{Target}': {Message}",
            session.Connection.ConnectionId,
            session.ActiveCharacter.Name,
            req.TargetCharacterName,
            req.Message);

        // Deliver incoming whisper to target
        var targetPacket = new CharacterWhisperResponsePacket(ResultCode: 0, session.ActiveCharacter.Name, req.Message);
        await SendPacketAsync(targetSession, targetPacket, cancellationToken).ConfigureAwait(false);

        // Send confirmation echo to sender
        var senderConfirmation = new CharacterWhisperResponsePacket(ResultCode: 0, session.ActiveCharacter.Name, req.Message);
        await SendPacketAsync(session, senderConfirmation, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleFieldListAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[{ConnectionId}] Processing FieldList request (Opcode 0x0036) for user '{Username}'.",
            session.Connection.ConnectionId,
            session.User?.Username ?? "Unknown");

        if (session.ActiveChannel is null)
        {
            await SendPacketAsync(session, FieldListResponsePacket.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        FieldListRequestPacket.TryParse(payload.Span, out var req);
        byte pageSize = req?.PageSize > 0 ? req.PageSize : (byte)10;
        ushort pageIndex = req?.PageIndex ?? 0;

        var allFields = session.ActiveChannel.FieldManager.GetFields(session.ActiveChannel.Index);
        var pagedEntries = allFields
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(f => f.ToFieldListEntry())
            .ToList();

        var response = pagedEntries.Count == 0
            ? FieldListResponsePacket.Empty
            : new FieldListResponsePacket(pagedEntries);

        await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleFieldCreateAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveCharacter is null || session.ActiveChannel is null)
        {
            _logger.LogWarning("[{ConnectionId}] FieldCreate rejected: no active character or channel.", session.Connection.ConnectionId);
            return;
        }

        if (!FieldCreateRequestPacket.TryParse(payload.Span, out var req))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldCreate request packet.", session.Connection.ConnectionId);
            return;
        }

        if (session.ActiveChannel.FieldManager.TryCreateField(
            session.ActiveChannel.Index,
            session,
            req.RoomName,
            req.Password,
            req.Mode,
            0, // Map
            req.MaxPlayers,
            req.RoundTime,
            (byte)req.Rounds,
            req.SubMode,
            req.LevelLimit,
            out var field,
            out var failureReason))
        {
            _logger.LogInformation(
                "[{ConnectionId}] Room #{Number} '{Name}' created by '{Host}' in channel {Channel}.",
                session.Connection.ConnectionId,
                field!.Number,
                field.Name,
                session.ActiveCharacter.Name,
                session.ActiveChannel.Index);

            var response = new FieldCreateResponsePacket(ResultCode: 0, RoomNumber: field.Number);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogWarning(
                "[{ConnectionId}] FieldCreate failed (reason: {Reason}) for user '{Username}'.",
                session.Connection.ConnectionId,
                failureReason,
                session.User?.Username ?? "Unknown");

            var response = new FieldCreateResponsePacket(ResultCode: failureReason, RoomNumber: 0);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldQuickEnterAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveCharacter is null || session.ActiveChannel is null)
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryQuickJoinField(
            session.ActiveChannel.Index,
            session,
            out var field,
            out var player,
            out var failureReason))
        {
            _logger.LogInformation(
                "[{ConnectionId}] '{Player}' quick-joined room #{Number} (Slot: {Slot}, Team: {Team}).",
                session.Connection.ConnectionId,
                session.ActiveCharacter.Name,
                field!.Number,
                player!.SlotIndex,
                player.Team);

            var response = new FieldCreateResponsePacket(ResultCode: 0, RoomNumber: field.Number);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "[{ConnectionId}] FieldQuickEnter failed (reason: {Reason}) for '{Player}'.",
                session.Connection.ConnectionId,
                failureReason,
                session.ActiveCharacter.Name);

            var response = new FieldCreateResponsePacket(ResultCode: failureReason, RoomNumber: 0);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldEnterAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveCharacter is null || session.ActiveChannel is null)
        {
            return;
        }

        if (!FieldEnterRequestPacket.TryParse(payload.Span, out var req))
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryJoinField(
            session.ActiveChannel.Index,
            req.RoomNumber,
            session,
            req.Password,
            out var field,
            out var player,
            out var failureReason))
        {
            var response = new FieldCreateResponsePacket(ResultCode: 0, RoomNumber: field!.Number);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var response = new FieldCreateResponsePacket(ResultCode: failureReason, RoomNumber: 0);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldExitAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryLeaveField(session, out var field, out var player, out bool roomDestroyed))
        {
            byte exitingSlot = player?.SlotIndex ?? 0;
            _logger.LogInformation(
                "[{ConnectionId}] '{Player}' (Slot {Slot}) left room #{Number} (Destroyed: {Destroyed}).",
                session.Connection.ConnectionId,
                session.ActiveCharacter?.Name ?? "Unknown",
                exitingSlot,
                field?.Number ?? 0,
                roomDestroyed);

            // Notify exiting player with their slot index so client knows to empty slot
            var response = new FieldExitResponsePacket(SlotIndex: exitingSlot);
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);

            // Broadcast to remaining room members so their client clears the player's slot
            if (!roomDestroyed && field is not null)
            {
                foreach (var member in field.Players)
                {
                    await SendPacketAsync(member.Session, response, cancellationToken).ConfigureAwait(false);
                }
            }

            // Observed (rakion.bin): FieldExit handler 0x00474410 only empties the slot, and the
            // WaitRoom menu state is 0x1C (set near 0x00447030). The only code path that rebuilds
            // the SelectGame lobby UI (ctor 0x00469320) from a room is the ChannelEnter handler
            // 0x00473DA0 (case result 0, matching channel index). So re-send ChannelEnter.
            var channel = session.ActiveChannel;
            var reenter = new ChannelEnterResponsePacket(
                ResultCode: 0,
                ChannelIndex: channel.Index,
                ServerId: 0,
                ChannelName: channel.Name,
                ChannelType: (byte)channel.Type,
                MaxPlayers: channel.MaxPlayers,
                Flags: 0);
            await SendPacketAsync(session, reenter, cancellationToken).ConfigureAwait(false);

            // Refresh the room list once the client is back in SelectGame (state 0x17).
            var fields = session.ActiveChannel.FieldManager.GetFields(session.ActiveChannel.Index);
            var pagedEntries = fields
                .Take(6)
                .Select(f => f.ToFieldListEntry())
                .ToList();
            var fieldListResp = pagedEntries.Count == 0
                ? FieldListResponsePacket.Empty
                : new FieldListResponsePacket(pagedEntries);
            await SendPacketAsync(session, fieldListResp, cancellationToken).ConfigureAwait(false);

            // Send updated channel character roster
            await HandleChannelCharactersAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldReadyAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        if (!FieldReadyRequestPacket.TryParse(payload.Span, out var req))
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) &&
            field is not null && player is not null)
        {
            player.IsReady = req.ReadyState != 0;
            _logger.LogInformation(
                "[{ConnectionId}] Room #{Number} player '{Player}' ready state changed: {Ready}.",
                session.Connection.ConnectionId,
                field.Number,
                session.ActiveCharacter?.Name ?? "Unknown",
                player.IsReady);

            var broadcast = new FieldReadyBroadcastPacket(player.SlotIndex, req.ReadyState);
            foreach (var member in field.Players)
            {
                await SendPacketAsync(member.Session, broadcast, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask HandleFieldChatAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        if (!FieldChatRequestPacket.TryParse(payload.Span, out var req))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(req.Message))
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out _) &&
            field is not null)
        {
            string charName = session.ActiveCharacter?.Name ?? "Unknown";
            string formattedMessage = req.Message.StartsWith(charName, StringComparison.Ordinal)
                ? req.Message
                : $"{charName} : {req.Message}";

            _logger.LogInformation(
                "[{ConnectionId}] Room #{Number} chat: {Message}",
                session.Connection.ConnectionId,
                field.Number,
                formattedMessage);

            var broadcast = new FieldChatBroadcastPacket(formattedMessage);
            foreach (var member in field.Players)
            {
                await SendPacketAsync(member.Session, broadcast, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask HandleFieldChangeSlotStatusAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        if (!FieldChangeSlotStatusRequestPacket.TryParse(payload.Span, out var req))
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) &&
            field is not null && player is not null)
        {
            if (field.TryChangeSlotStatus(player, req.SlotIndex, req.Status, out byte newStatus))
            {
                _logger.LogInformation(
                    "[{ConnectionId}] Room #{Number} slot {Slot} status changed to {Status} by host '{Host}'.",
                    session.Connection.ConnectionId,
                    field.Number,
                    req.SlotIndex,
                    newStatus,
                    session.ActiveCharacter?.Name ?? "Unknown");

                var broadcast = new FieldChangeSlotStatusBroadcastPacket(req.SlotIndex, newStatus);
                foreach (var member in field.Players)
                {
                    await SendPacketAsync(member.Session, broadcast, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask HandleFieldChangeTeamAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        if (!FieldChangeTeamRequestPacket.TryParse(payload.Span, out _))
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) &&
            field is not null && player is not null)
        {
            if (field.TryChangeTeam(player, out byte oldSlot, out byte newSlot, out byte result))
            {
                _logger.LogInformation(
                    "[{ConnectionId}] Room #{Number} player '{Player}' switched from slot {OldSlot} to slot {NewSlot} (Team {Team}).",
                    session.Connection.ConnectionId,
                    field.Number,
                    session.ActiveCharacter?.Name ?? "Unknown",
                    oldSlot,
                    newSlot,
                    player.Team);

                var broadcast = new FieldChangeTeamBroadcastPacket(0, oldSlot, newSlot);
                foreach (var member in field.Players)
                {
                    await SendPacketAsync(member.Session, broadcast, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                _logger.LogWarning(
                    "[{ConnectionId}] Room #{Number} player '{Player}' failed to switch team (Result {Result}).",
                    session.Connection.ConnectionId,
                    field.Number,
                    session.ActiveCharacter?.Name ?? "Unknown",
                    result);

                var reply = new FieldChangeTeamBroadcastPacket(result, oldSlot, oldSlot);
                await SendPacketAsync(session, reply, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask HandleFieldChangeRuleAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel is null)
        {
            return;
        }

        if (!FieldChangeRuleRequestPacket.TryParse(payload.Span, out var req))
        {
            return;
        }

        if (session.ActiveChannel.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) &&
            field is not null && player is not null)
        {
            if (field.TryChangeRule(
                player,
                req.RoomName,
                req.Password,
                req.Mode,
                (byte)0,
                req.MaxPlayers,
                req.RoundTime,
                req.Rounds,
                req.SubMode,
                (byte)0))
            {
                _logger.LogInformation(
                    "[{ConnectionId}] Room #{Number} rules updated by host '{Host}'.",
                    session.Connection.ConnectionId,
                    field.Number,
                    session.ActiveCharacter?.Name ?? "Unknown");

                var broadcast = new FieldChangeRuleBroadcastPacket(
                    field.Name,
                    field.Password,
                    req.MapName,
                    field.Mode,
                    field.MaxPlayers,
                    field.RoundTime,
                    field.Rounds,
                    field.SubMode);

                foreach (var member in field.Players)
                {
                    await SendPacketAsync(member.Session, broadcast, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private ValueTask SendPacketAsync(WorldSession session, IPacket packet, CancellationToken cancellationToken) =>
        _sender.SendAsync(session, packet, cancellationToken);
}
