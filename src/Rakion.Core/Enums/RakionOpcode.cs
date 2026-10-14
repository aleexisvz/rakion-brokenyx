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
    /// World server session authentication and character list handshake.
    /// Evidence: Observed in RakionWorldServ switch jump table (0x004299B8) and OnRecvLogin in rakion.bin.
    /// </summary>
    WorldLogin = 0x0002,

    /// <summary>
    /// Direct WorldLogin opcode variant used by IScavengerWorldNet::SendLogin(ticket, user).
    /// Evidence: Observed in engine.dll ?SendLogin@IScavengerWorldNet@@UAEXKPAD@Z.
    /// </summary>
    WorldLoginAlt = 0x000C,

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
    /// Character deletion confirmation response.
    /// Evidence: Observed in RakionWorldServ opcode 19 handler (0x002D).
    /// </summary>
    CharacterDeleteResponse = 0x002D,

    /// <summary>
    /// Character selection confirmation response.
    /// Evidence: Observed in RakionWorldServ opcode 20 handler (0x002E).
    /// </summary>
    CharacterSelectResponse = 0x002E,

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
}
