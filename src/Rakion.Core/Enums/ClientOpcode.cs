namespace Rakion.Core.Enums;

/// <summary>
/// Opcodes sent by the client to the server (client → server direction).
/// Server responses live in <see cref="ServerOpcode"/>; the same numeric value can mean a
/// different message in each direction, so the two sets are never mixed.
/// </summary>
public enum ClientOpcode : ushort
{
    None = 0x0000,

    // --- Broker (TCP 40706) ---
    /// <summary>
    /// Sent by the client to request the server list.
    /// Evidence: Observed in BrokenServer.Systems.oPCode and client handshake.
    /// </summary>
    ServerListRequest = 0xCDDF,

    /// <summary>
    /// Server list request framed with the response opcode value.
    /// Evidence: Inferred — accepted by the Broker as a server list request since phase 2.
    /// </summary>
    ServerListRequestAlt = 0x0101,

    /// <summary>
    /// Client login authentication (standard v258).
    /// Evidence: Observed in BrokenServer SV_AUTH_LOGIN.
    /// </summary>
    Login = 0x1310,

    /// <summary>
    /// Alternative login opcode variant 2 (SV_AUTH_LOGIN_2).
    /// Evidence: Observed in BrokenServer.Systems.oPCode.
    /// </summary>
    LoginAlt2 = 0x3000,

    /// <summary>
    /// Alternative login opcode variant 3 (SV_AUTH_LOGIN_3).
    /// Evidence: Observed in BrokenServer.Systems.oPCode.
    /// </summary>
    LoginAlt3 = 0x5000,

    /// <summary>
    /// Alternative login opcode variant 4 (SV_AUTH_LOGIN_4).
    /// Evidence: Observed in BrokenServer.Systems.oPCode.
    /// </summary>
    LoginAlt4 = 0x4000,

    // --- World Server (TCP 40708) ---
    /// <summary>
    /// Legacy/unencrypted WorldLogin opcode variant.
    /// Evidence: Observed in RakionWorldServ switch jump table (0x004299B8).
    /// </summary>
    WorldLoginLegacy = 0x0002,

    /// <summary>
    /// Legacy world login part 2 packet opcode.
    /// Evidence: Observed in RakionWorldServ 0x411ff0.
    /// </summary>
    WorldLoginPart2Legacy = 0x0003,

    /// <summary>
    /// World server character creation request (retail WorldServer opcode).
    /// Evidence: Observed in RakionWorldServ opcode 0x0007 handler (DBCommandCharacterCreate).
    /// </summary>
    CharacterCreateRetail = 0x0007,

    /// <summary>
    /// World server character deletion request (retail WorldServer opcode).
    /// Evidence: Observed in RakionWorldServ opcode 0x0008 handler (DBCommandCharacterDelete).
    /// </summary>
    CharacterDeleteRetail = 0x0008,

    /// <summary>
    /// World server character selection request (retail WorldServer opcode).
    /// Evidence: Observed in RakionWorldServ opcode 0x0009 handler (DBCommandCharacterSelect).
    /// </summary>
    CharacterSelectRetail = 0x0009,

    /// <summary>
    /// World server session authentication with the Broker session ticket.
    /// Evidence: Observed in engine.dll ?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z (0x000C).
    /// </summary>
    WorldLogin = 0x000C,

    /// <summary>
    /// World login part 2 completing the client handshake.
    /// Evidence: Observed in engine.dll jump table opcode 13 (0x000D).
    /// </summary>
    WorldLoginPart2 = 0x000D,

    /// <summary>
    /// UDP hole punching / NAT probe success notification.
    /// Evidence: Observed in engine.dll ?SendSuccessUDP@IScavengerWorldNet@@UAEXE@Z (0x000E).
    /// </summary>
    UdpSuccess = 0x000E,

    /// <summary>
    /// Client heartbeat / ping packet.
    /// Evidence: Observed in engine.dll ?SendAlive@IScavengerWorldNet@@UAEXXZ (0x000F).
    /// </summary>
    KeepAlive = 0x000F,

    /// <summary>
    /// GameGuard authentication check from client.
    /// Evidence: Observed in engine.dll ?SendGameGuard@IScavengerWorldNet@@UAEXPAE@Z (0x0010).
    /// </summary>
    GameGuard = 0x0010,

    /// <summary>
    /// Character creation request.
    /// Evidence: Observed in engine.dll ?SendCharacterCreate@IScavengerWorldNet@@UAEXPADEE@Z (0x0012).
    /// </summary>
    CharacterCreate = 0x0012,

    /// <summary>
    /// Character deletion request.
    /// Evidence: Observed in engine.dll ?SendCharacterDelete@IScavengerWorldNet@@UAEXKPAD@Z (0x0013).
    /// </summary>
    CharacterDelete = 0x0013,

    /// <summary>
    /// Character selection request to enter lobby.
    /// Evidence: Observed in engine.dll ?SendCharacterSelect@IScavengerWorldNet@@UAEXK@Z (0x0014).
    /// </summary>
    CharacterSelect = 0x0014,

    /// <summary>
    /// Character name change notification for the messenger / buddy system.
    /// Evidence: Observed in engine.dll ?SendCharacterChangeBuddyName@IScavengerWorldNet@@UAEXPAD@Z (0x0015).
    /// </summary>
    CharacterChangeBuddyName = 0x0015,

    /// <summary>
    /// Whisper / private chat between characters.
    /// Evidence: Observed in engine.dll ?SendCharacterWhisper@IScavengerWorldNet@@UAEXPAD0@Z (0x0016).
    /// </summary>
    CharacterWhisper = 0x0016,

    /// <summary>
    /// Tutorial completion notification.
    /// Evidence: Observed in engine.dll ?SendCharacterTutorialClear@IScavengerWorldNet@@UAEXXZ (0x001A).
    /// </summary>
    CharacterTutorialClear = 0x001A,

    /// <summary>
    /// Stat point allocation reset.
    /// Evidence: Observed in engine.dll ?SendCharacterStateClear@IScavengerWorldNet@@UAEXEG@Z (0x001B).
    /// </summary>
    CharacterStateClear = 0x001B,

    /// <summary>
    /// Character rename with scroll.
    /// Evidence: Observed in engine.dll ?SendCharacterChangeCharName@IScavengerWorldNet@@UAEXPADEG@Z (0x001C).
    /// </summary>
    CharacterChangeCharName = 0x001C,

    // --- Channel / Lobby ---
    /// <summary>
    /// Channel list request.
    /// Evidence: Observed in engine.dll ?SendChannelList@IScavengerWorldNet@@UAEXEE@Z (0x001D).
    /// </summary>
    ChannelList = 0x001D,

    /// <summary>
    /// Channel characters request.
    /// Evidence: Observed in engine.dll ?SendChannelCharacters@IScavengerWorldNet@@UAEXXZ (0x001E).
    /// </summary>
    ChannelCharacters = 0x001E,

    /// <summary>
    /// Channel enter request.
    /// Evidence: Observed in engine.dll ?SendChannelEnter@IScavengerWorldNet@@UAEXEPAD@Z (0x001F).
    /// </summary>
    ChannelEnter = 0x001F,

    /// <summary>
    /// Channel exit request.
    /// Evidence: Observed in engine.dll ?SendChannelExit@IScavengerWorldNet@@UAEXXZ (0x0020).
    /// </summary>
    ChannelExit = 0x0020,

    /// <summary>
    /// Channel create request.
    /// Evidence: Observed in engine.dll ?SendChannelCreate@IScavengerWorldNet@@UAEXPAD0E@Z (0x0021).
    /// </summary>
    ChannelCreate = 0x0021,

    /// <summary>
    /// Channel lobby chat message.
    /// Evidence: Observed in engine.dll ?SendChannelChat@IScavengerWorldNet@@UAEXPAD@Z (0x0022).
    /// </summary>
    ChannelChat = 0x0022,

    // --- Inventory / Shop / Blacksmith (opcode read from the Send* export's immediate) ---
    /// <summary>
    /// Opens the inventory screen. No payload.
    /// Evidence: Observed in engine.dll ?SendInventoryEnter@IScavengerWorldNet@@UAEXXZ (0x002C).
    /// </summary>
    InventoryEnter = 0x002C,

    /// <summary>
    /// Closes the inventory screen. No payload.
    /// Evidence: Observed in engine.dll ?SendInventoryLeave@IScavengerWorldNet@@UAEXXZ (0x002D).
    /// </summary>
    InventoryLeave = 0x002D,

    /// <summary>
    /// Buys an item: (u16, u8, u8, u16).
    /// Evidence: Observed in engine.dll ?SendInventoryBuy@IScavengerWorldNet@@UAEXGEEG@Z (0x002E).
    /// </summary>
    InventoryBuy = 0x002E,

    /// <summary>
    /// Sells an item: (u8).
    /// Evidence: Observed in engine.dll ?SendInventorySell@IScavengerWorldNet@@UAEXE@Z (0x002F).
    /// </summary>
    InventorySell = 0x002F,

    /// <summary>
    /// Moves an item: (u8, u8, u8, u8).
    /// Evidence: Observed in engine.dll ?SendInventoryMove@IScavengerWorldNet@@UAEXEEEE@Z (0x0031).
    /// </summary>
    InventoryMove = 0x0031,

    /// <summary>
    /// Buys a bag: (u8, u16).
    /// Evidence: Observed in engine.dll ?SendInventoryBuyBag@IScavengerWorldNet@@UAEXEG@Z (0x0032).
    /// </summary>
    InventoryBuyBag = 0x0032,

    /// <summary>
    /// Spends one stat point: (u8).
    /// Evidence: Observed in engine.dll ?SendInventoryAllocationPoint@IScavengerWorldNet@@UAEXE@Z (0x0033).
    /// </summary>
    InventoryAllocationPoint = 0x0033,

    /// <summary>
    /// Buys PowerUser time: (u8, u8, u16).
    /// Evidence: Observed in engine.dll ?SendInventoryBuyPowerUser@IScavengerWorldNet@@UAEXEEG@Z (0x0034).
    /// </summary>
    InventoryBuyPowerUser = 0x0034,

    /// <summary>
    /// Buys a character slot: (u8, u16).
    /// Evidence: Observed in engine.dll ?SendInventoryBuyCharacterSlot@IScavengerWorldNet@@UAEXEG@Z (0x0035).
    /// </summary>
    InventoryBuyCharacterSlot = 0x0035,

    /// <summary>
    /// Lists pending presents. No payload.
    /// Evidence: Observed in engine.dll ?SendPresentPeek@IScavengerWorldNet@@UAEXXZ (0x006B).
    /// </summary>
    PresentPeek = 0x006B,

    /// <summary>
    /// Accepts a present: (u32, u16).
    /// Evidence: Observed in engine.dll ?SendPresentAccept@IScavengerWorldNet@@UAEXKG@Z (0x006C).
    /// </summary>
    PresentAccept = 0x006C,

    /// <summary>
    /// Discards a present: (u32).
    /// Evidence: Observed in engine.dll ?SendPresentDispose@IScavengerWorldNet@@UAEXK@Z (0x006D).
    /// </summary>
    PresentDispose = 0x006D,

    /// <summary>
    /// Buys a potion slot. No payload.
    /// Evidence: Observed in engine.dll ?SendInventoryBuyPotionSlot@IScavengerWorldNet@@UAEXXZ (0x006F).
    /// </summary>
    InventoryBuyPotionSlot = 0x006F,

    /// <summary>
    /// Buys a stage rank clear. No payload.
    /// Evidence: Observed in engine.dll ?SendInventoryBuyStageRankClear@IScavengerWorldNet@@UAEXXZ (0x0070).
    /// </summary>
    InventoryBuyStageRankClear = 0x0070,

    /// <summary>
    /// Buys stage level-free access. No payload.
    /// Evidence: Observed in engine.dll ?SendInventoryBuyStageLevelFree@IScavengerWorldNet@@UAEXXZ (0x0071).
    /// </summary>
    InventoryBuyStageLevelFree = 0x0071,

    /// <summary>
    /// Stacks potions: (u8, u8).
    /// Evidence: Observed in engine.dll ?SendInventoryStackPotion@IScavengerWorldNet@@UAEXEE@Z (0x0073).
    /// </summary>
    InventoryStackPotion = 0x0073,

    /// <summary>
    /// Blacksmith reinforce: (u8, u8, u8, u8*).
    /// Evidence: Observed in engine.dll ?SendEncahntReinforce@IScavengerWorldNet@@UAEXEEEPAE@Z (0x0074).
    /// </summary>
    EnchantReinforce = 0x0074,

    // --- Fields / Rooms ---
    /// <summary>
    /// Requests the active field / room list in the channel.
    /// Evidence: Observed in engine.dll ?SendFieldList (0x36191ba0, opcode 0x0036).
    /// </summary>
    FieldList = 0x0036,

    /// <summary>
    /// Player enters a field / room by room number: (u16 roomNumber, string password).
    /// Evidence: Observed in engine.dll ?SendFieldEnter@IScavengerWorldNet@@UAEXGPAD@Z (opcode 0x0038).
    /// </summary>
    FieldEnter = 0x0038,

    /// <summary>
    /// Quick join into an open room.
    /// Evidence: Observed in engine.dll ?SendFieldQuickEnter@IScavengerWorldNet@@UAEXXZ (opcode 0x0039).
    /// </summary>
    FieldQuickEnter = 0x0039,

    /// <summary>
    /// Player exits a field / room.
    /// Evidence: Observed in engine.dll ?SendFieldExit@IScavengerWorldNet@@UAEXXZ (opcode 0x003A).
    /// </summary>
    FieldExit = 0x003A,

    /// <summary>
    /// Host creates a new field / room.
    /// Evidence: Observed in engine.dll ?SendFieldCreate@IScavengerWorldNet@@UAEXPAD00EEEGEEEE@Z (opcode 0x003B).
    /// </summary>
    FieldCreate = 0x003B,

    /// <summary>
    /// Host changes room settings / rules.
    /// Evidence: Observed in engine.dll ?SendFieldChangeRule@IScavengerWorldNet@@UAEXPAD00EEGEE@Z (opcode 0x0041).
    /// </summary>
    FieldChangeRule = 0x0041,

    /// <summary>
    /// Player toggles ready state in waiting room.
    /// Evidence: Observed in engine.dll ?SendFieldReady@IScavengerWorldNet@@UAEXE@Z (opcode 0x003D).
    /// </summary>
    FieldReady = 0x003D,

    /// <summary>
    /// Player requests to switch team (Red &lt;-&gt; Blue) in waiting room.
    /// Evidence: Observed in engine.dll ?SendFieldChangeTeam@IScavengerWorldNet@@UAEXXZ (opcode 0x003E).
    /// </summary>
    FieldChangeTeam = 0x003E,

    /// <summary>
    /// Host changes the open/close status of a player slot in waiting room.
    /// Evidence: Observed in engine.dll ?SendFieldChangeSlotStatus@IScavengerWorldNet@@UAEXEE@Z (opcode 0x0042).
    /// </summary>
    FieldChangeSlotStatus = 0x0042,

    /// <summary>
    /// Chat message inside a field / room sent by client.
    /// Evidence: Observed in engine.dll ?SendFieldChat@IScavengerWorldNet@@UAEXPAD@Z (opcode 0x0047).
    /// </summary>
    FieldChat = 0x0047,

    // --- UDP (40708 / 40709) ---
    /// <summary>
    /// UDP hole punch probe / keep-alive sent to UDP Port 1 (40708).
    /// Evidence: Observed in Rakion.bin 0x00473B84 and RakionWorldServ.exe 0x00429554.
    /// </summary>
    UdpHolePunch1 = 0x0201,

    /// <summary>
    /// UDP hole punch probe / keep-alive sent to UDP Port 2 (40709).
    /// Evidence: Observed in Rakion.bin 0x00473C2E and RakionWorldServ.exe 0x00429557.
    /// </summary>
    UdpHolePunch2 = 0x0202,

    /// <summary>
    /// UDP tunneling / P2P relay packet.
    /// Evidence: Observed in RakionWorldServ.exe 0x00429546.
    /// </summary>
    UdpTunneling = 0x0401,
}
