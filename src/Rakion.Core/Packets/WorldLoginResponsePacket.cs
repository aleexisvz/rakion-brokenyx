using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// Lightweight character summary sent inside WorldLoginResponsePacket.
/// </summary>
public sealed record CharacterSummary(
    uint Id,
    string Name,
    byte Slot,
    CharacterClass Class,
    byte Level,
    uint Exp,
    uint LevelPoint,
    ushort Hit1 = 0,
    ushort Hit2 = 0,
    ushort Hit3 = 0,
    ushort Hit4 = 0,
    ushort Chit = 0,
    ushort Hp = 100,
    ushort Ap = 100,
    ushort AttackSpeed = 100,
    ushort Speed = 100,
    ushort MaxCp = 100,
    uint Win = 0,
    uint Lose = 0,
    uint Draw = 0);

/// <summary>
/// World server login response and character list packet (Opcode 0x0002).
/// Evidence: Observed in RakionWorldServ DBCommandCharacterList and rakion.bin OnRecvLogin.
/// </summary>
public sealed record WorldLoginResponsePacket(
    byte ResultCode,
    uint UserId = 0,
    uint Gold = 0,
    uint Cash = 0,
    byte UnlockedSlots = 3,
    IReadOnlyList<CharacterSummary>? Characters = null) : IPacket
{
    public ushort Opcode => (ushort)RakionOpcode.WorldLogin;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);

        if (ResultCode != 0)
        {
            return;
        }

        writer.WriteUInt32(UserId);
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteByte(UnlockedSlots);

        var list = Characters ?? [];
        writer.WriteByte((byte)list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            writer.WriteByte(c.Slot);
            writer.WriteUInt32(c.Id);
            writer.WriteNullTerminatedString(c.Name);
            writer.WriteByte((byte)c.Class);
            writer.WriteByte(c.Level);
            writer.WriteUInt32(c.Exp);
            writer.WriteUInt32(c.LevelPoint);
            writer.WriteUInt16(c.Hit1);
            writer.WriteUInt16(c.Hit2);
            writer.WriteUInt16(c.Hit3);
            writer.WriteUInt16(c.Hit4);
            writer.WriteUInt16(c.Chit);
            writer.WriteUInt16(c.Hp);
            writer.WriteUInt16(c.Ap);
            writer.WriteUInt16(c.AttackSpeed);
            writer.WriteUInt16(c.Speed);
            writer.WriteUInt16(c.MaxCp);
            writer.WriteUInt32(c.Win);
            writer.WriteUInt32(c.Lose);
            writer.WriteUInt32(c.Draw);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out WorldLoginResponsePacket packet)
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

            if (result != 0)
            {
                packet = new WorldLoginResponsePacket(result);
                return true;
            }

            uint userId = reader.ReadUInt32();
            uint gold = reader.ReadUInt32();
            uint cash = reader.ReadUInt32();
            byte slots = reader.ReadByte();
            byte charCount = reader.ReadByte();

            var chars = new List<CharacterSummary>(charCount);
            for (int i = 0; i < charCount; i++)
            {
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

                chars.Add(new CharacterSummary(
                    id,
                    name,
                    slot,
                    (CharacterClass)classId,
                    level,
                    exp,
                    levelPoint,
                    hit1, hit2, hit3, hit4, chit, hp, ap, attackSpeed, speed, maxCp,
                    win, lose, draw));
            }

            packet = new WorldLoginResponsePacket(result, userId, gold, cash, slots, chars);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}

