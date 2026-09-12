using Loans.Api.Models;
using Microsoft.EntityFrameworkCore.Storage;

namespace Loans.Api.Repositories;

public interface IOutboxRepository
{
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locks and returns the oldest pending message that no one else has locked, or null when
    /// there is none. Must run inside a transaction, which holds the lock until it ends.
    /// </summary>
    Task<OutboxMessage?> GetNextForUpdateAsync(int maxAttempts, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
