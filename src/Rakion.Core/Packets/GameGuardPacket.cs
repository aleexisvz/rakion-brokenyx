using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// GameGuard authentication check sent by client on opcode 0x0010 and confirmed by server on 0x001B.
/// </summary>
public sealed record GameGuardPacket(byte ResultCode = 0) : IPacket
{
    public ushort Opcode => (ushort)ServerOpcode.GameGuard;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);
    }

    public static bool TryParseRequest(ReadOnlySpan<byte> payload, out byte[] data)
    {
        data = payload.ToArray();
        return true;
    }

    public static bool TryParseResponse(ReadOnlySpan<byte> payload, out GameGuardPacket packet)
    {
        if (payload.Length < 1)
        {
            packet = default!;
            return false;
        }

        packet = new GameGuardPacket(payload[0]);
        return true;
    }
}
