using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Host asks to start the match. No payload.
/// Evidence: Observed in engine.dll ?SendFieldGameStart@IScavengerWorldNet@@UAEXXZ (0x36192140, opcode 0x0043).
/// </summary>
public sealed record FieldGameStartRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldGameStart;

    public void Serialize(PacketWriter writer)
    {
    }
}

/// <summary>
/// Match start result: [u8 Result]. 0 is broadcast to the whole room and moves every waiting/ready slot to
/// "in game" and the client to the loading screen; 1..4 go only to the host and show a message.
/// Evidence: Observed in engine.dll 0x36193c80 (reads one byte), rakion.bin 0x474790 (callback, switch on 0..4)
/// and RakionWorldServ.exe 0x4079D0 (3-byte send).
/// </summary>
public sealed record FieldGameStartResponsePacket(byte Result) : IPacket
{
    /// <summary>Match starts.</summary>
    public const byte Started = 0;

    /// <summary>Teams are not balanced, or too few players. Observed result 1 at 0x407A45 / 0x407A76.</summary>
    public const byte TeamsNotBalanced = 1;

    /// <summary>UNKNOWN: second team check (legacy compares two more per-team counters). Observed result 2 at 0x407A5A.</summary>
    public const byte TeamCheckFailed = 2;

    /// <summary>A player other than the host is not ready. Observed result 3 at 0x407B5B.</summary>
    public const byte NotAllReady = 3;

    public ushort Opcode => (ushort)ServerOpcode.FieldGameStart;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameStartResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new FieldGameStartResponsePacket(span[0]);
        return true;
    }
}

/// <summary>
/// Player enters the running match of its room. No payload.
/// Evidence: Observed in engine.dll ?SendFieldGameEnter@IScavengerWorldNet@@UAEXXZ (0x361921C0, opcode 0x0045).
/// </summary>
public sealed record FieldGameEnterRequestPacket : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldGameEnter;

    public void Serialize(PacketWriter writer)
    {
    }
}

/// <summary>
/// Match enter result: [u8 Result][u8 SlotIndex]. 0 is broadcast to the room; 1 (match not running) and
/// 2 (team full) go only to the requester.
/// Evidence: Observed in engine.dll 0x36193cc0 (reads two bytes), rakion.bin 0x4744F0 (callback)
/// and RakionWorldServ.exe 0x407C70 (4-byte send).
/// </summary>
public sealed record FieldGameEnterResponsePacket(byte Result, byte SlotIndex) : IPacket
{
    public const byte Entered = 0;
    public const byte NotRunning = 1;
    public const byte TeamFull = 2;

    public ushort Opcode => (ushort)ServerOpcode.FieldGameEnter;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteByte(SlotIndex);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameEnterResponsePacket packet)
    {
        packet = null!;
        if (span.Length < 2)
        {
            return false;
        }

        packet = new FieldGameEnterResponsePacket(span[0], span[1]);
        return true;
    }
}

/// <summary>
/// Player leaves the running match back to the waiting room: [u8 Value].
/// Evidence: Observed in engine.dll ?SendFieldGameExit@IScavengerWorldNet@@UAEXE@Z (0x36192200, opcode 0x0046).
/// UNKNOWN: meaning of the byte (the legacy server echoes it unchanged).
/// </summary>
public sealed record FieldGameExitRequestPacket(byte Value) : IPacket
{
    public ushort Opcode => (ushort)ClientOpcode.FieldGameExit;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Value);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameExitRequestPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new FieldGameExitRequestPacket(span[0]);
        return true;
    }
}

/// <summary>
/// Match exit broadcast: [u8 Value], the request byte echoed to the room.
/// Evidence: Observed in engine.dll 0x36193cf0 (reads one byte) and RakionWorldServ.exe 0x407E00 (3-byte send).
/// </summary>
public sealed record FieldGameExitBroadcastPacket(byte Value) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldGameExit;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Value);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameExitBroadcastPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new FieldGameExitBroadcastPacket(span[0]);
        return true;
    }
}

/// <summary>
/// Match end broadcast: [u8 Value]. The room goes back to waiting and every in-game slot to "not ready".
/// Evidence: Observed in engine.dll 0x36193ca0 (reads one byte) and RakionWorldServ.exe 0x407BE0 (3-byte send).
/// UNKNOWN: meaning of the byte (Inferred: winner or end reason).
/// </summary>
public sealed record FieldGameEndBroadcastPacket(byte Value) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.FieldGameEnd;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Value);
    }

    public static bool TryParse(ReadOnlySpan<byte> span, out FieldGameEndBroadcastPacket packet)
    {
        packet = null!;
        if (span.Length < 1)
        {
            return false;
        }

        packet = new FieldGameEndBroadcastPacket(span[0]);
        return true;
    }
}
