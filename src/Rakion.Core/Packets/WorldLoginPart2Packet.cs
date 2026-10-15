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
/// </summary>
public sealed record WorldLoginPart2Packet(
    byte UnlockedSlots = 4,
    IReadOnlyList<CharacterSummary>? Characters = null) : IPacket
{
    public const int BasePayloadSize = 1326;

    public ushort Opcode => (ushort)RakionOpcode.WorldLoginPart2;

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

        // Tail block (1326 bytes) for retail engine.dll 0x36196B41:
        writer.WriteByte(0);        // WordCount (0 words)
        writer.WriteByte(0);        // Flag (0)
        writer.WriteZeroBytes(240); // 120 words
        writer.WriteZeroBytes(120); // 120 bytes
        writer.WriteZeroBytes(480); // 120 dwords
        writer.WriteZeroBytes(480); // 120 dwords
        writer.WriteUInt32(0);      // Final dword
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


