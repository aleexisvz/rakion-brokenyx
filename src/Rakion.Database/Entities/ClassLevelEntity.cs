namespace Rakion.Database.Entities;

/// <summary>
/// One row of the level table: a character of <see cref="ClassId"/> leaves <see cref="Level"/> once its total
/// experience exceeds <see cref="Exp"/> (legacy <c>ClassLevelInfo</c>, used by RakionWorldServ.exe 0x40D300, Observed).
/// </summary>
public sealed record ClassLevelEntity(byte ClassId, byte Level, int Exp);
