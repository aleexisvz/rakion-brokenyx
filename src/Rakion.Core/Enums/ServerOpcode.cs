namespace Rakion.Core.Enums;

/// <summary>
/// Opcodes sent by the server to the client (server → client direction).
/// Client requests live in <see cref="ClientOpcode"/>. Many World responses reuse the
/// request value (the client dispatches by the same jump-table index); that is recorded here
/// per message instead of through aliases.
/// </summary>
/// <remarks>
/// Not listed because they collide with retail v258 channel opcodes: the legacy
/// RakionWorldServ login result (0x001E, Observed at 0x4286fd) and character list
/// (0x001F, Observed at 0x428832). The v258 client uses 0x000C / 0x000D instead.
/// Likewise the legacy character delete (0x002D) and select (0x002E) confirmations are not listed:
/// in v258 those values are the inventory leave / buy replies, and the client confirms delete and
/// select on 0x0013 / 0x0014.
/// </remarks>
public enum ServerOpcode : ushort
{
    None = 0x0000,

    // --- Broker (TCP 40706) ---
    /// <summary>
    /// Response containing the list of available game servers.
    /// Evidence: Observed in BrokenServer.Systems.ServerListPacket.
    /// </summary>
    ServerListResponse = 0x0101,

    // --- World Server (TCP 40708) ---
    /// <summary>
    /// World login result and account summary (same value as the request).
    /// Evidence: Observed in RakionWorldServ DBCommandCharacterList and rakion.bin OnRecvLogin.
    /// </summary>
    WorldLogin = 0x000C,

    /// <summary>
    /// World login part 2 acknowledgement carrying the character roster.
    /// Evidence: Observed in engine.dll jump table opcode 13 (0x000D) calling 0x36196690.
    /// </summary>
    WorldLoginPart2 = 0x000D,

    /// <summary>
    /// UDP probe success confirmation (Result, IP1, Port1, IP2, Port2).
    /// Evidence: Observed in engine.dll OnRecvSuccessUDP (0x36192DA0) and RakionWorldServ 0x4208b3.
    /// </summary>
    UdpSuccess = 0x000E,

    /// <summary>
    /// Character creation result.
    /// Evidence: Observed in engine.dll 0x36192e30 and RakionWorldServ.exe 0x00414e3d.
    /// </summary>
    CharacterCreate = 0x0012,

    /// <summary>
    /// Character deletion confirmation (retail v258 engine.dll opcode 19).
    /// Evidence: Observed in engine.dll jump table opcode 19 (0x0013) calling 0x36192e70.
    /// </summary>
    CharacterDelete = 0x0013,

    /// <summary>
    /// Character selection confirmation (retail v258 engine.dll opcode 20).
    /// Evidence: Observed in engine.dll jump table opcode 20 (0x0014) calling 0x36192f90.
    /// </summary>
    CharacterSelect = 0x0014,

    /// <summary>
    /// Buddy name change confirmation.
    /// Evidence: Observed in engine.dll OnRecvChangeBuddyName jump table opcode 21 calling 0x36192FB0.
    /// </summary>
    CharacterChangeBuddyName = 0x0015,

    /// <summary>
    /// Whisper delivery / result.
    /// Evidence: Observed in engine.dll OnRecvCharacterWhisper (0x36193000).
    /// </summary>
    CharacterWhisper = 0x0016,

    /// <summary>
    /// Account of a character: [u8 status][string account][string Messenger name].
    /// Evidence: Observed in engine.dll jump table opcode 0x19 (0x3619745F -> 0x36193170).
    /// </summary>
    CharacterGetUserName = 0x0019,

    /// <summary>
    /// Server confirmation response to GameGuard verification.
    /// Evidence: Observed in RakionWorldServ opcode 16 stub response (0x001B). The client dispatches 0x1B to the
    /// stat reset answer (engine.dll 0x36195400), so CharacterStateClearResponsePacket uses this value too.
    /// </summary>
    GameGuard = 0x001B,

    // --- Inventory / Shop / Blacksmith ---
    /// <summary>
    /// Inventory enter result: [u8 Result][u32 ServerTimeMinutes].
    /// Evidence: Observed in RakionWorldServ.exe 0x427A9D (7-byte send) and engine.dll 0x36193650 (reads u8 + u32).
    /// </summary>
    InventoryEnter = 0x002C,

    /// <summary>
    /// Inventory leave result: [u8 Result].
    /// Evidence: Observed in engine.dll 0x36193680 (reads one byte for opcode 0x2D and forwards it to vtable+0x1D4).
    /// </summary>
    InventoryLeave = 0x002D,

    /// <summary>
    /// Inventory move result: [u8 Result] + source and destination slot states (19 bytes).
    /// Evidence: Observed in engine.dll 0x36193810 (reader) and RakionWorldServ.exe 0x4219F7 (21-byte send).
    /// </summary>
    InventoryMove = 0x0031,

    /// <summary>
    /// Shop purchase result: result, gold, cash, bought items, trailing u32 list.
    /// Evidence: Observed in engine.dll 0x36195640 (reader) and rakion.bin 0x4774E0 (callback).
    /// </summary>
    InventoryBuy = 0x002E,

    /// <summary>
    /// Sell result: [u8 Result][u32 Gold][u32 Cash][u8 BoxSlot].
    /// Evidence: Observed in engine.dll 0x361936A0 (reader) and rakion.bin 0x478A70 (callback).
    /// </summary>
    InventorySell = 0x002F,

    /// <summary>
    /// Stat point allocation result: [u8 Result][u16 LevelPoints][u16 PowerPoints][u8 Stat][u16 NewValue].
    /// Evidence: Observed in engine.dll 0x361938A0 (reader) and rakion.bin 0x47DBB0 (callback).
    /// </summary>
    InventoryAllocationPoint = 0x0033,

    /// <summary>
    /// PowerUser purchase result: result, gold, cash, PowerUser expiry, PowerUser points, trailing u32 list.
    /// Evidence: Observed in engine.dll 0x361958C0 (reader) and rakion.bin 0x474F50 (callback).
    /// </summary>
    InventoryBuyPowerUser = 0x0034,

    /// <summary>
    /// Blacksmith reinforce result: [u8 Result] and, when Result is below 6, [u8 TargetSlot][u8 StoneSlot][u8 N][N x u8].
    /// Evidence: Observed in engine.dll 0x36194600 (reader) and rakion.bin 0x478D80 (callback).
    /// </summary>
    EnchantReinforce = 0x0074,

    // --- Channel / Lobby ---
    /// <summary>
    /// Channel list response.
    /// Evidence: Observed in engine.dll OnRecvChannelList (0x361931E0, opcode 0x001D).
    /// </summary>
    ChannelList = 0x001D,

    /// <summary>
    /// Channel characters response.
    /// Evidence: Observed in engine.dll OnRecvChannelCharacters (0x361932A0, opcode 0x001E).
    /// </summary>
    ChannelCharacters = 0x001E,

    /// <summary>
    /// Channel enter response.
    /// Evidence: Observed in engine.dll OnRecvChannelEnter (0x361933F0, opcode 0x001F).
    /// </summary>
    ChannelEnter = 0x001F,

    /// <summary>
    /// Channel exit response.
    /// Evidence: Observed in engine.dll OnRecvChannelExit (0x36193490, opcode 0x0020).
    /// </summary>
    ChannelExit = 0x0020,

    /// <summary>
    /// Channel chat broadcast.
    /// Evidence: Observed in engine.dll OnRecvChannelChat (0x361934E0, opcode 0x0022).
    /// </summary>
    ChannelChat = 0x0022,

    // --- Fields / Rooms ---
    /// <summary>
    /// Active field / room list.
    /// Evidence: Observed in engine.dll jump table 0x3619786C opcode 54 and RakionWorldServ.exe 0x422f14.
    /// </summary>
    FieldList = 0x0036,

    /// <summary>
    /// Room details for a player who just entered: rule header, names and the 20 slots.
    /// Evidence: Observed in engine.dll jump table opcode 0x37 (0x361975e7 -> 0x36196ce0) and RakionWorldServ 0x4073B7.
    /// </summary>
    FieldInfo = 0x0037,

    /// <summary>
    /// Network check relayed between room members: [u8 SenderSlot].
    /// Evidence: Observed in engine.dll jump table opcode 0x62 (0x36197817 -> 0x36194510) and RakionWorldServ 0x406930.
    /// </summary>
    FieldSlotUdp = 0x0062,

    /// <summary>
    /// Quick enter failed (no room). No payload.
    /// Evidence: Observed in engine.dll jump table opcode 0x39 (0x36197603 -> 0x36193a60) and RakionWorldServ 0x423436.
    /// </summary>
    FieldQuickEnter = 0x0039,

    /// <summary>
    /// Response to field / room creation request: [u8 ResultCode, u16 RoomNumber].
    /// Evidence: Observed in engine.dll jump table opcode 0x3B (0x3619761f -> 0x36193a90) and RakionWorldServ 0x423a82.
    /// </summary>
    FieldCreate = 0x003B,

    /// <summary>
    /// Response to enter room request (details of room and players).
    /// Evidence: Observed in engine.dll jump table opcode 0x38 (0x361975f5 -> 0x361970e0) and RakionWorldServ 0x423870.
    /// </summary>
    FieldEnter = 0x0038,

    /// <summary>
    /// Response to exit room request.
    /// Evidence: Observed in engine.dll jump table opcode 0x3A (0x36197611 -> 0x36193a70) and RakionWorldServ.
    /// </summary>
    FieldExit = 0x003A,

    /// <summary>
    /// Player ready state broadcast in field / room.
    /// Evidence: Observed in engine.dll jump table opcode 0x3D (0x3619763b -> 0x36193ae0) and RakionWorldServ 0x407568.
    /// </summary>
    FieldReady = 0x003D,

    /// <summary>
    /// Player team change broadcast in field / room: [u8 Result, u8 OldSlot, u8 NewSlot].
    /// Evidence: Observed in engine.dll jump table opcode 0x3E (0x36197649 -> 0x36193b0b) and RakionWorldServ 0x40779D.
    /// </summary>
    FieldChangeTeam = 0x003E,

    /// <summary>
    /// Room rule change broadcast in field / room.
    /// Evidence: Observed in engine.dll jump table opcode 0x41 (0x36197657) and RakionWorldServ 0x4077C0.
    /// </summary>
    FieldChangeRule = 0x0041,

    /// <summary>
    /// Slot status change broadcast in field / room: [u8 SlotIndex, u8 Status].
    /// Evidence: Observed in engine.dll jump table opcode 0x42 (0x36197665 -> 0x36193c50) and RakionWorldServ 0x407980.
    /// </summary>
    FieldChangeSlotStatus = 0x0042,

    /// <summary>
    /// Field chat message broadcast.
    /// Evidence: Observed in engine.dll jump table opcode 0x47 (0x361976ab -> 0x36193b70) and RakionWorldServ.
    /// </summary>
    FieldChat = 0x0047,

    /// <summary>
    /// Match start result: [u8 Result].
    /// Evidence: Observed in engine.dll jump table opcode 0x43 (0x36197673 -> 0x36193c80) and RakionWorldServ 0x4079D0.
    /// </summary>
    FieldGameStart = 0x0043,

    /// <summary>
    /// Match end broadcast: [u8 Value].
    /// Evidence: Observed in engine.dll jump table opcode 0x44 (0x36197681 -> 0x36193ca0) and RakionWorldServ 0x407BE0.
    /// </summary>
    FieldGameEnd = 0x0044,

    /// <summary>
    /// Match enter result: [u8 Result][u8 SlotIndex].
    /// Evidence: Observed in engine.dll jump table opcode 0x45 (0x3619768f -> 0x36193cc0) and RakionWorldServ 0x407C70.
    /// </summary>
    FieldGameEnter = 0x0045,

    /// <summary>
    /// Match exit broadcast: [u8 Value].
    /// Evidence: Observed in engine.dll jump table opcode 0x46 (0x3619769d -> 0x36193cf0) and RakionWorldServ 0x407E00.
    /// </summary>
    FieldGameExit = 0x0046,

    /// <summary>
    /// Round start: [u8 Round][u16 SecondsLeft][u8][u8][u8 RedLeader][u8 BlueLeader].
    /// Evidence: Observed in engine.dll jump table opcode 0x48 (0x361976b9 -> 0x36193de0) and RakionWorldServ 0x408440.
    /// </summary>
    FieldGameRoundStart = 0x0048,

    /// <summary>
    /// In-game player data relayed from a room member: [u8 SenderSlot][u16 Length][data].
    /// Evidence: Observed in RakionWorldServ 0x405C00 / 0x405CC0 (relays of 0x4B and 0x4C) and engine.dll 0x36193d70.
    /// </summary>
    FieldGameAddPlayer = 0x004B,

    /// <summary>
    /// Next round: [u8 Round][u8 RedLeaderSlot][u8 BlueLeaderSlot].
    /// Evidence: Observed in engine.dll jump table opcode 0x49 (0x361976c7 -> 0x36193e40) and RakionWorldServ 0x409CA7.
    /// </summary>
    FieldGameNextRound = 0x0049,

    /// <summary>
    /// Round end: [u8 EndedByGoal][u8 Winner][u8 RedRoundWins][u8 BlueRoundWins].
    /// Evidence: Observed in engine.dll jump table opcode 0x4A (0x361976d5 -> 0x36193e80) and RakionWorldServ 0x408E3E / 0x409FA7.
    /// </summary>
    FieldGameRoundEnd = 0x004A,

    /// <summary>
    /// Death broadcast: [u8 Victim][u8 Cause][u8 Killer][u8 ValueA][u8 ValueB].
    /// Evidence: Observed in RakionWorldServ 0x4087D0 (7-byte send to every loaded member).
    /// </summary>
    FieldGameDie = 0x004F,

    /// <summary>
    /// Level up: [u8 Level][u16 LevelPoints].
    /// Evidence: Observed in engine.dll jump table opcode 0x51 (0x36197761 -> 0x36194100) and RakionWorldServ 0x424DAC.
    /// </summary>
    CharacterLevelUp = 0x0051,

    /// <summary>
    /// A room member's new levels: [u8 Slot][u8 Level][u8 x3 CreatureLevels].
    /// Evidence: Observed in engine.dll jump table opcode 0x52 (0x3619776F -> 0x36194130, five bytes) and
    /// RakionWorldServ 0x424E62 (room broadcast after a level change).
    /// </summary>
    FieldPlayerLevel = 0x0052,

    /// <summary>
    /// Answer to a stage clear report: [u8 result] and, for 0, [u8 stage][u8 rank][u8 n][n x u16 item].
    /// Evidence: Observed in RakionWorldServ.exe 0x42515F / 0x4251F4 (3-byte refusal, 6 + 2n bytes otherwise).
    /// </summary>
    FieldGameStagePoint = 0x0053,

    /// <summary>
    /// Bag purchase answer (see AccountUpgradeResponsePacket).
    /// Evidence: Observed in engine.dll jump table opcode 0x32 (0x36197577 -> 0x361957C0).
    /// </summary>
    InventoryBuyBag = 0x0032,

    /// <summary>
    /// Kick vote opened: [u8 target slot][string reason].
    /// Evidence: Observed in engine.dll jump table opcode 0x5D (0x361977ED -> 0x36194360).
    /// </summary>
    FieldGameVoteOpen = 0x005D,

    /// <summary>
    /// Answer to a targeted GM notice: [u8 result] 0 delivered, 1 no such character online.
    /// Evidence: Observed in RakionWorldServ.exe 0x41F449 and engine.dll jump table opcode 0x05.
    /// </summary>
    AdminNotice = 0x0005,

    /// <summary>
    /// System notice shown to a player: [string text].
    /// Evidence: Observed in RakionWorldServ.exe 0x41F30E (GM notice delivery) and engine.dll jump table opcode
    /// 0x63 (0x36197809 -> 0x36194460).
    /// </summary>
    SystemNotice = 0x0063,

    /// <summary>
    /// Kick vote refusal or result (see FieldGameVoteResultPacket).
    /// Evidence: Observed in engine.dll jump table opcode 0x5F (0x361977FB -> 0x361943D0).
    /// </summary>
    FieldGameVoteResult = 0x005F,

    /// <summary>
    /// Potion slot purchase answer (see AccountUpgradeResponsePacket).
    /// Evidence: Observed in engine.dll jump table opcode 0x6F (0x361975AF -> 0x36195AC0).
    /// </summary>
    InventoryBuyPotionSlot = 0x006F,

    /// <summary>
    /// Stage rank clear purchase answer (see StagePurchaseResponsePacket).
    /// Evidence: Observed in engine.dll jump table opcode 0x70 (0x361975BD -> 0x36195BD0).
    /// </summary>
    InventoryBuyStageRankClear = 0x0070,

    /// <summary>
    /// Stage level pass purchase answer (see StagePurchaseResponsePacket).
    /// Evidence: Observed in engine.dll jump table opcode 0x71 (0x361975CB -> 0x36195CC0).
    /// </summary>
    InventoryBuyStageLevelFree = 0x0071,

    // --- UDP (40708 / 40709) ---
    /// <summary>
    /// UDP hole punch echo on Port 1 (40708).
    /// Evidence: Observed in RakionWorldServ.exe 0x00429554.
    /// </summary>
    UdpHolePunch1 = 0x0201,

    /// <summary>
    /// UDP hole punch echo on Port 2 (40709).
    /// Evidence: Observed in RakionWorldServ.exe 0x00429557.
    /// </summary>
    UdpHolePunch2 = 0x0202,
}
