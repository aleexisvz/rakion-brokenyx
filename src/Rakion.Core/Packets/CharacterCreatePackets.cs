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
/// </summary>
public sealed record CharacterCreateResponsePacket(
    byte ResultCode,
    CharacterSummary? Character = null) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.CharacterCreate;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);

        if (ResultCode != 0 || Character is null)
        {
            return;
        }

        writer.WriteByte(Character.Slot);
        writer.WriteUInt32(Character.Id);
        writer.WriteNullTerminatedString(Character.Name);
        writer.WriteByte((byte)Character.Class);
        writer.WriteByte(Character.Level);
        writer.WriteUInt32(Character.Exp);
        writer.WriteUInt32(Character.LevelPoint);
        writer.WriteUInt16(Character.Hit1);
        writer.WriteUInt16(Character.Hit2);
        writer.WriteUInt16(Character.Hit3);
        writer.WriteUInt16(Character.Hit4);
        writer.WriteUInt16(Character.Chit);
        writer.WriteUInt16(Character.Hp);
        writer.WriteUInt16(Character.Ap);
        writer.WriteUInt16(Character.AttackSpeed);
        writer.WriteUInt16(Character.Speed);
        writer.WriteUInt16(Character.MaxCp);
        writer.WriteUInt32(Character.Win);
        writer.WriteUInt32(Character.Lose);
        writer.WriteUInt32(Character.Draw);
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

            if (result != 0 || reader.Remaining < 10)
            {
                packet = new CharacterCreateResponsePacket(result);
                return true;
            }

            byte slot = reader.ReadByte();
            uint id = reader.ReadUInt32();
            string name = reader.ReadNullTerminatedString();
            byte classId = reader.ReadByte();
            byte level = reader.ReadByte();
            uint exp = reader.ReadUInt32();
            uint levelPoint = reader.ReadUInt32();
            ushort hit1 = reader.ReadUInt16();
            ushort hit2 = reader.ReadUInt16();
            ushort hit3 = reader.ReadUInt16();
            ushort hit4 = reader.ReadUInt16();
            ushort chit = reader.ReadUInt16();
            ushort hp = reader.ReadUInt16();
            ushort ap = reader.ReadUInt16();
            ushort attackSpeed = reader.ReadUInt16();
            ushort speed = reader.ReadUInt16();
            ushort maxCp = reader.ReadUInt16();
            uint win = reader.ReadUInt32();
            uint lose = reader.ReadUInt32();
            uint draw = reader.ReadUInt32();

            var summary = new CharacterSummary(
                id, name, slot, (CharacterClass)classId, level, exp, levelPoint,
                hit1, hit2, hit3, hit4, chit, hp, ap, attackSpeed, speed, maxCp, win, lose, draw);

            packet = new CharacterCreateResponsePacket(result, summary);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

