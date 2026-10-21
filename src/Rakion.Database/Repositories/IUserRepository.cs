using Rakion.Database.Entities;

namespace Rakion.Database.Repositories;

/// <summary>
/// Data repository for user accounts and game configuration.
/// </summary>
public interface IUserRepository
{
    Task<UserEntity?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<UserEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CreateUserAsync(UserEntity user, long initialGold = 10_000, int initialCash = 0, CancellationToken ct = default);
    Task UpdatePasswordHashAsync(int userId, string newHash, CancellationToken ct = default);
    Task<UserGameInfoEntity?> GetGameInfoByUserIdAsync(int userId, CancellationToken ct = default);
    Task UpdateLastConnectAsync(int userId, DateTime connectTime, CancellationToken ct = default);
    Task SetTutorialCompletedAsync(int userId, bool completed = true, CancellationToken ct = default);
}

