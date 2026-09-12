using Loans.Api.Data;
using Loans.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Loans.Api.Repositories;

public class LoanRepository : ILoanRepository
{
    private readonly LoansDbContext _db;

    public LoanRepository(LoansDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(LoanApplication loan, OutboxMessage outboxMessage, CancellationToken cancellationToken)
    {
        _db.LoanApplications.Add(loan);
        _db.OutboxMessages.Add(outboxMessage);

        // SaveChanges wraps all its inserts in one transaction, so both rows are saved or neither
        // is. This is the whole point of the outbox: the event can't be lost or sent on its own.
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoanApplication?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // AsNoTracking: we only read the loan, so EF doesn't need to watch it for changes.
        return await _db.LoanApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<ApplyDecisionResult> ApplyDecisionIfSubmittedAsync(
        Guid loanId,
        LoanDecision decision,
        CancellationToken cancellationToken)
    {
        LoanStatus newStatus;
        if (decision == LoanDecision.Approved)
        {
            newStatus = LoanStatus.Approved;
        }
        else if (decision == LoanDecision.Rejected)
        {
            newStatus = LoanStatus.Rejected;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown decision.");
        }

        // Runs one statement, without loading the loan first:
        //   UPDATE LoanApplications SET Status = @newStatus WHERE Id = @loanId AND Status = 'Submitted'
        // A single UPDATE is atomic. If two duplicates race, SQL Server makes the second wait for
        // the first one's row lock. Then the second sees the loan is no longer Submitted and
        // changes nothing.
        int updatedRows = await _db.LoanApplications
            .Where(l => l.Id == loanId && l.Status == LoanStatus.Submitted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.Status, newStatus), cancellationToken);

        if (updatedRows == 1)
        {
            return ApplyDecisionResult.Applied;
        }

        // Nothing changed. Find out why. Loans are never deleted, so the answer can't go stale
        // between the two statements.
        bool loanExists = await _db.LoanApplications.AnyAsync(l => l.Id == loanId, cancellationToken);
        if (loanExists)
        {
            return ApplyDecisionResult.AlreadyDecided;
        }

        return ApplyDecisionResult.LoanNotFound;
    }
}
