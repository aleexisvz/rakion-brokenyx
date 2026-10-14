using System.Data.Common;

namespace Rakion.Database.Connection;

/// <summary>
/// Factory for providing open database connections.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates and opens a new database connection asynchronously.
    /// </summary>
    Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

