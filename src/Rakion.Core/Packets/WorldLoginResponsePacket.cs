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
    uint Draw = 0,
    IReadOnlyList<ItemSlot>? Equipment = null);

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
    IReadOnlyList<CharacterSummary>? Characters = null,
    string Username = "",
    bool TutorialCompleted = false,
    ushort ResponseOpcode = (ushort)ServerOpcode.WorldLogin) : IPacket
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
        writer.WriteUInt16(0); // Country (engine.dll 0x36195F6C)
        writer.WriteUInt32(Gold); // [7..10] Gold (engine.dll 0x36195F1F -> [esp + 0x30])
        writer.WriteUInt32(Cash); // [11..14] Cash (engine.dll 0x36195F30 -> [esp + 0x4C])
        writer.WriteUInt32(0); // [15..18] RankGrade (engine.dll 0x36195F43)
        writer.WriteNullTerminatedString(Username ?? string.Empty);

        // Fixed auxiliary blocks matching RakionWorldServ.exe 0x00426DD5..0x00427052 and engine.dll 0x36195FB8..0x3619610C
        writer.WriteUInt32(0);
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteUInt32(0);
        writer.WriteUInt16(0);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteNullTerminatedString(string.Empty);
        writer.WriteByte(0);
        writer.WriteByte(0); // Buddy count (engine.dll 0x3619608D)
        writer.WriteUInt16(0); // Post-buddy uint16 (engine.dll 0x361960D0)
        writer.WriteUInt32(Gold); // Gold (engine.dll 0x361960DA -> [AccountInfo + 0x64], RakionWorldServ 0x00427008)
        writer.WriteUInt32(Cash); // Cash (engine.dll 0x361960E8 -> [AccountInfo + 0x68], RakionWorldServ 0x00427002)
        writer.WriteByte(UnlockedSlots); // UnlockedSlots (engine.dll 0x361960F5 -> [AccountInfo + 0x6C], RakionWorldServ 0x00427032)
        // Tutorial flag: engine.dll 0x36196100 reads it, rakion.bin 0x47E47D stores it at listener+0x7C, and
        // rakion.bin 0x468C4B blocks character entry with a dialog while it is 0. Offsets Observed; meaning
        // Derived (the legacy login query reads gold, tutorial, slot, bag from UserGameInfo).
        writer.WriteByte(TutorialCompleted ? (byte)1 : (byte)0);

        var list = Characters ?? [];
        int totalSlots = Math.Min((int)UnlockedSlots, 4);

        for (int slot = 0; slot < totalSlots; slot++)
        {
            CharacterSummary? c = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Slot == slot)
                {
                    c = list[i];
                    break;
                }
            }

            if (c is null || c.Id == 0)
            {
                writer.WriteUInt32(0); // Empty slot marker for client loop (engine.dll 0x36196143)
                continue;
            }

            // Observed in engine.dll 0x36196143..0x3619655E and RakionWorldServ.exe 0x00427090..0x00427518:
            writer.WriteUInt32(c.Id);                 // [CharacterRecord + 0x00] Id
            writer.WriteNullTerminatedString(c.Name); // [CharacterRecord + 0x10] Name
            writer.WriteUInt32(c.Win);                // [CharacterRecord + 0x04] Win (engine.dll 0x3619617B)
            writer.WriteUInt32(c.Lose);               // [CharacterRecord + 0x08] Lose (engine.dll 0x3619618B)
            writer.WriteUInt32(c.Draw);               // [CharacterRecord + 0x0C] Draw (engine.dll 0x3619619B)
            writer.WriteByte(0);                      // [CharacterRecord + 0xB0] RankGrade (engine.dll 0x361961AB)
            writer.WriteUInt32(0);                    // [CharacterRecord + 0xB4] TotalRank (engine.dll 0x361961B9)
            writer.WriteUInt32(0);                    // [CharacterRecord + 0xB8] ClassRank (engine.dll 0x361961C9)
            writer.WriteByte(0);                      // [CharacterRecord + 0x24] Auth (engine.dll 0x361961D9)
            writer.WriteByte((byte)c.Class);          // [CharacterRecord + 0x2C] Class (engine.dll 0x361961FF, RakionWorldServ 0x004271E5)
            writer.WriteByte(c.Level);                // [CharacterRecord + 0x2D] Level (engine.dll 0x3619620D, RakionWorldServ 0x004271ED)
            writer.WriteUInt32(c.Exp);                // [CharacterRecord + 0x30] Exp (engine.dll 0x3619621B, RakionWorldServ 0x004271FF)
            writer.WriteUInt16((ushort)c.LevelPoint); // [CharacterRecord + 0x34] LevelPoint (ushort, engine.dll 0x3619622B, RakionWorldServ 0x0042730E)
            writer.WriteUInt16(c.Hit1);               // [CharacterRecord + 0x3C] Hit1 (engine.dll 0x3619623D)
            writer.WriteUInt16(c.Hit2);               // [CharacterRecord + 0x48] Hit2 (engine.dll 0x36196271)
            writer.WriteUInt16(c.Hit3);               // [CharacterRecord + 0x54] Hit3 (engine.dll 0x361962A5)
            writer.WriteUInt16(c.Hit4);               // [CharacterRecord + 0x60] Hit4 (engine.dll 0x361962D9)
            writer.WriteUInt16(c.Chit);               // [CharacterRecord + 0x6C] Chit (engine.dll 0x3619630D)
            writer.WriteUInt16(c.Hp);                 // [CharacterRecord + 0x78] Hp (engine.dll 0x36196341)
            writer.WriteUInt16(c.Ap);                 // [CharacterRecord + 0x84] Ap (engine.dll 0x36196375)
            writer.WriteUInt16(c.AttackSpeed);        // [CharacterRecord + 0x90] AttackSpeed (engine.dll 0x361963A9)
            writer.WriteUInt16(c.Speed);              // [CharacterRecord + 0x9C] Speed (engine.dll 0x361963DD)
            writer.WriteUInt16(c.MaxCp);              // [CharacterRecord + 0xA8] MaxCp (engine.dll 0x36196411)
            CharacterEquipment.Serialize(writer, c.Equipment); // 19 equipment slots (engine.dll 0x36196450..0x361964F0)
            writer.WriteZeroBytes(0x64);              // 100 * byte StageRanks (engine.dll 0x36196530)
            writer.WriteByte(3);                      // [CharacterRecord + 0x41C] PotionSlotCount (engine.dll 0x36196547, RakionWorldServ 0x00427514)
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
                r.ReadUInt32(); // postBuddyGold
                r.ReadUInt32(); // postBuddyCash
                byte slots = r.ReadByte();
                byte extraFlag = r.ReadByte();
                int totalSlots = Math.Min((int)slots, 4);

                var chars = new List<CharacterSummary>(totalSlots);
                for (int slot = 0; slot < totalSlots; slot++)
                {
                    uint id = r.ReadUInt32();
                    if (id == 0)
                    {
                        continue;
                    }

                    string name = r.ReadNullTerminatedString();
                    uint win = r.ReadUInt32();
                    uint lose = r.ReadUInt32();
                    uint draw = r.ReadUInt32();
                    byte charRankGrade = r.ReadByte();
                    uint totalRank = r.ReadUInt32();
                    uint classRank = r.ReadUInt32();
                    byte auth = r.ReadByte();
                    byte classId = r.ReadByte();
                    byte level = r.ReadByte();
                    uint exp = r.ReadUInt32();
                    uint levelPoint = r.ReadUInt16();
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
                    r.Skip(0x26 + 0x13 + 0x4c * 2 + 0x64 + 1);

                    chars.Add(new CharacterSummary(
                        id, name, (byte)slot, (CharacterClass)classId, level, exp, levelPoint,
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
