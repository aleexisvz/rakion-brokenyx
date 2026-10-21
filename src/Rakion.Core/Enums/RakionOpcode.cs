namespace Rakion.Core.Enums;

/// <summary>
/// Known protocol opcodes for the Rakion v258 network protocol.
/// </summary>
public enum RakionOpcode : ushort
{
    None = 0x0000,

    // --- Broker / Handshake Opcodes ---
    /// <summary>
    /// Sent by the client to request the server list.
    /// Evidence: Observed in BrokenServer.Systems.oPCode and client handshake.
    /// </summary>
    ServerListRequest = 0xCDDF,

    /// <summary>
    /// Response sent to client containing the list of available game servers.
    /// Evidence: Observed in BrokenServer.Systems.ServerListPacket.
    /// </summary>
    ServerListResponse = 0x0101,

    /// <summary>
    /// Client login authentication (standard v258).
    /// Evidence: Observed in BrokenServer SV_AUTH_LOGIN.
    /// </summary>
    Login = 0x1310,

    /// <summary>
    /// Alternative login opcode variant 2.
    /// </summary>
    LoginAlt2 = 0x3000,

    /// <summary>
    /// Alternative login opcode variant 3.
    /// </summary>
    LoginAlt3 = 0x5000,

    /// <summary>
    /// Alternative login opcode variant 4.
    /// </summary>
    LoginAlt4 = 0x4000,

    // --- World Server / Character Management Opcodes ---
    /// <summary>
    /// World server session authentication and character list handshake for retail v258.
    /// Evidence: Observed in engine.dll ?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z and jump table opcode 12 (0x000C).
    /// </summary>
    WorldLogin = 0x000C,

    /// <summary>
    /// Legacy/unencrypted WorldLogin opcode variant.
    /// Evidence: Observed in RakionWorldServ switch jump table (0x004299B8).
    /// </summary>
    WorldLoginLegacy = 0x0002,

    /// <summary>
    /// World server login success/failure result response.
    /// Evidence: Observed in RakionWorldServ 0x4286fd (0x001E).
    /// </summary>
    WorldLoginResponse = 0x001E,

    /// <summary>
    /// World server character roster, gold, cash, and unlocked slots.
    /// Evidence: Observed in RakionWorldServ 0x428832 (0x001F).
    /// </summary>
    WorldCharacterList = 0x001F,

    /// <summary>
    /// World server login part 2 packet completing client handshake and enabling Character Select UI (retail OnRecvLogin2).
    /// Evidence: Observed in engine.dll jump table opcode 13 (0x000D) calling 0x36196690.
    /// </summary>
    WorldLoginPart2 = 0x000D,

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
    /// Direct WorldLogin opcode variant used by IScavengerWorldNet::SendLogin(ticket, user).
    /// Evidence: Observed in engine.dll ?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z.
    /// </summary>
    WorldLoginAlt = 0x000C,

    /// <summary>
    /// Retail WorldLogin alias.
    /// </summary>
    WorldLoginRetail = 0x000C,

    /// <summary>
    /// UDP hole punching / NAT probe success notification and confirmation.
    /// Evidence: Observed in engine.dll ?SendSuccessUDP@IScavengerWorldNet@@UAEXE@Z (0x000E),
    /// OnRecvSuccessUDP (0x36192DA0), and RakionWorldServ 0x4208b3.
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
    /// Server confirmation response to GameGuard verification.
    /// Evidence: Observed in RakionWorldServ opcode 16 stub response (0x001B).
    /// </summary>
    GameGuardResponse = 0x001B,

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
    /// Character name change notification for messenger / buddy system after character creation.
    /// Evidence: Observed in engine.dll ?SendCharacterChangeBuddyName@IScavengerWorldNet@@UAEXPAD@Z (0x0015)
    /// and OnRecvChangeBuddyName jump table opcode 21 calling 0x36192FB0.
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

    /// <summary>
    /// Character deletion confirmation response (retail v258 engine.dll opcode 19).
    /// Evidence: Observed in engine.dll jump table opcode 19 (0x0013) calling 0x36192e70.
    /// </summary>
    CharacterDeleteResponse = 0x0013,

    /// <summary>
    /// Legacy character deletion confirmation response.
    /// Evidence: Observed in RakionWorldServ opcode 19 handler (0x002D).
    /// </summary>
    CharacterDeleteResponseLegacy = 0x002D,

    /// <summary>
    /// Character selection confirmation response (retail v258 engine.dll opcode 20).
    /// Evidence: Observed in engine.dll jump table opcode 20 (0x0014) calling 0x36192f90.
    /// </summary>
    CharacterSelectResponse = 0x0014,

    /// <summary>
    /// Legacy character selection confirmation response.
    /// Evidence: Observed in RakionWorldServ opcode 20 handler (0x002E).
    /// </summary>
    CharacterSelectResponseLegacy = 0x002E,

    // --- Channel / Lobby Opcodes ---
    /// <summary>
    /// Requests or returns the active field / room list in the channel.
    /// Evidence: Observed (71 / 0x47) in WorldServer strings.
    /// </summary>
    FieldList = 0x0047,

    /// <summary>
    /// Quick join into an open room.
    /// Evidence: Observed (79 / 0x4F) in WorldServer strings.
    /// </summary>
    FieldQuickEnter = 0x004F,

    /// <summary>
    /// Player exits a field / room.
    /// Evidence: Observed (81 / 0x51) in WorldServer strings.
    /// </summary>
    FieldExit = 0x0051,

    /// <summary>
    /// Host creates a new field / room.
    /// Evidence: Observed (83 / 0x53) in WorldServer strings.
    /// </summary>
    FieldCreate = 0x0053,

    /// <summary>
    /// Player toggles ready state in waiting room.
    /// Evidence: Observed (97 / 0x61) in WorldServer strings.
    /// </summary>
    FieldReady = 0x0061,

    /// <summary>
    /// Chat message inside a field / room.
    /// Evidence: Observed (127 / 0x7F) in WorldServer strings.
    /// </summary>
    FieldChat = 0x007F,

    // --- UDP NAT Probe / Relay Opcodes ---
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
