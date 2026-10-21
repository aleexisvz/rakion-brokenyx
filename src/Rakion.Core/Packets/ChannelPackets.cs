using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Entry representing a single channel in the channel list packet.
/// </summary>
public sealed record ChannelListEntry(
    byte Index,
    ChannelType Type,
    byte CurrentPlayers,
    byte MaxPlayers,
    string Name);

/// <summary>
/// Client request to retrieve the channel list.
/// Evidence: Observed in engine.dll ?SendChannelList@IScavengerWorldNet@@UAEXEE@Z (Opcode 0x001D).
/// </summary>
public sealed record ChannelListRequestPacket(
    byte Category = 0,
    byte SubCategory = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelList;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Category);
        writer.WriteByte(SubCategory);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelListRequestPacket packet)
    {
        var reader = new PacketReader(span);
        byte cat = reader.Remaining > 0 ? reader.ReadByte() : (byte)0;
        byte sub = reader.Remaining > 0 ? reader.ReadByte() : (byte)0;
        packet = new ChannelListRequestPacket(cat, sub);
        return true;
    }
}

/// <summary>
/// Server response providing the list of available lobby channels.
/// Evidence: Observed in engine.dll OnRecvChannelList (0x361931E0, Opcode 0x001D).
/// </summary>
public sealed record ChannelListResponsePacket(
    IReadOnlyList<ChannelListEntry> Channels) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelList;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte((byte)Channels.Count);
        for (int i = 0; i < Channels.Count; i++)
        {
            var ch = Channels[i];
            writer.WriteByte(ch.Index);
            writer.WriteByte((byte)ch.Type);
            writer.WriteByte(ch.CurrentPlayers);
            writer.WriteByte(ch.MaxPlayers);
            writer.WriteNullTerminatedString(ch.Name);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelListResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        byte count = reader.ReadByte();
        var list = new List<ChannelListEntry>(count);
        for (int i = 0; i < count; i++)
        {
            if (reader.Remaining < 4)
            {
                return false;
            }

            byte index = reader.ReadByte();
            byte type = reader.ReadByte();
            byte current = reader.ReadByte();
            byte max = reader.ReadByte();
            string name = reader.ReadNullTerminatedString();

            list.Add(new ChannelListEntry(index, (ChannelType)type, current, max, name));
        }

        packet = new ChannelListResponsePacket(list);
        return true;
    }
}

/// <summary>
/// Client request to join a specific lobby channel.
/// Evidence: Observed in engine.dll ?SendChannelEnter@IScavengerWorldNet@@UAEXEPAD@Z (Opcode 0x001F).
/// </summary>
public sealed record ChannelEnterRequestPacket(
    byte ChannelIndex,
    string Password = "") : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ChannelIndex);
        writer.WriteNullTerminatedString(Password);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelEnterRequestPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        byte index = reader.ReadByte();
        string pass = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : string.Empty;
        packet = new ChannelEnterRequestPacket(index, pass);
        return true;
    }
}

/// <summary>
/// Server response to channel enter request (also sent as lobby transition confirmation upon character select).
/// Evidence: Observed in engine.dll OnRecvChannelEnter (0x361933F0, Opcode 0x001F) and rakion.bin (0x00473DA0).
/// Wire layout:
///   [0]      ResultCode (byte, 0 = success)
///   [1]      ChannelIndex (byte, 0 = Beginner, 1 = Free, etc.)
///   [2..3]   ServerId (ushort, must match client's WorldNet ServerId [WorldNet + 0x14], e.g. 0)
///   [4..]    ChannelName (null-terminated string)
///   [4+len+1] ChannelType (byte: 0 = Beginner, 1 = Free, 2 = Clan)
///   [4+len+2] MaxPlayers (byte: e.g. 100)
///   [4+len+3..6] Flags (uint32: e.g. 0)
/// </summary>
public sealed record ChannelEnterResponsePacket(
    byte ResultCode,
    byte ChannelIndex = 0,
    ushort ServerId = 0,
    string ChannelName = "",
    byte ChannelType = 0,
    byte MaxPlayers = 100,
    uint Flags = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
        if (ResultCode == 0)
        {
            writer.WriteByte(ChannelIndex);
            writer.WriteUInt16(ServerId);
            writer.WriteNullTerminatedString(ChannelName);
            writer.WriteByte(ChannelType);
            writer.WriteByte(MaxPlayers);
            writer.WriteUInt32(Flags);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelEnterResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        byte result = reader.ReadByte();
        if (result != 0)
        {
            packet = new ChannelEnterResponsePacket(result);
            return true;
        }

        if (reader.Remaining < 3)
        {
            return false;
        }

        byte index = reader.ReadByte();
        ushort serverId = reader.ReadUInt16();
        string name = reader.ReadNullTerminatedString();

        if (reader.Remaining < 6)
        {
            return false;
        }

        byte type = reader.ReadByte();
        byte max = reader.ReadByte();
        uint flags = reader.ReadUInt32();

        packet = new ChannelEnterResponsePacket(result, index, serverId, name, type, max, flags);
        return true;
    }
}

/// <summary>
/// Client request to exit the current lobby channel.
/// Evidence: Observed in engine.dll ?SendChannelExit@IScavengerWorldNet@@UAEXXZ (Opcode 0x0020).
/// </summary>
public sealed record ChannelExitRequestPacket : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelExit;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelExitRequestPacket packet)
    {
        packet = new ChannelExitRequestPacket();
        return true;
    }
}

/// <summary>
/// Server response to channel exit request.
/// Evidence: Observed in engine.dll OnRecvChannelExit (0x36193490, Opcode 0x0020).
/// </summary>
public sealed record ChannelExitResponsePacket(byte ResultCode = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelExit;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelExitResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        packet = new ChannelExitResponsePacket(reader.ReadByte());
        return true;
    }
}

/// <summary>
/// Client message sent to the lobby channel chat.
/// Evidence: Observed in engine.dll ?SendChannelChat@IScavengerWorldNet@@UAEXPAD@Z (Opcode 0x0022).
/// </summary>
public sealed record ChannelChatRequestPacket(string Message) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelChat;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(Message);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelChatRequestPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string msg = reader.ReadNullTerminatedString();
        packet = new ChannelChatRequestPacket(msg);
        return true;
    }
}

/// <summary>
/// Server broadcast of a lobby channel chat message to players in that channel.
/// Evidence: Observed in engine.dll OnRecvChannelChat (0x361934E0, Opcode 0x0022).
/// </summary>
public sealed record ChannelChatBroadcastPacket(
    byte ChatType,
    string FormattedMessage) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelChat;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ChatType);
        writer.WriteNullTerminatedString(FormattedMessage);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelChatBroadcastPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        byte type = reader.ReadByte();
        string msg = reader.ReadNullTerminatedString();
        packet = new ChannelChatBroadcastPacket(type, msg);
        return true;
    }
}

/// <summary>
/// Character summary inside ChannelCharactersResponsePacket.
/// </summary>
public sealed record ChannelCharacterSummary(
    byte Slot,
    ushort Grade,
    string Name,
    byte Class,
    byte Level,
    uint ClanId);

/// <summary>
/// Client request to list the active characters present in the channel lobby.
/// Evidence: Observed in engine.dll ?SendChannelCharacters@IScavengerWorldNet@@UAEXXZ (Opcode 0x001E).
/// </summary>
public sealed record ChannelCharactersRequestPacket : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelCharacters;

    public void Serialize(PacketWriter writer)
    {
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelCharactersRequestPacket packet)
    {
        packet = new ChannelCharactersRequestPacket();
        return true;
    }
}

/// <summary>
/// Server response listing characters currently present in the channel lobby.
/// Evidence: Observed in engine.dll OnRecvChannelCharacters (0x361932A0, Opcode 0x001E).
/// </summary>
public sealed record ChannelCharactersResponsePacket(
    byte ChannelIndex,
    byte CharacterCount,
    byte PageIndex,
    string ChannelName,
    string ServerName,
    IReadOnlyList<ChannelCharacterSummary> Characters) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.ChannelCharacters;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ChannelIndex);
        writer.WriteByte(CharacterCount);
        writer.WriteByte(PageIndex);
        writer.WriteNullTerminatedString(ChannelName);
        writer.WriteNullTerminatedString(ServerName);

        for (int i = 0; i < Characters.Count; i++)
        {
            var c = Characters[i];
            writer.WriteByte(c.Slot);
            writer.WriteUInt16(c.Grade);
            writer.WriteNullTerminatedString(c.Name);
            writer.WriteByte(c.Class);
            writer.WriteByte(c.Level);
            writer.WriteUInt32(c.ClanId);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out ChannelCharactersResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 3)
        {
            return false;
        }

        byte chIndex = reader.ReadByte();
        byte count = reader.ReadByte();
        byte page = reader.ReadByte();
        string chName = reader.ReadNullTerminatedString();
        string srvName = reader.ReadNullTerminatedString();

        var chars = new List<ChannelCharacterSummary>(count);
        for (int i = 0; i < count; i++)
        {
            if (reader.Remaining < 3)
            {
                return false;
            }

            byte slot = reader.ReadByte();
            ushort grade = reader.ReadUInt16();
            string name = reader.ReadNullTerminatedString();

            if (reader.Remaining < 6)
            {
                return false;
            }

            byte cls = reader.ReadByte();
            byte level = reader.ReadByte();
            uint clanId = reader.ReadUInt32();

            chars.Add(new ChannelCharacterSummary(slot, grade, name, cls, level, clanId));
        }

        packet = new ChannelCharactersResponsePacket(chIndex, count, page, chName, srvName, chars);
        return true;
    }
}

/// <summary>
/// Client request to send a private whisper to another character.
/// Evidence: Observed in engine.dll ?SendCharacterWhisper@IScavengerWorldNet@@UAEXPAD0@Z (Opcode 0x0016).
/// </summary>
public sealed record CharacterWhisperRequestPacket(
    string TargetCharacterName,
    string Message) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterWhisper;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(TargetCharacterName);
        writer.WriteNullTerminatedString(Message);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out CharacterWhisperRequestPacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        string target = reader.ReadNullTerminatedString();
        string msg = reader.ReadNullTerminatedString();
        packet = new CharacterWhisperRequestPacket(target, msg);
        return true;
    }
}

/// <summary>
/// Server whisper delivery to recipient, or result response to sender.
/// Evidence: Observed in engine.dll OnRecvCharacterWhisper (0x36193000, Opcode 0x0016).
/// </summary>
public sealed record CharacterWhisperResponsePacket(
    byte ResultCode,
    string CharacterName = "",
    string Message = "") : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterWhisper;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
        writer.WriteNullTerminatedString(CharacterName);
        writer.WriteNullTerminatedString(Message);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out CharacterWhisperResponsePacket packet)
    {
        packet = null!;
        var reader = new PacketReader(span);
        if (reader.Remaining < 1)
        {
            return false;
        }

        byte result = reader.ReadByte();
        string name = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : string.Empty;
        string msg = reader.Remaining > 0 ? reader.ReadNullTerminatedString() : string.Empty;
        packet = new CharacterWhisperResponsePacket(result, name, msg);
        return true;
    }
}
