using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Tells the players of a lobby channel that someone entered: a 0x001F with result 0 whose record is the newcomer,
/// in the layout of a <see cref="ChannelCharacterSummary"/> ([u8 list id][u16 user id][cstr name][u8][u8][u32]).
/// Evidence (Observed): engine.dll OnRecvChannelEnter (0x361933F0) copies that record; rakion.bin 0x473DEA compares
/// the u16 with the player's own id and, when it differs, adds the record to the user list (0x40CC40).
/// </summary>
public sealed record ChannelUserEnterNoticePacket(byte ListId, ushort UserId, ChannelCharacterSummary Character) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.ChannelEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(0);
        writer.WriteByte(ListId);
        writer.WriteUInt16(UserId);
        writer.WriteNullTerminatedString(Character.Name);
        writer.WriteByte(Character.Level);
        writer.WriteByte(Character.Class);
        writer.WriteUInt32(Character.ClanId);
    }
}

/// <summary>
/// Removes a player from the user list of the others: 0x0020 [u8 list id].
/// Evidence (Observed): engine.dll OnRecvChannelExit (0x36193490) passes the byte to rakion.bin 0x42EB30, which deletes
/// the list line whose id matches.
/// </summary>
public sealed record ChannelUserLeaveNoticePacket(byte ListId) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.ChannelExit;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ListId);
    }
}
