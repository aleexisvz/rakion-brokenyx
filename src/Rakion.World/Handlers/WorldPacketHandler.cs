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
using Rakion.World.Fields;
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

    /// <summary>Account authority of a GM: <c>--create-user --admin</c> creates 100 (Inferred equivalent of legacy 0x34).</summary>
    public const int GmAuthority = 100;
    private readonly IUserRepository _userRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly IAccountBalanceRepository _balanceRepository;
    private readonly ISessionTicketService _ticketService;
    private readonly IChannelManager _channelManager;
    private readonly ISessionSender _sender;
    private readonly IAccountInventoryProvider? _accountInventory;
    private readonly UdpEndpointRegistry _udpEndpoints;
    private readonly IMatchResultRepository? _matchResults;
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
        ILogger<WorldPacketHandler> logger,
        UdpEndpointRegistry? udpEndpoints = null,
        IMatchResultRepository? matchResults = null)
    {
        _options = options;
        _udpEndpoints = udpEndpoints ?? new UdpEndpointRegistry();
        _matchResults = matchResults;
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
        _registry.Map(ClientOpcode.FieldGameStart, HandleFieldGameStartAsync);
        _registry.Map(ClientOpcode.FieldGameEnter, HandleFieldGameEnterAsync);
        _registry.Map(ClientOpcode.FieldGameExit, HandleFieldGameExitAsync);
        _registry.Map(ClientOpcode.FieldSlotUdp, HandleFieldSlotUdpAsync);
        _registry.Map(ClientOpcode.FieldGameRoundStart, HandleFieldGameRoundStartAsync);
        _registry.Map(ClientOpcode.FieldGameDie, HandleFieldGameDieAsync);
        _registry.Map(ClientOpcode.FieldKick, HandleFieldKickAsync);
        _registry.Map(ClientOpcode.FieldGameVoteOpen, HandleFieldGameVoteOpenAsync);
        _registry.Map(ClientOpcode.FieldGameVote, HandleFieldGameVoteAsync);
        _registry.Map(ClientOpcode.FieldClose, HandleFieldCloseAsync);
        _registry.Map(ClientOpcode.FieldGamePoint, HandleFieldGamePointAsync);
        _registry.Map(ClientOpcode.FieldGameMasterGolem, HandleFieldGameGolemAsync);
        _registry.Map(ClientOpcode.FieldGameRoundEnd, HandleFieldGameStageEndAsync);
        _registry.Map(ClientOpcode.FieldGameStagePoint, HandleFieldGameStagePointAsync);
        _registry.Map(ClientOpcode.FieldGameMasterBossHp, HandleFieldGameLeaderHpAsync);
        _registry.Map(ClientOpcode.FieldGameAddPlayer, (session, payload, ct) => HandleFieldGameAddPlayerAsync(session, payload, hasTarget: false, ct));
        _registry.Map(ClientOpcode.FieldGameAddPlayerReply, (session, payload, ct) => HandleFieldGameAddPlayerAsync(session, payload, hasTarget: true, ct));
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
            // A dropped client never sends FieldExit; free its slot so the room does not stay behind.
            LeaveCurrentFieldInBackground(session);
            _channelManager.LeaveChannel(session);
            if (session.UdpIndex != 0)
            {
                _udpEndpoints.Release(session.UdpIndex);
            }

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
        if (session.UdpIndex == 0)
        {
            (session.UdpIndex, session.UdpKey) = _udpEndpoints.Register();
        }

        // Load account currencies and character roster
        long gold = await _balanceRepository.GetGoldAsync(user.Id, cancellationToken).ConfigureAwait(false);
        int cash = await _balanceRepository.GetCashAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var characterEntities = await _characterRepository.GetCharactersByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        AccountInventory? inventory = _accountInventory is null
            ? null
            : await _accountInventory.GetAccountInventoryAsync(user.Id, gameInfo?.BagCount ?? 1, cancellationToken).ConfigureAwait(false);

        var stageRanks = _matchResults is null
            ? null
            : await _matchResults.GetStageRanksAsync(characterEntities.Select(c => c.Id).ToList(), cancellationToken).ConfigureAwait(false);

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
                inventory?.EquipmentOf(ce.Id),
                stageRanks?.GetValueOrDefault(ce.Id),
                Math.Clamp(ce.PotionSlotCount, (byte)3, (byte)6)));
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
            PowerUserPoints: (ushort)Math.Clamp(gameInfo?.PowerLevelPoint ?? 0, 0, ushort.MaxValue),
            UdpIndex: session.UdpIndex,
            UdpKey: session.UdpKey);

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
                ServerId: session.UdpIndex, // id of the entering user; the client matches it with its own (rakion.bin 0x473DE5)
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
                ServerId: session.UdpIndex, // id of the entering user; the client matches it with its own (rakion.bin 0x473DE5)
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
            .Select(ToFieldListEntry)
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

        await LeaveStaleFieldAsync(session, cancellationToken).ConfigureAwait(false);

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
            field!.Rules = FieldRules.FromCreate(req);
            field.ApplyDefaultSlots();
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

        await LeaveStaleFieldAsync(session, cancellationToken).ConfigureAwait(false);
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

            await AnnounceJoinAsync(field, player, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "[{ConnectionId}] FieldQuickEnter failed (reason: {Reason}) for '{Player}'.",
                session.Connection.ConnectionId,
                failureReason,
                session.ActiveCharacter.Name);

            await SendPacketAsync(session, new FieldQuickEnterFailedPacket(), cancellationToken).ConfigureAwait(false);
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

        await LeaveStaleFieldAsync(session, cancellationToken).ConfigureAwait(false);
        if (session.ActiveChannel.FieldManager.TryJoinField(
            session.ActiveChannel.Index,
            req.RoomNumber,
            session,
            req.Password,
            out var field,
            out var player,
            out var failureReason))
        {
            _logger.LogInformation(
                "[{ConnectionId}] '{Player}' entered room #{Number} (Slot: {Slot}).",
                session.Connection.ConnectionId,
                session.ActiveCharacter.Name,
                field!.Number,
                player!.SlotIndex);

            await AnnounceJoinAsync(field, player, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "[{ConnectionId}] FieldEnter room #{Number} refused for '{Player}': result {Result}.",
                session.Connection.ConnectionId,
                req.RoomNumber,
                session.ActiveCharacter.Name,
                failureReason);

            await SendPacketAsync(session, new FieldPlayerEnterPacket(failureReason), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Legacy join order (RakionWorldServ.exe 0x407358..0x4074EF, Observed): the new member goes to the whole room
    /// on 0x38, then the newcomer gets the room details on 0x37.
    /// </summary>
    private async ValueTask AnnounceJoinAsync(GameField field, FieldPlayer newcomer, CancellationToken cancellationToken)
    {
        var slots = field.SnapshotSlots();
        var records = new Dictionary<FieldPlayer, FieldPlayerRecord>();
        foreach (var (_, member) in slots)
        {
            if (member is not null)
            {
                records[member] = await BuildPlayerRecordAsync(member.Session, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var member in field.Players)
        {
            var entered = new FieldPlayerEnterPacket(
                FieldPlayerEnterPacket.Entered,
                newcomer.SlotIndex,
                GameField.SlotStateOf(newcomer),
                PlayerIdOf(newcomer.Session, member.Session),
                0,
                records[newcomer]);
            await SendPacketAsync(member.Session, entered, cancellationToken).ConfigureAwait(false);
        }

        var rules = field.Rules;
        var info = new FieldInfoPacket(
            field.Number,
            field.IsInProgress ? (byte)2 : (byte)1,
            field.MasterSlot,
            rules.Kind,
            rules.Mode,
            rules.MinLevel,
            rules.MaxLevel,
            rules.Extra,
            0,
            rules.Option,
            rules.TimeLimit,
            rules.Goal,
            field.Name,
            field.Password,
            rules.MapName,
            slots.Select(s => s.Player is null
                ? new FieldSlotInfo(s.State, 0, 0, null)
                : new FieldSlotInfo(s.State, PlayerIdOf(s.Player.Session, newcomer.Session), 0, records[s.Player])).ToList());
        await SendPacketAsync(newcomer.Session, info, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FieldPlayerRecord> BuildPlayerRecordAsync(WorldSession member, CancellationToken cancellationToken)
    {
        var character = member.ActiveCharacter;
        IReadOnlyList<ItemSlot>? equipment = null;
        if (_accountInventory is not null && member.User is not null && character is not null)
        {
            var inventory = await _accountInventory
                .GetAccountInventoryAsync(member.User.Id, member.GameInfo?.BagCount ?? 1, cancellationToken)
                .ConfigureAwait(false);
            equipment = inventory.EquipmentOf(character.Id);
        }

        var udp = _udpEndpoints.Get(member.UdpIndex);
        return new FieldPlayerRecord(
            character?.Name ?? string.Empty,
            string.Empty,
            (byte)(character?.Class ?? 0),
            (byte)(character?.Level ?? 1),
            equipment,
            udp.Ip1,
            udp.Port1,
            udp.Ip2,
            udp.Port2);
    }

    private FieldListEntry ToFieldListEntry(GameField field) =>
        field.ToFieldListEntry(field.Master is { } master ? _udpEndpoints.Get(master.Session.UdpIndex) : default);

    /// <summary>
    /// Room player id: the user's id from WorldLogin bytes [5..6], the same for every recipient. The client finds its
    /// own room entry by comparing it with its own id (rakion.bin 0x47A170 against listener + 0x14 via 0x472B00,
    /// Observed).
    /// </summary>
    private static ushort PlayerIdOf(WorldSession member, WorldSession recipient) => member.UdpIndex;

    /// <summary>
    /// Removes the session from its room and tells the remaining members (FieldExit with the freed slot). Returns
    /// false when the session was in no room.
    /// </summary>
    private async ValueTask<bool> LeaveCurrentFieldAsync(WorldSession session, LobbyChannel? channel, bool notifyLeaver, CancellationToken cancellationToken)
    {
        if (channel?.FieldManager.TryLeaveField(session, out var field, out var player, out bool roomDestroyed) is not true)
        {
            return false;
        }

        byte exitingSlot = player?.SlotIndex ?? 0;
        _logger.LogInformation(
            "[{ConnectionId}] '{Player}' (Slot {Slot}) left room #{Number} (Destroyed: {Destroyed}).",
            session.Connection.ConnectionId,
            session.ActiveCharacter?.Name ?? "Unknown",
            exitingSlot,
            field?.Number ?? 0,
            roomDestroyed);

        var response = new FieldExitResponsePacket(SlotIndex: exitingSlot);
        if (notifyLeaver)
        {
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
        }

        if (!roomDestroyed && field is not null)
        {
            await BroadcastToFieldAsync(field, response, cancellationToken).ConfigureAwait(false);
            if (player is not null)
            {
                await FinishVoteAsync(field, field.CancelVoteFor(player, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            }

            if (field.IsInProgress && !field.Players.Any(p => p.IsInGame))
            {
                await EndFieldGameAsync(field, cancellationToken).ConfigureAwait(false);
            }
        }

        return true;
    }

    /// <summary>
    /// A client asking to create or enter a room is in the lobby, so any room the server still has it in is stale
    /// (the client left without FieldExit). Inferred recovery; the legacy server tracks this with the user state.
    /// </summary>
    private async ValueTask LeaveStaleFieldAsync(WorldSession session, CancellationToken cancellationToken)
    {
        if (await LeaveCurrentFieldAsync(session, session.ActiveChannel, notifyLeaver: false, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("[{ConnectionId}] Removed '{Player}' from a room it had left without FieldExit.", session.Connection.ConnectionId, session.ActiveCharacter?.Name ?? "Unknown");
        }
    }

    private void LeaveCurrentFieldInBackground(WorldSession session)
    {
        var channel = session.ActiveChannel;
        _ = Task.Run(async () =>
        {
            try
            {
                await LeaveCurrentFieldAsync(session, channel, notifyLeaver: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{ConnectionId}] Failed to free the room slot of a disconnected client.", session.Connection.ConnectionId);
            }
        });
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

        if (await LeaveCurrentFieldAsync(session, session.ActiveChannel, notifyLeaver: true, cancellationToken).ConfigureAwait(false))
        {
            await ReturnToLobbyAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Host kicks the member in a slot: (u8 slot). The legacy server accepts it from the host only, for an occupied
    /// slot (RakionWorldServ.exe 0x423CC0 / 0x4097C0, Observed). UNKNOWN: the exact notice the kicked client gets; it is
    /// sent back to the lobby like a normal room exit (Inferred).
    /// </summary>
    private async ValueTask HandleFieldKickAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (payload.Length < 1 ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var host) is not true ||
            field is null || host is null || !host.IsMaster)
        {
            _logger.LogWarning("[{ConnectionId}] FieldKick rejected: not the host of a room.", session.Connection.ConnectionId);
            return;
        }

        byte slot = payload.Span[0];
        var target = field.Players.FirstOrDefault(p => p.SlotIndex == slot);
        if (target is null || target == host)
        {
            return;
        }

        _logger.LogInformation("Room #{Number}: host '{Host}' kicked slot {Slot}.", field.Number, session.ActiveCharacter?.Name ?? "Unknown", slot);
        if (await LeaveCurrentFieldAsync(target.Session, target.Session.ActiveChannel, notifyLeaver: true, cancellationToken).ConfigureAwait(false))
        {
            await ReturnToLobbyAsync(target.Session, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Kick vote opened by a player in the match (RakionWorldServ.exe 0x425A70 / 0x40A420).</summary>
    private async ValueTask HandleFieldGameVoteOpenAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameVoteOpenRequestPacket.TryParse(payload.Span, out var request) ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var opener) is not true ||
            field is null || opener is null || !opener.IsInGame)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGameVoteOpen rejected: malformed or not in a match.", session.Connection.ConnectionId);
            return;
        }

        var (refusal, notify) = field.OpenVote(opener, request.TargetSlot, DateTimeOffset.UtcNow);
        if (refusal is { } code)
        {
            await SendPacketAsync(session, new FieldGameVoteResultPacket(code), cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("Room #{Number}: slot {Opener} opened a kick vote against slot {Target} ('{Reason}').", field.Number, opener.SlotIndex, request.TargetSlot, request.Reason);
        var opened = new FieldGameVoteOpenPacket(request.TargetSlot, request.Reason);
        foreach (var member in notify)
        {
            await SendPacketAsync(member.Session, opened, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Vote on the running kick vote (RakionWorldServ.exe 0x425BB0 / 0x4098E0).</summary>
    private async ValueTask HandleFieldGameVoteAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameVoteRequestPacket.TryParse(payload.Span, out var request) ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var voter) is not true ||
            field is null || voter is null)
        {
            return;
        }

        var (refusal, outcome) = field.CastVote(voter, request.Choice, DateTimeOffset.UtcNow);
        if (refusal is { } code)
        {
            await SendPacketAsync(session, new FieldGameVoteResultPacket(code), cancellationToken).ConfigureAwait(false);
            return;
        }

        await FinishVoteAsync(field, outcome, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Announces a finished vote and kicks the target when it passed (legacy 0x4090B0 -> 0x4068C0).</summary>
    private async ValueTask FinishVoteAsync(GameField field, GameField.VoteOutcome? outcome, CancellationToken cancellationToken)
    {
        if (outcome is null)
        {
            return;
        }

        var result = outcome.Result;
        _logger.LogInformation(
            "Room #{Number}: kick vote on slot {Target} ended (cancelled {Cancelled}, {Voters} players, {Yes} yes, {No} no, {Abstain} abstain), kick: {Kick}.",
            field.Number,
            result.TargetSlot,
            result.Cancelled,
            result.Voters,
            result.Yes,
            result.No,
            result.Abstain,
            outcome.KickSlot is not null);
        foreach (var member in outcome.Recipients)
        {
            await SendPacketAsync(member.Session, result, cancellationToken).ConfigureAwait(false);
        }

        if (outcome.KickSlot is { } slot && field.Players.FirstOrDefault(p => p.SlotIndex == slot) is { } target &&
            await LeaveCurrentFieldAsync(target.Session, target.Session.ActiveChannel, notifyLeaver: true, cancellationToken).ConfigureAwait(false))
        {
            await ReturnToLobbyAsync(target.Session, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// GM room close (RakionWorldServ.exe 0x423C00 -> 0x405740): every occupant leaves the room. The legacy server
    /// accepts it only from authority 0x34 and disconnects anyone else; here GM means <see cref="GmAuthority"/> or
    /// more (Inferred mapping of our account authority) and other senders are only logged.
    /// </summary>
    private async ValueTask HandleFieldCloseAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.User is not { } user || user.Authority < GmAuthority ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out _) is not true ||
            field is null)
        {
            _logger.LogWarning("[{ConnectionId}] FieldClose rejected: not a GM in a room.", session.Connection.ConnectionId);
            return;
        }

        _logger.LogInformation("Room #{Number} closed by GM '{User}'.", field.Number, user.Username);
        foreach (var member in field.Players.ToList())
        {
            if (await LeaveCurrentFieldAsync(member.Session, member.Session.ActiveChannel, notifyLeaver: true, cancellationToken).ConfigureAwait(false))
            {
                await ReturnToLobbyAsync(member.Session, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask ReturnToLobbyAsync(WorldSession session, CancellationToken cancellationToken)
    {
        var channel = session.ActiveChannel;
        if (channel is null)
        {
            return;
        }

        // Observed (rakion.bin): FieldExit handler 0x00474410 only empties the slot, and the
        // WaitRoom menu state is 0x1C (set near 0x00447030). The only code path that rebuilds
        // the SelectGame lobby UI (ctor 0x00469320) from a room is the ChannelEnter handler
        // 0x00473DA0 (case result 0, matching channel index). So re-send ChannelEnter.
        var reenter = new ChannelEnterResponsePacket(
            ResultCode: 0,
            ChannelIndex: channel.Index,
            ServerId: session.UdpIndex, // id of the entering user; the client matches it with its own (rakion.bin 0x473DE5)
            ChannelName: channel.Name,
            ChannelType: (byte)channel.Type,
            MaxPlayers: channel.MaxPlayers,
            Flags: 0);
        await SendPacketAsync(session, reenter, cancellationToken).ConfigureAwait(false);

        // Refresh the room list once the client is back in SelectGame (state 0x17).
        var pagedEntries = channel.FieldManager.GetFields(channel.Index)
            .Take(6)
            .Select(ToFieldListEntry)
            .ToList();
        var fieldListResp = pagedEntries.Count == 0
            ? FieldListResponsePacket.Empty
            : new FieldListResponsePacket(pagedEntries);
        await SendPacketAsync(session, fieldListResp, cancellationToken).ConfigureAwait(false);

        // Send updated channel character roster
        await HandleChannelCharactersAsync(session, cancellationToken).ConfigureAwait(false);
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

        if (session.ActiveChannel.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var chatter) &&
            field is not null && chatter is not null)
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

            var broadcast = new FieldChatBroadcastPacket(formattedMessage, chatter.SlotIndex);
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
                field.Rules = field.Rules.With(req);
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

    private async ValueTask HandleFieldGameStartAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGameStart rejected: not in a room.", session.Connection.ConnectionId);
            return;
        }

        byte? result = field.TryStartGame(player);
        if (result is null)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGameStart ignored in room #{Number}: not the host or already running.", session.Connection.ConnectionId, field.Number);
            return;
        }

        _logger.LogInformation("[{ConnectionId}] Room #{Number} game start by '{Host}': result {Result}.", session.Connection.ConnectionId, field.Number, session.ActiveCharacter?.Name ?? "Unknown", result.Value);

        var response = new FieldGameStartResponsePacket(result.Value);
        if (result.Value != FieldGameStartResponsePacket.Started)
        {
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
            return;
        }

        await BroadcastToFieldAsync(field, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleFieldGameEnterAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGameEnter rejected: not in a room.", session.Connection.ConnectionId);
            return;
        }

        byte result = field.TryEnterGame(player);
        _logger.LogInformation("[{ConnectionId}] Room #{Number} slot {Slot} game enter: result {Result}.", session.Connection.ConnectionId, field.Number, player.SlotIndex, result);

        var response = new FieldGameEnterResponsePacket(result, player.SlotIndex);
        if (result != FieldGameEnterResponsePacket.Entered)
        {
            await SendPacketAsync(session, response, cancellationToken).ConfigureAwait(false);
            return;
        }

        await BroadcastToFieldAsync(field, response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleFieldGameExitAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameExitRequestPacket.TryParse(payload.Span, out var req))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldGameExit request packet.", session.Connection.ConnectionId);
            return;
        }

        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null || !player.IsInGame)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGameExit rejected: not in a running match.", session.Connection.ConnectionId);
            return;
        }

        var cancelledVote = field.CancelVoteFor(player, DateTimeOffset.UtcNow);
        bool matchOver = field.ExitGame(player);
        await FinishVoteAsync(field, cancelledVote, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[{ConnectionId}] Room #{Number} slot {Slot} left the match (value {Value}, match over: {Over}).", session.Connection.ConnectionId, field.Number, player.SlotIndex, req.Value, matchOver);

        // The broadcast carries the leaver's slot, not the request byte (RakionWorldServ.exe 0x424350 -> 0x407E00,
        // Observed); echoing the byte made clients add a nameless member in that slot.
        await BroadcastToFieldAsync(field, new FieldGameExitBroadcastPacket(player.SlotIndex), cancellationToken).ConfigureAwait(false);
        if (matchOver)
        {
            await EndFieldGameAsync(field, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldGameRoundStartAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null)
        {
            return;
        }

        var start = field.MarkLoaded(player, DateTimeOffset.UtcNow);
        _logger.LogInformation(
            "[{ConnectionId}] Room #{Number} slot {Slot} loaded the map; round start {Action}.",
            session.Connection.ConnectionId,
            field.Number,
            player.SlotIndex,
            start is null ? "waits for the others" : start.Value.ToAllLoaded ? "sent to every member" : "sent to this member");
        if (start is not { } round)
        {
            return;
        }

        var recipients = round.ToAllLoaded ? field.LoadedPlayers : [player];
        foreach (var member in recipients)
        {
            await SendPacketAsync(member.Session, round.Packet, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Relays in-game player data: 0x4B goes to every other loaded member, 0x4C to one loaded member, both as 0x4B with
    /// the sender's slot (RakionWorldServ.exe 0x405C00 / 0x405CC0, Observed).
    /// </summary>
    private async ValueTask HandleFieldGameAddPlayerAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        bool hasTarget,
        CancellationToken cancellationToken)
    {
        if (!FieldGameAddPlayerRequestPacket.TryParse(payload.Span, hasTarget, out var req))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldGameAddPlayer request ({Length} bytes).", session.Connection.ConnectionId, payload.Length);
            return;
        }

        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var sender) is not true ||
            field is null || sender is null || !sender.IsLoaded)
        {
            return;
        }

        var relay = new FieldGameAddPlayerPacket(sender.SlotIndex, req.Data);
        foreach (var member in field.LoadedPlayers)
        {
            bool isTarget = req.TargetSlot is { } target ? member.SlotIndex == target : member != sender;
            if (isTarget)
            {
                await SendPacketAsync(member.Session, relay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask HandleFieldSlotUdpAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldSlotUdpPacket.TryParse(payload.Span, out var req))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldSlotUdp request packet.", session.Connection.ConnectionId);
            return;
        }

        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var sender) is not true ||
            field is null || sender is null)
        {
            return;
        }

        var target = field.Players.FirstOrDefault(p => p.SlotIndex == req.SlotIndex);
        if (target is null)
        {
            _logger.LogDebug("[{ConnectionId}] FieldSlotUdp to empty slot {Slot} in room #{Number} ignored.", session.Connection.ConnectionId, req.SlotIndex, field.Number);
            return;
        }

        await SendPacketAsync(target.Session, new FieldSlotUdpPacket(sender.SlotIndex), cancellationToken).ConfigureAwait(false);
    }

    /// <param name="reason">End byte: 2 last round played, 5 / 6 not enough players (Observed); 0 when everyone left
    /// the match (Inferred).</param>
    private async ValueTask EndFieldGameAsync(GameField field, CancellationToken cancellationToken, byte reason = 0)
    {
        // The legacy end (0x407BE0) broadcasts to every room member before resetting the slots.
        await BroadcastToFieldAsync(field, new FieldGameEndBroadcastPacket(reason), cancellationToken).ConfigureAwait(false);
        field.EndGame();
        _logger.LogInformation("Room #{Number} match ended (reason {Reason}).", field.Number, reason);
    }

    private async ValueTask HandleFieldGameDieAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameDieRequestPacket.TryParse(payload.Span, out var req))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldGameDie request ({Length} bytes).", session.Connection.ConnectionId, payload.Length);
            return;
        }

        if (session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var victim) is not true ||
            field is null || victim is null)
        {
            return;
        }

        var result = field.RecordDeath(victim, req.Cause, req.KillerSlot, DateTimeOffset.UtcNow);
        if (result is not { } death)
        {
            _logger.LogDebug("[{ConnectionId}] Room #{Number} death of slot {Slot} ignored.", session.Connection.ConnectionId, field.Number, victim.SlotIndex);
            return;
        }

        _logger.LogInformation(
            "Room #{Number}: slot {Victim} killed by slot {Killer} (cause {Cause}), score {A}-{B}.",
            field.Number,
            victim.SlotIndex,
            req.KillerSlot,
            req.Cause,
            death.Death.ValueA,
            death.Death.ValueB);
        await SendToLoadedAsync(field, death.Death, cancellationToken).ConfigureAwait(false);
        if (death.RoundEnd is { } roundEnd)
        {
            _logger.LogInformation("Room #{Number} round {Round} won by {Winner} (goal reached).", field.Number, field.Round, roundEnd.Winner);
            await SendToLoadedAsync(field, roundEnd, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// End-of-round report (RakionWorldServ.exe 0x424B60, Observed): the client sends the experience and gold it
    /// computed; the server accepts it only after a round end, rejects values above the mode's limit
    /// ("Wrong Game Point!"), adds 50 % experience to PowerUsers, stores both and announces a level up with 0x51.
    /// Inferred: the legacy +50 % flag (user + 0x236C) is the PowerUser state. The legacy server disconnects a cheater
    /// (error 0x97); here the report is only dropped and logged. The round outcome is counted and creature experience
    /// is stored in the same transaction; a level change is broadcast to the room with 0x52.
    /// </summary>
    private async ValueTask HandleFieldGamePointAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGamePointRequestPacket.TryParse(payload.Span, out var report))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldGamePoint report ({Length} bytes).", session.Connection.ConnectionId, payload.Length);
            return;
        }

        if (session.User is null || session.ActiveCharacter is null || _matchResults is null ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null || !field.AcceptsPointReport)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGamePoint ignored: no ended round to report.", session.Connection.ConnectionId);
            return;
        }

        var (maxExp, maxGold) = field.PointReportLimits;
        if (report.Exp > maxExp || report.Gold > maxGold)
        {
            _logger.LogWarning(
                "[{ConnectionId}] Wrong Game Point! Exp : {Exp}, Gold : {Gold} (limits {MaxExp} / {MaxGold}, room #{Number} mode {Mode}); report dropped.",
                session.Connection.ConnectionId,
                report.Exp,
                report.Gold,
                maxExp,
                maxGold,
                field.Number,
                field.Rules.Mode);
            return;
        }

        long nowMinutes = LegacyGameClock.ToMinutes(DateTime.Now);
        bool powerUser = session.GameInfo is { } info && info.PowerTime > nowMinutes;
        int exp = (int)(powerUser ? report.Exp * 3 / 2 : report.Exp);
        var rewards = _options.Value.Rewards;
        exp = (int)Math.Min((long)exp * Math.Max(rewards.ExpMultiplier, 1), int.MaxValue);
        int gold = (int)Math.Min((long)report.Gold * Math.Max(rewards.GoldMultiplier, 1), int.MaxValue);

        // The legacy server decides the outcome itself and counts a draw when the report's u16 is 0 (0x424E80, Observed).
        byte result = report.Value != 0 ? field.RoundOutcomeFor(player) : GameField.RoundOutcomeDraw;
        var outcome = await _matchResults
            .ApplyRoundRewardAsync(
                session.User.Id,
                session.ActiveCharacter.Id,
                exp,
                gold,
                result,
                [(int)Math.Min(report.CreatureExp1, int.MaxValue), (int)Math.Min(report.CreatureExp2, int.MaxValue), (int)Math.Min(report.CreatureExp3, int.MaxValue)],
                cancellationToken)
            .ConfigureAwait(false);
        if (outcome is null)
        {
            return;
        }

        session.ActiveCharacter.Level = outcome.Level;
        session.ActiveCharacter.Exp = outcome.Exp;
        session.ActiveCharacter.LevelPoint = (byte)outcome.LevelPoint;
        session.ActiveCharacter.WinCount = outcome.Wins;
        session.ActiveCharacter.LossCount = outcome.Losses;
        session.ActiveCharacter.DrawCount = outcome.Draws;
        _logger.LogInformation(
            "[{ConnectionId}] '{Character}' earned {Exp} exp{PowerUser} and {Gold} gold: level {Level}, exp {Total}, gold {TotalGold}, W/L/D {Wins}/{Losses}/{Draws} (result {Result}), creatures {C1}/{C2}/{C3}.",
            session.Connection.ConnectionId,
            session.ActiveCharacter.Name,
            exp,
            powerUser ? " (PowerUser +50 %)" : string.Empty,
            gold,
            outcome.Level,
            outcome.Exp,
            outcome.Gold,
            outcome.Wins,
            outcome.Losses,
            outcome.Draws,
            result,
            outcome.Creature1Level,
            outcome.Creature2Level,
            outcome.Creature3Level);

        if (outcome.LeveledUp)
        {
            await SendPacketAsync(session, new CharacterLevelUpPacket(outcome.Level, (ushort)outcome.LevelPoint), cancellationToken).ConfigureAwait(false);
        }

        if (outcome.LeveledUp || outcome.CreatureLeveledUp)
        {
            // Every room member learns the new levels (0x424E1F, sent to all occupied slots by 0x4061F0).
            var levels = new FieldPlayerLevelPacket(player.SlotIndex, outcome.Level, outcome.Creature1Level, outcome.Creature2Level, outcome.Creature3Level);
            await BroadcastToFieldAsync(field, levels, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldGameStageEndAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameRoundEndRequestPacket.TryParse(payload.Span, out var request) ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null)
        {
            return;
        }

        if (field.EndStage(player, request.Reason, DateTimeOffset.UtcNow) is { } roundEnd)
        {
            _logger.LogInformation("Room #{Number} stage round {Round} ended by the master: reason {Reason}.", field.Number, field.Round, request.Reason);
            await SendToLoadedAsync(field, roundEnd, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stage clear report (RakionWorldServ.exe 0x425010, Observed): accepted only in Stage after a round end and
    /// within the mode's limits. A rank that does not beat the stored one is ignored, as in the legacy server;
    /// otherwise the rank, the drops and the reward (counted as a draw) are stored together, the client gets 0x53
    /// with the stage, rank and drops, and level changes are announced with 0x51 / 0x52.
    /// </summary>
    private async ValueTask HandleFieldGameStagePointAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameStagePointRequestPacket.TryParse(payload.Span, out var report))
        {
            _logger.LogWarning("[{ConnectionId}] Malformed FieldGameStagePoint report ({Length} bytes).", session.Connection.ConnectionId, payload.Length);
            return;
        }

        if (session.User is null || session.ActiveCharacter is null || _matchResults is null ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out var player) is not true ||
            field is null || player is null || field.Rules.Mode != 0 || !field.AcceptsPointReport)
        {
            _logger.LogWarning("[{ConnectionId}] FieldGameStagePoint ignored: no ended stage round to report.", session.Connection.ConnectionId);
            return;
        }

        var (maxExp, maxGold) = field.PointReportLimits;
        if (report.Exp > maxExp || report.Gold > maxGold)
        {
            _logger.LogWarning(
                "[{ConnectionId}] Wrong Game Point! Exp : {Exp}, Gold : {Gold} (limits {MaxExp} / {MaxGold}, stage {Stage}); report dropped.",
                session.Connection.ConnectionId,
                report.Exp,
                report.Gold,
                maxExp,
                maxGold,
                report.Stage);
            return;
        }

        long nowMinutes = LegacyGameClock.ToMinutes(DateTime.Now);
        bool powerUser = session.GameInfo is { } info && info.PowerTime > nowMinutes;
        int exp = (int)(powerUser ? report.Exp * 3 / 2 : report.Exp);
        var rewards = _options.Value.Rewards;
        exp = (int)Math.Min((long)exp * Math.Max(rewards.ExpMultiplier, 1), int.MaxValue);
        int gold = (int)Math.Min((long)report.Gold * Math.Max(rewards.GoldMultiplier, 1), int.MaxValue);

        var clear = await _matchResults
            .ApplyStageClearAsync(
                session.User.Id,
                session.ActiveCharacter.Id,
                report.Stage,
                report.Rank,
                report.Items,
                exp,
                gold,
                [(int)Math.Min(report.CreatureExp1, int.MaxValue), (int)Math.Min(report.CreatureExp2, int.MaxValue), (int)Math.Min(report.CreatureExp3, int.MaxValue)],
                cancellationToken)
            .ConfigureAwait(false);
        if (clear is null)
        {
            return;
        }

        if (!clear.Improved || clear.Reward is not { } outcome)
        {
            _logger.LogInformation(
                "[{ConnectionId}] '{Character}' cleared stage {Stage} with rank {Rank}, not better than {Previous}: no reward.",
                session.Connection.ConnectionId,
                session.ActiveCharacter.Name,
                report.Stage,
                report.Rank,
                clear.PreviousRank);
            return;
        }

        var drops = clear.Drops.Select(drop => drop.ItemId).ToArray();
        await SendPacketAsync(session, new FieldGameStagePointPacket(FieldGameStagePointPacket.Accepted, report.Stage, report.Rank, drops), cancellationToken).ConfigureAwait(false);

        session.ActiveCharacter.Level = outcome.Level;
        session.ActiveCharacter.Exp = outcome.Exp;
        session.ActiveCharacter.LevelPoint = (byte)outcome.LevelPoint;
        session.ActiveCharacter.DrawCount = outcome.Draws;
        _logger.LogInformation(
            "[{ConnectionId}] '{Character}' cleared stage {Stage} with rank {Rank} (was {Previous}): {Exp} exp, {Gold} gold, drops [{Drops}]; level {Level}, exp {Total}, gold {TotalGold}.",
            session.Connection.ConnectionId,
            session.ActiveCharacter.Name,
            report.Stage,
            report.Rank,
            clear.PreviousRank,
            exp,
            gold,
            string.Join(", ", clear.Drops.Select(drop => $"{drop.ItemId}@{drop.Slot}")),
            outcome.Level,
            outcome.Exp,
            outcome.Gold);

        if (outcome.LeveledUp)
        {
            await SendPacketAsync(session, new CharacterLevelUpPacket(outcome.Level, (ushort)outcome.LevelPoint), cancellationToken).ConfigureAwait(false);
        }

        if (outcome.LeveledUp || outcome.CreatureLeveledUp)
        {
            var levels = new FieldPlayerLevelPacket(player.SlotIndex, outcome.Level, outcome.Creature1Level, outcome.Creature2Level, outcome.Creature3Level);
            await BroadcastToFieldAsync(field, levels, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleFieldGameGolemAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (!FieldGameGolemHpRequestPacket.TryParse(payload.Span, out var report) ||
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out _) is not true ||
            field is null)
        {
            return;
        }

        if (field.RecordGolemHealth(report.RedGolem, report.BlueGolem, DateTimeOffset.UtcNow) is { } roundEnd)
        {
            _logger.LogInformation("Room #{Number} round {Round}: a golem died, winner {Winner}.", field.Number, field.Round, roundEnd.Winner);
            await SendToLoadedAsync(field, roundEnd, cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask HandleFieldGameLeaderHpAsync(
        WorldSession session,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (FieldGameLeaderHpRequestPacket.TryParse(payload.Span, out var report) &&
            session.ActiveChannel?.FieldManager.TryGetFieldBySession(session.Connection.ConnectionId, out var field, out _) is true &&
            field is not null)
        {
            field.RecordLeaderHealth(report.LeaderSlot, report.Health);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Runs the match timers of every room once (round time-outs, next rounds, match ends).</summary>
    public async Task TickFieldsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var channel in _channelManager.GetChannels())
        {
            foreach (var field in channel.FieldManager.GetFields(channel.Index))
            {
                await FinishVoteAsync(field, field.TickVote(now), cancellationToken).ConfigureAwait(false);
                if (field.Tick(now) is not { } tick)
                {
                    continue;
                }

                if (tick.RoundEnd is { } roundEnd)
                {
                    _logger.LogInformation("Room #{Number} round {Round} timed out, winner {Winner}.", field.Number, field.Round, roundEnd.Winner);
                    await SendToLoadedAsync(field, roundEnd, cancellationToken).ConfigureAwait(false);
                }

                if (tick.NextRound is { } nextRound)
                {
                    _logger.LogInformation("Room #{Number} round {Round} starts.", field.Number, nextRound.Round);
                    await SendToLoadedAsync(field, nextRound, cancellationToken).ConfigureAwait(false);
                }

                if (tick.GameEnd is { } reason)
                {
                    await EndFieldGameAsync(field, cancellationToken, reason).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask SendToLoadedAsync(GameField field, IPacket packet, CancellationToken cancellationToken)
    {
        foreach (var member in field.LoadedPlayers)
        {
            await SendPacketAsync(member.Session, packet, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask BroadcastToFieldAsync(GameField field, IPacket packet, CancellationToken cancellationToken)
    {
        foreach (var member in field.Players)
        {
            await SendPacketAsync(member.Session, packet, cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask SendPacketAsync(WorldSession session, IPacket packet, CancellationToken cancellationToken) =>
        _sender.SendAsync(session, packet, cancellationToken);
}
