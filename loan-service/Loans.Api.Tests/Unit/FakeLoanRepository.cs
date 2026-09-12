using Loans.Api.Models;
using Loans.Api.Repositories;

namespace Loans.Api.Tests.Unit;

/// <summary>An in-memory ILoanRepository, so LoanService can be tested without a database.</summary>
public class FakeLoanRepository : ILoanRepository
{
    public List<LoanApplication> AddedLoans { get; } = new List<LoanApplication>();

    public List<OutboxMessage> AddedOutboxMessages { get; } = new List<OutboxMessage>();

    /// <summary>What ApplyDecisionIfSubmittedAsync returns.</summary>
    public ApplyDecisionResult DecisionResult { get; set; } = ApplyDecisionResult.Applied;

    public Task AddAsync(LoanApplication loan, OutboxMessage outboxMessage, CancellationToken cancellationToken)
    {
        AddedLoans.Add(loan);
        AddedOutboxMessages.Add(outboxMessage);
        return Task.CompletedTask;
    }

    public Task<LoanApplication?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        LoanApplication? loan = AddedLoans.FirstOrDefault(l => l.Id == id);
        return Task.FromResult(loan);
    }

    public Task<ApplyDecisionResult> ApplyDecisionIfSubmittedAsync(Guid loanId, LoanDecision decision, CancellationToken cancellationToken)
    {
        return Task.FromResult(DecisionResult);
    }
}
