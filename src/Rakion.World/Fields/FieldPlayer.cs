using Rakion.World.Session;

namespace Rakion.World.Fields;

/// <summary>
/// State of a single player inside a room.
/// </summary>
public sealed class FieldPlayer
{
    public WorldSession Session { get; }
    public byte SlotIndex { get; set; }
    public byte Team { get; set; } // 0=None, 1=Red, 2=Blue
    public bool IsReady { get; set; }
    public bool IsMaster { get; set; }

    public FieldPlayer(WorldSession session, byte slotIndex, byte team, bool isMaster)
    {
        Session = session;
        SlotIndex = slotIndex;
        Team = team;
        IsMaster = isMaster;
        IsReady = isMaster; // Host is always ready
    }
}
