using Loans.Api.Models;

namespace Loans.Api.Repositories;

public interface ILoanRepository
{
    /// <summary>Saves the loan and its outbox message together, in one transaction.</summary>
    Task AddAsync(LoanApplication loan, OutboxMessage outboxMessage, CancellationToken cancellationToken);

    Task<LoanApplication?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the loan's status from the decision, but only while the loan is still Submitted.
    /// Safe to call twice, and safe to call concurrently: only one call can change the status.
    /// </summary>
    Task<ApplyDecisionResult> ApplyDecisionIfSubmittedAsync(Guid loanId, LoanDecision decision, CancellationToken cancellationToken);
}
