namespace Rakion.Database.Entities;

/// <summary>
/// One row of the creature level table: a creature item (id 8000 + <see cref="Npc"/>) leaves <see cref="Level"/> once
/// its experience exceeds <see cref="Exp"/> (legacy <c>NPCInfo</c>, loaded at RakionWorldServ.exe 0x40FF58 and used
/// at 0x40B940, Observed). The dump's values match the client's DataSetup.xfs for the first creature (Derived).
/// </summary>
public sealed record CreatureLevelEntity(byte Npc, byte Level, int Exp);
