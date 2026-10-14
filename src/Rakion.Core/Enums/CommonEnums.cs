namespace Rakion.Core.Enums;

/// <summary>
/// Status of a GameServer reported in the ServerList response packet (0x0101).
/// </summary>
public enum ServerStatus : byte
{
    Normal = 0,
    Busy = 1,
    Full = 2,
    Offline = 3,
}

/// <summary>
/// Channel types inside a WorldServer.
/// </summary>
public enum ChannelType : byte
{
    Beginner = 0,
    Free = 1,
    Clan = 2,
    Pro = 3,
}

/// <summary>
/// Team affiliation in team-based game modes.
/// </summary>
public enum TeamType : byte
{
    None = 0,
    Red = 1,
    Blue = 2,
}

/// <summary>
/// State of a player slot in a room/field.
/// </summary>
public enum RoomSlotStatus : byte
{
    Empty = 0,
    Occupied = 1,
    Closed = 2,
}

/// <summary>
/// IPC commands exchanged between Broker and WorldServer.
/// Evidence: Observed in BrokenServer.Network.IPCCommand.
/// </summary>
public enum IpcCommand : byte
{
    RequestServerInfo = 0x01,
    InfoServer = 0x02,
    RequestLogin = 0x03,
    InfoLogin = 0x04,
}
