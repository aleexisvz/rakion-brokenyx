namespace Rakion.World.Features.Economy;

/// <summary>What one reinforce attempt uses: the item level, the stone and how many of each catalyst.</summary>
public readonly record struct EnchantAttempt(int Level, int StoneId, int DestroyGuards, int DownGuards, int Charms);
