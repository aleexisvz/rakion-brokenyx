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
    /// Server confirmation response to GameGuard verification.
    /// Evidence: Observed in RakionWorldServ opcode 16 stub response (0x001B).
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
