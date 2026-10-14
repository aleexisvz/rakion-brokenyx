using Rakion.Core.Enums;
using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Data repository for player characters.
/// </summary>
public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterEntity>> GetCharactersByUserIdAsync(int userId, CancellationToken ct = default);
    Task<CharacterEntity?> GetCharacterByIdAsync(int characterId, CancellationToken ct = default);
    Task<CharacterEntity?> GetCharacterByNameAsync(string name, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);
    Task<CharacterEntity> CreateCharacterAsync(int userId, string name, CharacterClass characterClass, byte slotIndex, CancellationToken ct = default);
    Task<bool> DeleteCharacterAsync(int characterId, string deleteKey, CancellationToken ct = default);
}

