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
