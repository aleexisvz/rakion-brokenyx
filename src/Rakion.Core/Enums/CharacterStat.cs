namespace Rakion.Core.Enums;

/// <summary>
/// Stat index used by stat point allocation, in character record order.
/// Evidence: the legacy server switches over 0..9 (RakionWorldServ.exe 0x40B41F) and rakion.bin 0x47DBE8 maps
/// each index to consecutive stat fields (Observed); names follow the record fields Hit1..MaxCp (Derived).
/// </summary>
public enum CharacterStat : byte
{
    Hit1 = 0,
    Hit2 = 1,
    Hit3 = 2,
    Hit4 = 3,
    Chit = 4,
    Hp = 5,
    Ap = 6,
    AttackSpeed = 7,
    Speed = 8,
    MaxCp = 9,
}
