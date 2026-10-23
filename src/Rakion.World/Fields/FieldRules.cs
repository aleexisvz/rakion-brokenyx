using Rakion.Core.Packets;

namespace Rakion.World.Fields;

/// <summary>
/// Room rules as the legacy server stores them, named after the room fields they fill (RakionWorldServ.exe room
/// init 0x405440 and rule change 0x4077C0, Observed). The packet records name the same bytes differently; this
/// mapping follows the legacy server:
/// create request rule bytes b0..b2, u16, b3..b6 land in room +0x118, +0x119, +0x11A, +0x11C, +0x11E, +0x111,
/// +0x112, +0x113; the rule change bytes land in +0x118, +0x11A, +0x11C, +0x11E, +0x119.
/// </summary>
public sealed record FieldRules(
    byte Kind,
    byte Mode,
    byte Option,
    ushort TimeLimit,
    byte Goal,
    byte MinLevel,
    byte MaxLevel,
    byte Extra,
    string MapName)
{
    public static FieldRules Default { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, string.Empty);

    /// <summary>
    /// Inferred meanings from the legacy create checks (RakionWorldServ.exe 0x423744..0x4237F3): Mode 1..4 are the
    /// PvP modes (0 skips to a stage check), Option is at most 21, TimeLimit is 290..1210, Goal is 13..30 in mode 2
    /// and 20..50 in mode 3, and the character level must be within MinLevel..MaxLevel.
    /// </summary>
    public static FieldRules FromCreate(FieldCreateRequestPacket request) => new(
        Kind: request.Mode,
        Mode: request.MaxPlayers,
        Option: request.RoundTime,
        TimeLimit: request.Rounds,
        Goal: request.SubMode,
        MinLevel: request.LevelLimit,
        MaxLevel: request.Extra1,
        Extra: request.Extra2,
        MapName: request.MapName);

    public FieldRules With(FieldChangeRuleRequestPacket request) => this with
    {
        Kind = request.Mode,
        Option = request.MaxPlayers,
        TimeLimit = request.RoundTime,
        Goal = request.Rounds,
        Mode = request.SubMode,
        MapName = request.MapName,
    };

    /// <summary>Level range check for joiners. Inferred: a zero maximum means no upper limit.</summary>
    public bool AllowsLevel(int level) => level >= MinLevel && (MaxLevel == 0 || level <= MaxLevel);
}
