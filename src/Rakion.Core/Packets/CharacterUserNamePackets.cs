using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>Request for the account of a character: [string CharacterName], below 13 characters (RakionWorldServ.exe 0x4207D1).</summary>
public sealed record CharacterGetUserNameRequestPacket(string CharacterName) : IPacket
{
    public const int MaxNameLength = 12;

    public ushort Opcode => (ushort)ClientOpcode.CharacterGetUserName;

    public void Serialize(PacketWriter writer) => writer.WriteNullTerminatedString(CharacterName);

    public static bool TryParse(ReadOnlySpan<byte> span, out CharacterGetUserNameRequestPacket packet)
    {
        packet = null!;
        if (span.IsEmpty)
        {
            return false;
        }

        string name = new PacketReader(span).ReadNullTerminatedString();
        if (name.Length == 0 || name.Length > MaxNameLength)
        {
            return false;
        }

        packet = new CharacterGetUserNameRequestPacket(name);
        return true;
    }
}

/// <summary>
/// Account of a character for the Messenger: [u8 Status][string Account][string BuddyName]. Status 0 found, 1 no
/// such character (meaning of the non-zero value Inferred). Layout Observed in engine.dll 0x36193170.
/// </summary>
public sealed record CharacterGetUserNameResponsePacket(byte Status, string Account = "", string BuddyName = "") : IPacket
{
    public const byte Found = 0;
    public const byte NotFound = 1;

    public ushort Opcode => (ushort)ServerOpcode.CharacterGetUserName;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Status);
        writer.WriteNullTerminatedString(Account);
        writer.WriteNullTerminatedString(BuddyName);
    }
}
