using Rakion.Core.Enums;
using Rakion.Core.Protocol;

namespace Rakion.Core.Packets;

/// <summary>
/// World server login part 2 packet completing client handshake and enabling Character Select UI (Opcode 0x000D).
/// Evidence: Observed in engine.dll jump table opcode 13 (0x000D) calling 0x36196690 and Rakion.bin 0x0047DBFA.
/// Wire layout (when UnlockedSlots &lt;= 4):
///   [0]     byte WordCount (0)
///   [1]     byte Flag (0)
///   [2..]   byte[240] (120 words)
///   [242..] byte[120] (120 bytes)
///   [362..] byte[480] (120 dwords)
///   [842..] byte[480] (120 dwords)
///   [1322..]uint FinalDword (4 bytes)
/// Total payload size: 1326 bytes.
/// If UnlockedSlots &gt; 4, character records for slots 4..(UnlockedSlots-1) are serialized before the tail block.
/// The tail block is the account item box, modelled by <see cref="AccountBoxBlock"/> (lane A, C-3).
/// </summary>
public sealed record WorldLoginPart2Packet(
    byte UnlockedSlots = 3,
    IReadOnlyList<CharacterSummary>? Characters = null,
    AccountBoxBlock? AccountBox = null) : IPacket
{
    public const int BasePayloadSize = 1326;

    public ushort Opcode => (ushort)ServerOpcode.WorldLoginPart2;

    public void Serialize(PacketWriter writer)
    {
        if (UnlockedSlots > 4)
        {
            var list = Characters ?? [];
            for (int slot = 4; slot < UnlockedSlots; slot++)
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
                    writer.WriteUInt32(0); // Empty slot marker
                    continue;
                }

                // Observed in engine.dll 0x36196720..0x36196B3B and RakionWorldServ.exe 0x00426365..0x00426AC0:
                writer.WriteUInt32(c.Id);                 // [CharacterRecord + 0x00] Id
                writer.WriteNullTerminatedString(c.Name); // [CharacterRecord + 0x10] Name
                writer.WriteUInt32(c.Win);                // [CharacterRecord + 0x04] Win
                writer.WriteUInt32(c.Lose);               // [CharacterRecord + 0x08] Lose
                writer.WriteUInt32(c.Draw);               // [CharacterRecord + 0x0C] Draw
                writer.WriteByte(0);                      // [CharacterRecord + 0xB0] RankGrade
                writer.WriteUInt32(0);                    // [CharacterRecord + 0xB4] TotalRank
                writer.WriteUInt32(0);                    // [CharacterRecord + 0xB8] ClassRank
                writer.WriteByte(0);                      // [CharacterRecord + 0x24] Auth
                writer.WriteByte((byte)c.Class);          // [CharacterRecord + 0x2C] Class
                writer.WriteByte(c.Level);                // [CharacterRecord + 0x2D] Level
                writer.WriteUInt32(c.Exp);                // [CharacterRecord + 0x30] Exp
                writer.WriteUInt16((ushort)c.LevelPoint); // [CharacterRecord + 0x34] LevelPoint (ushort)
                writer.WriteUInt16(c.Hit1);               // [CharacterRecord + 0x3C] Hit1
                writer.WriteUInt16(c.Hit2);               // [CharacterRecord + 0x48] Hit2
                writer.WriteUInt16(c.Hit3);               // [CharacterRecord + 0x54] Hit3
                writer.WriteUInt16(c.Hit4);               // [CharacterRecord + 0x60] Hit4
                writer.WriteUInt16(c.Chit);               // [CharacterRecord + 0x6C] Chit
                writer.WriteUInt16(c.Hp);                 // [CharacterRecord + 0x78] Hp
                writer.WriteUInt16(c.Ap);                 // [CharacterRecord + 0x84] Ap
                writer.WriteUInt16(c.AttackSpeed);        // [CharacterRecord + 0x90] AttackSpeed
                writer.WriteUInt16(c.Speed);              // [CharacterRecord + 0x9C] Speed
                writer.WriteUInt16(c.MaxCp);              // [CharacterRecord + 0xA8] MaxCp
                CharacterEquipment.Serialize(writer, c.Equipment); // 19 equipment slots
                WorldLoginResponsePacket.WriteStageRanks(writer, c.StageRanks); // 100 * byte StageRanks
                writer.WriteByte(3);                      // [CharacterRecord + 0x41C] PotionSlotCount
            }
        }

        // Tail block for retail engine.dll 0x36196B41 (1326 bytes when no expired items are listed).
        (AccountBox ?? AccountBoxBlock.Empty).Serialize(writer);
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out WorldLoginPart2Packet packet)
    {
        if (payload.Length < BasePayloadSize)
        {
            packet = default!;
            return false;
        }

        packet = new WorldLoginPart2Packet();
        return true;
    }
}


