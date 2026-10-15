using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// World server login part 2 packet completing client handshake and enabling Character Select UI (Opcode 0x000D).
/// Evidence: Observed in engine.dll jump table opcode 13 (0x000D) calling OnRecvLogin2 (0x36196690).
/// Payload layout:
///   [0]    byte ResultCode (0)
///   [1]    byte (0)
///   [2]    byte (0)
///   [3..]  byte[240] zeroes
///   [..]   byte[120] zeroes
///   [..]   byte[480] zeroes
///   [..]   byte[480] zeroes
///   [..]   byte[480] zeroes
///   [..]   uint (0)
///   [..]   uint (0)
/// Total payload size: 1811 bytes.
/// </summary>
public sealed record WorldLoginPart2Packet(byte ResultCode = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.WorldLoginPart2;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
        if (ResultCode != 0)
        {
            return;
        }

        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteZeroBytes(240);
        writer.WriteZeroBytes(120);
        writer.WriteZeroBytes(480);
        writer.WriteZeroBytes(480);
        writer.WriteZeroBytes(480);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out WorldLoginPart2Packet packet)
    {
        if (payload.Length < 1)
        {
            packet = default!;
            return false;
        }

        packet = new WorldLoginPart2Packet(payload[0]);
        return true;
    }
}

