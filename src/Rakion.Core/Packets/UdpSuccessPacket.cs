using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// UDP probe success packet exchanged between client and WorldServer.
/// Opcode: 0x000E.
/// Evidence:
/// - Client outgoing: engine.dll ?SendSuccessUDP@IScavengerWorldNet@@UAEXE@Z (0x36190C20).
/// - Server incoming/dispatch: engine.dll OnRecvSuccessUDP (0x36192DA0) &amp; Rakion.bin OnSuccessUDP (0x00477200).
/// - Server outgoing: RakionWorldServ 0x4208b3 (8-byte TCP payload) and engine.dll 0x36192DA0 (13-byte wire layout: Result, IP1, Port1, IP2, Port2).
/// </summary>
public sealed record UdpSuccessPacket(
    byte Result = 0,
    uint Ip1 = 0x0100007F,
    ushort Port1 = 40708,
    uint Ip2 = 0x0100007F,
    ushort Port2 = 40709) : IPacket
{
    public const int PayloadSize = 13;

    public ushort Opcode => (ushort)RakionOpcode.UdpSuccess;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(Result);
        writer.WriteUInt32(Ip1);
        writer.WriteUInt16(Port1);
        writer.WriteUInt32(Ip2);
        writer.WriteUInt16(Port2);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out UdpSuccessPacket packet)
    {
        if (payload.Length < 1)
        {
            packet = default!;
            return false;
        }

        var reader = new PacketReader(payload);
        byte result = reader.ReadByte();

        uint ip1 = reader.Remaining >= 4 ? reader.ReadUInt32() : 0x0100007F;
        ushort port1 = reader.Remaining >= 2 ? reader.ReadUInt16() : (ushort)40708;
        uint ip2 = reader.Remaining >= 4 ? reader.ReadUInt32() : 0x0100007F;
        ushort port2 = reader.Remaining >= 2 ? reader.ReadUInt16() : (ushort)40709;

        packet = new UdpSuccessPacket(result, ip1, port1, ip2, port2);
        return true;
    }
}
