using Loans.Api.Data;
using Loans.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Loans.Api.Repositories;

public class OutboxRepository : IOutboxRepository
{
    private readonly LoansDbContext _db;

    public OutboxRepository(LoansDbContext db)
    {
        _db = db;
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return await _db.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<OutboxMessage?> GetNextForUpdateAsync(int maxAttempts, CancellationToken cancellationToken)
    {
        // Without a transaction, SQL Server releases the lock as soon as the SELECT finishes, and
        // two workers could publish the same row.
        if (_db.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("GetNextForUpdateAsync must run inside a transaction.");
        }

        // UPDLOCK keeps other workers off this row until we commit. READPAST makes them skip it
        // instead of waiting, so each worker takes the next free row.
        //
        // FromSql turns {maxAttempts} into a SQL parameter, so this is not string concatenation.
        // ToListAsync runs the SQL exactly as written. Adding more LINQ, like FirstOrDefaultAsync,
        // would make EF wrap it in a subquery.
        List<OutboxMessage> rows = await _db.OutboxMessages
            .FromSql($"""
                SELECT TOP (1) * FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ProcessedAtUtc IS NULL AND AttemptCount < {maxAttempts}
                ORDER BY OccurredOnUtc
                """)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        return rows[0];
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}
