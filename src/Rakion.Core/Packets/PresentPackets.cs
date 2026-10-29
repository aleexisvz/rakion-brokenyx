using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Present box answer to a peek (0x006B): [u8 Result][u32 PendingId][u32 ItemId][u16 Extra]. Result 1 means the box is
/// empty. Evidence: engine.dll 0x36194530 reads the fields (Observed); the legacy agent fills them with the oldest
/// pendingpresents row (RakionWorldServ.exe 0x416BF0, Observed). Extra carries coupon data there (Unknown) and is 0.
/// </summary>
public sealed record PresentPeekResponsePacket(byte Result, uint PendingId = 0, uint ItemId = 0) : IPacket
{
    public const byte Found = 0;
    public const byte Empty = 1;

    public ushort Opcode => (ushort)ServerOpcode.PresentPeek;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(PendingId);
        writer.WriteUInt32(ItemId);
        writer.WriteUInt16(0);
    }
}

/// <summary>Takes a present (0x006C): [u32 PendingId][u16 BoxSlot] (engine.dll ?SendPresentAccept@...@@UAEXKG@Z, Observed).</summary>
public sealed record PresentAcceptRequestPacket(uint PendingId, ushort BoxSlot) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.PresentAccept;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt32(PendingId);
        writer.WriteUInt16(BoxSlot);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out PresentAcceptRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 6)
        {
            return false;
        }

        var reader = new PacketReader(span);
        packet = new PresentAcceptRequestPacket(reader.ReadUInt32(), reader.ReadUInt16());
        return true;
    }
}

/// <summary>
/// Answer to a present accept (0x006C): [u8 Result][u32 LimitTime]. engine.dll 0x36194560 reads both (Observed). Legacy
/// results (RakionWorldServ.exe 0x41710E..0x417216, Observed): 1 no such present, 3 box slot taken, 4 database failure.
/// LimitTime is the item's rental end in LegacyGameClock minutes, 0 for a permanent item.
/// </summary>
public sealed record PresentAcceptResponsePacket(byte Result, uint LimitTime = 0) : IPacket
{
    public const byte Accepted = 0;
    public const byte NotFound = 1;
    public const byte SlotTaken = 3;

    public ushort Opcode => (ushort)ServerOpcode.PresentAccept;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(LimitTime);
    }
}

/// <summary>Throws a present away (0x006D): [u32 PendingId] (engine.dll ?SendPresentDispose@...@@UAEXK@Z, Observed).</summary>
public sealed record PresentDisposeRequestPacket(uint PendingId) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.PresentDispose;

    public void Serialize(PacketWriter writer) => writer.WriteUInt32(PendingId);

    public static bool TryParse(ReadOnlySpan<byte> span, out PresentDisposeRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 4)
        {
            return false;
        }

        packet = new PresentDisposeRequestPacket(new PacketReader(span).ReadUInt32());
        return true;
    }
}

/// <summary>Answer to a present dispose (0x006D): [u8 Result], 0 thrown away, 1 no such present (engine.dll 0x36194590, Observed).</summary>
public sealed record PresentDisposeResponsePacket(byte Result) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.PresentDispose;

    public void Serialize(PacketWriter writer) => writer.WriteByte(Result);
}
