using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Authentication response sent by the Broker server to the client.
/// Evidence: Observed byte sequences in BrokenServer.Systems.oPCode:
///   - SV_AUTH_LOGIN (0x1310): Header Opcode 0xCBEB, Sub-Opcode 0x1312, Result Code (8 bytes total)
///   - SV_AUTH_LOGIN_2 (0x3000): Header Opcode 0xEBD3, Sub-Opcode 0x3001, Result Code (8 bytes total)
///   - SV_AUTH_LOGIN_4 (0x4000): Header Opcode 0x1BAF, Sub-Opcode 0x4001, Result Code, Ticket (12 bytes total)
/// </summary>
public sealed record LoginResponsePacket(
    ushort HeaderOpcode,
    ushort SubOpcode,
    LoginResultCode Result,
    uint? SessionTicket = null) : IPacket
{
    public ushort Opcode => HeaderOpcode;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt16(SubOpcode);
        writer.WriteUInt16((ushort)Result);

        if (SessionTicket.HasValue)
        {
            writer.WriteUInt32(SessionTicket.Value);
        }
    }

    /// <summary>
    /// Factory for creating the standard v258 authentication response matching the client's request.
    /// </summary>
    public static LoginResponsePacket Create(
        ushort requestSubOpcode,
        LoginResultCode result,
        uint? sessionTicket = null)
    {
        return requestSubOpcode switch
        {
            0x3000 => new LoginResponsePacket(0xEBD3, 0x3001, result),
            0x4000 => new LoginResponsePacket(0x1BAF, 0x4001, result, sessionTicket ?? 1),
            _ => new LoginResponsePacket(0xCBEB, 0x1312, result)
        };
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, ushort headerOpcode, out LoginResponsePacket packet)
    {
        if (payload.Length < 4)
        {
            packet = new LoginResponsePacket(headerOpcode, 0, LoginResultCode.ServerError);
            return false;
        }

        var reader = new PacketReader(payload);
        ushort subOpcode = reader.ReadUInt16();
        var result = (LoginResultCode)reader.ReadUInt16();
        uint? ticket = reader.Remaining >= 4 ? reader.ReadUInt32() : null;

        packet = new LoginResponsePacket(headerOpcode, subOpcode, result, ticket);
        return true;
    }
}
