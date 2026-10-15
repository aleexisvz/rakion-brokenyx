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
    byte UnlockedSlots = 4,
    IReadOnlyList<CharacterSummary>? Characters = null,
    string Username = "",
    ushort ResponseOpcode = (ushort)RakionOpcode.WorldLogin) : IPacket
{
    public ushort Opcode => ResponseOpcode;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ResultCode);

        if (ResultCode != 0)
        {
            return;
        }

        // Retail v258 client layout:
        // [1..4]   UserId (uint)
        // [5..6]   Country (ushort = 0)
        // [7..10]  Gold (uint)
        // [11..14] Cash (uint)
        // [15..18] RankGrade (uint = 0)
        // [19..]   Username (null-terminated string)
        writer.WriteUInt32(UserId);
        writer.WriteUInt16(0); // country
        writer.WriteUInt32(Gold);
        writer.WriteUInt32(Cash);
        writer.WriteUInt32(0); // rankgrade
        writer.WriteNullTerminatedString(Username ?? string.Empty);

        // Fixed auxiliary blocks matching RakionWorldServ.exe 0x411a59..0x411c50
        writer.WriteUInt32(0);
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteUInt32(0);
        writer.WriteUInt16(0);
        writer.WriteUInt16(0);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteByte(0);
        writer.WriteByte(0); // Buddy count
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteByte(UnlockedSlots);

        var list = Characters ?? [];
        writer.WriteByte((byte)list.Count);

        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            writer.WriteUInt32(c.Id);
            writer.WriteNullTerminatedString(c.Name);
            writer.WriteUInt32(c.Exp);
            writer.WriteUInt32(c.Win);
            writer.WriteUInt32(c.Lose);
            writer.WriteByte((byte)c.Class);
            writer.WriteUInt32(c.LevelPoint);
            writer.WriteUInt32(c.Draw);
            writer.WriteByte(c.Level);
            writer.WriteByte(c.Slot);
            writer.WriteByte(0); // Flag
            writer.WriteUInt32(0); // Rank
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
            writer.WriteUInt16(0);
            writer.WriteZeroBytes(0x26);
            writer.WriteZeroBytes(0x13);
            writer.WriteZeroBytes(0x4c);
            writer.WriteZeroBytes(0x4c);
            writer.WriteZeroBytes(0x4c);
            writer.WriteZeroBytes(0x64);
            writer.WriteZeroBytes(0x190);
            writer.WriteByte(0);
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out WorldLoginResponsePacket packet)
    {
        if (payload.Length < 1)
        {
            packet = default!;
            return false;
        }

        byte result = payload[0];
        if (result != 0)
        {
            packet = new WorldLoginResponsePacket(result);
            return true;
        }

        try
        {
            // Retail layout parsing
            if (payload.Length >= 22)
            {
                var r = new PacketReader(payload.Slice(1));
                uint userId = r.ReadUInt32();
                ushort country = r.ReadUInt16();
                uint gold = r.ReadUInt32();
                uint cash = r.ReadUInt32();
                uint rankGrade = r.ReadUInt32();
                string username = r.ReadNullTerminatedString();

                r.ReadUInt32();
                r.ReadUInt16();
                r.ReadUInt32();
                r.ReadUInt32();
                r.ReadUInt32();
                r.ReadNullTerminatedString();
                r.ReadNullTerminatedString();
                r.ReadUInt32();
                r.ReadUInt16();
                r.ReadUInt16();
                r.ReadNullTerminatedString();
                r.ReadNullTerminatedString();
                r.ReadByte();
                byte buddyCount = r.ReadByte();
                for (int b = 0; b < buddyCount; b++)
                {
                    r.ReadNullTerminatedString();
                    r.ReadNullTerminatedString();
                }
                r.ReadUInt16();
                r.ReadUInt32();
                r.ReadUInt32();
                byte slots = r.ReadByte();
                byte charCount = r.ReadByte();

                var chars = new List<CharacterSummary>(charCount);
                for (int i = 0; i < charCount; i++)
                {
                    uint id = r.ReadUInt32();
                    string name = r.ReadNullTerminatedString();
                    uint exp = r.ReadUInt32();
                    uint win = r.ReadUInt32();
                    uint lose = r.ReadUInt32();
                    byte classId = r.ReadByte();
                    uint levelPoint = r.ReadUInt32();
                    uint draw = r.ReadUInt32();
                    byte level = r.ReadByte();
                    byte slot = r.ReadByte();
                    byte flag = r.ReadByte();
                    uint rank = r.ReadUInt32();
                    ushort hit1 = r.ReadUInt16();
                    ushort hit2 = r.ReadUInt16();
                    ushort hit3 = r.ReadUInt16();
                    ushort hit4 = r.ReadUInt16();
                    ushort chit = r.ReadUInt16();
                    ushort hp = r.ReadUInt16();
                    ushort ap = r.ReadUInt16();
                    ushort attackSpeed = r.ReadUInt16();
                    ushort speed = r.ReadUInt16();
                    ushort maxCp = r.ReadUInt16();
                    r.ReadUInt16();
                    r.Skip(0x26 + 0x13 + 0x4c * 3 + 0x64 + 0x190 + 1);

                    chars.Add(new CharacterSummary(
                        id, name, slot, (CharacterClass)classId, level, exp, levelPoint,
                        hit1, hit2, hit3, hit4, chit, hp, ap, attackSpeed, speed, maxCp,
                        win, lose, draw));
                }

                packet = new WorldLoginResponsePacket(result, userId, gold, cash, slots, chars, username);
                return true;
            }
        }
        catch
        {
            // Fall through to legacy parsing
        }

        // Fallback for legacy test formats
        try
        {
            var reader = new PacketReader(payload);
            byte res = reader.ReadByte();
            if (res != 0)
            {
                packet = new WorldLoginResponsePacket(res);
                return true;
            }

            uint uId = reader.ReadUInt32();
            uint g = reader.ReadUInt32();
            uint c = reader.ReadUInt32();
            byte s = reader.ReadByte();
            byte count = reader.ReadByte();

            var list = new List<CharacterSummary>(count);
            for (int i = 0; i < count; i++)
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

                list.Add(new CharacterSummary(
                    id, name, slot, (CharacterClass)classId, level, exp, levelPoint,
                    hit1, hit2, hit3, hit4, chit, hp, ap, attackSpeed, speed, maxCp,
                    win, lose, draw));
            }

            packet = new WorldLoginResponsePacket(res, uId, g, c, s, list);
            return true;
        }
        catch (PacketParseException)
        {
            packet = default!;
            return false;
        }
    }
}
