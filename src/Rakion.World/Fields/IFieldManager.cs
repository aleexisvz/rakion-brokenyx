using Rakion.World.Session;

namespace Rakion.World.Fields;

/// <summary>
/// Service managing game rooms (fields) in WorldServer channels.
/// </summary>
public interface IFieldManager
{
    IReadOnlyList<GameField> GetFields(byte channelIndex);

    bool TryGetField(byte channelIndex, ushort roomNumber, out GameField? field);

    bool TryGetFieldBySession(Guid connectionId, out GameField? field, out FieldPlayer? player);

    bool TryCreateField(
        byte channelIndex,
        WorldSession hostSession,
        string name,
        string password,
        byte mode,
        byte map,
        byte maxPlayers,
        byte roundTime,
        byte rounds,
        byte subMode,
        byte levelLimit,
        out GameField? field,
        out byte failureReason);

    bool TryJoinField(
        byte channelIndex,
        ushort roomNumber,
        WorldSession session,
        string password,
        out GameField? field,
        out FieldPlayer? player,
        out byte failureReason);

    bool TryQuickJoinField(
        byte channelIndex,
        WorldSession session,
        out GameField? field,
        out FieldPlayer? player,
        out byte failureReason);

    bool TryLeaveField(
        WorldSession session,
        out GameField? field,
        out FieldPlayer? player,
        out bool roomDestroyed);
}
