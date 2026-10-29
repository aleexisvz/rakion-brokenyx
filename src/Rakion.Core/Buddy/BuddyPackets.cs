using System.Net;
using System.Text;
using Rakion.Core.Protocol;

namespace Rakion.Core.Buddy;

/// <summary>
/// Fixed-size text fields of the Messenger protocol: account names are 20 single-byte characters, nicknames and group
/// names 20 UTF-16 characters (40 bytes), both NUL padded.
/// </summary>
public static class BuddyText
{
    public const int AccountBytes = 20;
    public const int WideChars = 20;
    public const int WideBytes = WideChars * 2;

    public static void WriteAccount(PacketWriter writer, string account) =>
        writer.WriteFixedString(Truncate(account, AccountBytes - 1), AccountBytes, Encoding.Latin1);

    public static void WriteWide(PacketWriter writer, string text) =>
        writer.WriteFixedString(Truncate(text, WideChars - 1), WideBytes, Encoding.Unicode);

    public static string ReadAccount(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? field[..end] : field);
    }

    public static string ReadWide(ReadOnlySpan<byte> field)
    {
        for (int i = 0; i + 1 < field.Length; i += 2)
        {
            if (field[i] == 0 && field[i + 1] == 0)
            {
                return Encoding.Unicode.GetString(field[..i]);
            }
        }

        return Encoding.Unicode.GetString(field[..(field.Length & ~1)]);
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}

/// <summary>
/// One friend in the login list or an add answer (148 bytes): [char account[20]][wchar name[20]][wchar group[20]]
/// [u32 reserved x4][byte extension[32]]. Layout Inferred, size consistent with the RET_LOGIN reader of Buddy2.dll.
/// </summary>
public sealed record BuddyFriend(string Account, string DisplayName, string GroupName)
{
    public const int Size = 148;

    public void Serialize(PacketWriter writer)
    {
        BuddyText.WriteAccount(writer, Account);
        BuddyText.WriteWide(writer, DisplayName);
        BuddyText.WriteWide(writer, GroupName);
        writer.WriteZeroBytes(16);
        writer.WriteZeroBytes(32);
    }
}

/// <summary>Answer to the pre-credential request: [u32 seed][u32 cookie].</summary>
public sealed record BuddyPreCredentialPacket(uint Seed, uint Cookie) : IPacket
{
    public ushort Opcode => (ushort)BuddyOpcode.PreCredentialResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt32(Seed);
        writer.WriteUInt32(Cookie);
    }
}

/// <summary>Login answer: [u16 result][u32 udp token][u16 count][count x friend]. Result 0 is success.</summary>
public sealed record BuddyLoginResponsePacket(ushort Result, uint UdpToken = 0, IReadOnlyList<BuddyFriend>? Friends = null) : IPacket
{
    public const ushort Success = 0;
    public const ushort BadCredential = 1;
    public const ushort UnknownAccount = 2;
    public const int MaxFriends = 500;

    public ushort Opcode => (ushort)BuddyOpcode.LoginResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(Result);
        writer.WriteUInt32(UdpToken);
        var friends = Friends ?? [];
        int count = Math.Min(friends.Count, MaxFriends);
        writer.WriteUInt16((ushort)count);
        for (int i = 0; i < count; i++)
        {
            friends[i].Serialize(writer);
        }
    }
}

/// <summary>Add answer: [u16 result] and, on success, the new friend.</summary>
public sealed record BuddyAddResponsePacket(ushort Result, BuddyFriend? Friend = null) : IPacket
{
    public ushort Opcode => (ushort)BuddyOpcode.AddBuddyResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(Result);
        if (Result == 0 && Friend is not null)
        {
            Friend.Serialize(writer);
        }
    }
}

/// <summary>Remove answer: [u16 result] and, on success, the removed account.</summary>
public sealed record BuddyRemoveResponsePacket(ushort Result, string Account = "") : IPacket
{
    public ushort Opcode => (ushort)BuddyOpcode.RemoveBuddyResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(Result);
        if (Result == 0)
        {
            BuddyText.WriteAccount(writer, Account);
        }
    }
}

/// <summary>Answer carrying only a result code (set nick, guild, extensions).</summary>
public sealed record BuddyResultPacket(BuddyOpcode Command, ushort Result) : IPacket
{
    public ushort Opcode => (ushort)Command;

    public void Serialize(PacketWriter writer) => writer.WriteUInt16(Result);
}

/// <summary>A Messenger group (44 bytes): [u16 Id][wchar name[20]][u16 Flags]. Observed in the Buddy2.dll reader 0x100080E8.</summary>
public sealed record BuddyGroup(ushort Id, string Name, ushort Flags)
{
    public const int Size = 44;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(Id);
        BuddyText.WriteWide(writer, Name);
        writer.WriteUInt16(Flags);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out BuddyGroup group)
    {
        group = null!;
        if (span.Length < Size)
        {
            return false;
        }

        group = new BuddyGroup(
            (ushort)(span[0] | (span[1] << 8)),
            BuddyText.ReadWide(span.Slice(2, BuddyText.WideBytes)),
            (ushort)(span[42] | (span[43] << 8)));
        return true;
    }
}

/// <summary>
/// Group list answer: [u16 result][u16 count, at most 50][count x group]. Observed in the Buddy2.dll reader
/// 0x1000807A. With an empty list the client creates its groups with 0x3152.
/// </summary>
public sealed record BuddyGroupListPacket(IReadOnlyList<BuddyGroup> Groups) : IPacket
{
    public const int MaxGroups = 50;

    public ushort Opcode => (ushort)BuddyOpcode.GroupListResponse;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(0);
        int count = Math.Min(Groups.Count, MaxGroups);
        writer.WriteUInt16((ushort)count);
        for (int i = 0; i < count; i++)
        {
            Groups[i].Serialize(writer);
        }
    }
}

/// <summary>An IPv4 endpoint as the Messenger sends it: 4 address bytes and the port in network byte order.</summary>
public readonly record struct BuddyEndpoint(IPAddress Address, ushort Port)
{
    public void Serialize(PacketWriter writer)
    {
        Span<byte> address = stackalloc byte[4];
        if (!Address.MapToIPv4().TryWriteBytes(address, out _))
        {
            address.Clear();
        }

        writer.WriteBytes(address);
        writer.WriteByte((byte)(Port >> 8));
        writer.WriteByte((byte)Port);
    }
}

/// <summary>The client's own UDP endpoint as seen by the server, after its UDP registration.</summary>
public sealed record BuddyVipIpPortPacket(BuddyEndpoint Endpoint) : IPacket
{
    public ushort Opcode => (ushort)BuddyOpcode.VipIpPortNotice;

    public void Serialize(PacketWriter writer) => Endpoint.Serialize(writer);
}

/// <summary>A friend going on or off line: online records carry two endpoints (public and local).</summary>
public sealed record BuddyUserState(string Account, bool Online, BuddyEndpoint? Public = null, BuddyEndpoint? Local = null);

/// <summary>Presence notice: [u16 count] then per user [char account[20]][u8 online] and, when online, two endpoints.</summary>
public sealed record BuddyUserStatePacket(IReadOnlyList<BuddyUserState> States) : IPacket
{
    public ushort Opcode => (ushort)BuddyOpcode.UserStateNotice;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16((ushort)States.Count);
        foreach (var state in States)
        {
            BuddyText.WriteAccount(writer, state.Account);
            writer.WriteByte(state.Online ? (byte)1 : (byte)0);
            if (state.Online)
            {
                (state.Public ?? new BuddyEndpoint(IPAddress.Any, 0)).Serialize(writer);
                (state.Local ?? state.Public ?? new BuddyEndpoint(IPAddress.Any, 0)).Serialize(writer);
            }
        }
    }
}

/// <summary>
/// A client-to-client Messenger message sent through the server when UDP cannot reach the peer:
/// [u8 Flags][u16 InnerOpcode][u16 length, at most 255][inner][u16 count, at most 500][count x char account[20]].
/// Evidence: Observed in the Buddy2.dll builder 0x10008784 (sent as 0x2020 at 0x100087F4).
/// </summary>
public sealed record BuddyTunnelRequest(byte Flags, ushort InnerOpcode, byte[] Inner, IReadOnlyList<string> Recipients)
{
    public const int MaxInner = 255;
    public const int MaxRecipients = 500;

    /// <summary>Friend request and its answer between players who are not friends yet (Inferred).</summary>
    public const ushort AddBuddyAsk = 0xC041;
    public const ushort AddBuddyAnswer = 0xC042;

    public static bool TryParse(ReadOnlySpan<byte> payload, out BuddyTunnelRequest request)
    {
        request = null!;
        if (payload.Length < 7)
        {
            return false;
        }

        int length = payload[3] | (payload[4] << 8);
        if (length > MaxInner || payload.Length < 7 + length)
        {
            return false;
        }

        int countAt = 5 + length;
        int count = payload[countAt] | (payload[countAt + 1] << 8);
        if (count is < 1 or > MaxRecipients || payload.Length < countAt + 2 + (count * BuddyText.AccountBytes))
        {
            return false;
        }

        var recipients = new string[count];
        for (int i = 0; i < count; i++)
        {
            recipients[i] = BuddyText.ReadAccount(payload.Slice(countAt + 2 + (i * BuddyText.AccountBytes), BuddyText.AccountBytes));
        }

        request = new BuddyTunnelRequest(payload[0], (ushort)(payload[1] | (payload[2] << 8)), payload.Slice(5, length).ToArray(), recipients);
        return true;
    }
}

/// <summary>
/// A tunnelled message delivered to its recipient: [sender as account / name / group][u16 length][u16 InnerOpcode][inner].
/// Evidence: Observed in the Buddy2.dll 0x2021 reader 0x10007D43.
/// </summary>
public sealed record BuddyTunnelNoticePacket(string Account, string DisplayName, string GroupName, ushort InnerOpcode, byte[] Inner) : IPacket
{
    public ushort Opcode => (ushort)BuddyOpcode.TunnelNotice;

    public void Serialize(PacketWriter writer)
    {
        BuddyText.WriteAccount(writer, Account);
        BuddyText.WriteWide(writer, DisplayName);
        BuddyText.WriteWide(writer, GroupName);
        writer.WriteUInt16((ushort)Inner.Length);
        writer.WriteUInt16(InnerOpcode);
        writer.WriteBytes(Inner);
    }
}
