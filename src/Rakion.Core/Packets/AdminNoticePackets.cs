using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// GM notice request: [u8 Scope][string TargetCharacter][string Text]. Scope 0 reaches every player with a character,
/// 2 those in a room's waiting lobby and 3 those in a match; a target name restricts it to that character
/// (RakionWorldServ.exe 0x40C870, Observed).
/// </summary>
public sealed record AdminNoticeRequestPacket(byte Scope, string TargetCharacter, string Text) : IPacket
{
    public const byte Everyone = 0;
    public const byte InRoomLobby = 2;
    public const byte InMatch = 3;
    public const int MaxTargetLength = 12;
    public const int MaxTextLength = 0x80;

    public ushort Opcode => (ushort)ClientOpcode.AdminNotice;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Scope);
        writer.WriteNullTerminatedString(TargetCharacter);
        writer.WriteNullTerminatedString(Text);
    }

    /// <summary>Parses the request and refuses names and texts the legacy server disconnects for (0x41F317 / 0x41F352).</summary>
    public static bool TryParse(ReadOnlySpan<byte> span, out AdminNoticeRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 3)
        {
            return false;
        }

        var reader = new PacketReader(span);
        byte scope = reader.ReadByte();
        string target = reader.ReadNullTerminatedString();
        string text = reader.ReadNullTerminatedString();
        if (target.Length > MaxTargetLength || text.Length > MaxTextLength)
        {
            return false;
        }

        packet = new AdminNoticeRequestPacket(scope, target, text);
        return true;
    }
}

/// <summary>Answer to a targeted GM notice: [u8 Result] 0 delivered, 1 the character is not online.</summary>
public sealed record AdminNoticeResponsePacket(byte Result) : IPacket
{
    public const byte Delivered = 0;
    public const byte NotOnline = 1;

    public ushort Opcode => (ushort)ServerOpcode.AdminNotice;

    public void Serialize(PacketWriter writer) => writer.WriteByte(Result);
}

/// <summary>
/// System notice shown to the player: [string Text] (opcode 0x63). The text is cut at
/// <see cref="AdminNoticeRequestPacket.MaxTextLength"/> characters: the legacy server never sends longer ones and a
/// 150-character notice crashed the v258 client with a buffer overrun (observed with the real client).
/// </summary>
public sealed record SystemNoticePacket(string Text) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.SystemNotice;

    public void Serialize(PacketWriter writer) =>
        writer.WriteNullTerminatedString(Text.Length > AdminNoticeRequestPacket.MaxTextLength ? Text[..AdminNoticeRequestPacket.MaxTextLength] : Text);
}
