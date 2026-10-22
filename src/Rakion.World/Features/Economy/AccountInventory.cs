using Rakion.Core.Packets;

namespace Rakion.World.Features.Economy;

/// <summary>
/// What World sends about an account's items at login: the shared box and the equipment of each character.
/// </summary>
public sealed record AccountInventory(AccountBoxBlock Box, IReadOnlyDictionary<int, ItemSlot[]> EquipmentByCharacter)
{
    public IReadOnlyList<ItemSlot>? EquipmentOf(int characterId) =>
        EquipmentByCharacter.TryGetValue(characterId, out var slots) ? slots : null;
}
