using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Character creation request sent by client on opcode 0x0012.
/// Evidence: Observed in engine.dll ?SendCharacterCreate@IScavengerWorldNet@@UAEXPADEE@Z.
/// </summary>
public sealed record CharacterCreateRequestPacket(
    string Name,
    CharacterClass Class,
    byte Slot) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterCreate;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteNullTerminatedString(Name);
        writer.WriteByte((byte)Class);
        writer.WriteByte(Slot);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterCreateRequestPacket packet)
    {
        try
        {
            var reader = new PacketReader(payload);
            string name = reader.ReadNullTerminatedString();
            byte classId = reader.ReadByte();
            byte slot = reader.ReadByte();

            packet = new CharacterCreateRequestPacket(name, (CharacterClass)classId, slot);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

/// <summary>
/// Character creation confirmation response sent by server on opcode 0x0012.
/// Evidence: Observed in engine.dll 0x36192e30 and RakionWorldServ.exe 0x00414e3d.
/// Wire layout:
///   [0]    ResultCode (byte, 0 = success)
///   [1..4] CharacterId (uint32, only present if ResultCode == 0)
/// </summary>
public sealed record CharacterCreateResponsePacket(
    byte ResultCode,
    uint CharacterId = 0) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterCreate;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);

        if (ResultCode != 0)
        {
            return;
        }

        writer.WriteUInt32(CharacterId);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out CharacterCreateResponsePacket packet)
    {
        if (payload.Length < 1)
        {
            packet = default!;
            return false;
        }

        try
        {
            var reader = new PacketReader(payload);
            byte result = reader.ReadByte();

            if (result != 0 || reader.Remaining < 4)
            {
                packet = new CharacterCreateResponsePacket(result, 0);
                return true;
            }

            uint id = reader.ReadUInt32();
            packet = new CharacterCreateResponsePacket(result, id);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

