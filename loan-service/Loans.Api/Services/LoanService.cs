using Loans.Api.Messages;
using Loans.Api.Models;
using Loans.Api.Repositories;

namespace Loans.Api.Services;

public class LoanService : ILoanService
{
    private readonly ILoanRepository _loanRepository;

    public LoanService(ILoanRepository loanRepository)
    {
        _loanRepository = loanRepository;
    }

    public async Task<LoanApplication> SubmitAsync(
        string borrowerName,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        // The request's attributes already checked the shape: required fields, amount range,
        // currency format. This is the rule no attribute can express. The column is
        // decimal(18,2), and rejecting extra digits beats SQL Server silently rounding them away.
        if (decimal.Round(amount, 2) != amount)
        {
            throw new LoanValidationException("Amount", "Amount can have at most two decimal places.");
        }

        var loan = new LoanApplication
        {
            Id = Guid.NewGuid(),
            BorrowerName = borrowerName.Trim(),
            Amount = amount,
            Currency = currency,
            Status = LoanStatus.Submitted,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var submittedEvent = new LoanApplicationSubmitted
        {
            LoanId = loan.Id,
            BorrowerName = loan.BorrowerName,
            Amount = loan.Amount,
            Currency = loan.Currency,
            OccurredOnUtc = loan.CreatedAtUtc,
        };

        // Nothing is published here. The event goes into the outbox, and OutboxPublisher
        // publishes it later. The type name and the payload are what credit-check reads.
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = submittedEvent.OccurredOnUtc,
            Type = LoanApplicationSubmitted.TypeName,
            Payload = submittedEvent.ToPayload(),
        };

        // One call with both objects, so the repository saves them in one transaction. Saving
        // them in two calls could leave a loan without its event.
        await _loanRepository.AddAsync(loan, outboxMessage, cancellationToken);
        return loan;
    }

    public async Task<LoanApplication?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _loanRepository.GetAsync(id, cancellationToken);
    }

    public async Task<ApplyDecisionResult> ApplyDecisionAsync(
        Guid loanId,
        LoanDecision decision,
        CancellationToken cancellationToken)
    {
        // The check and the update have to happen in one atomic step, so both live in the
        // repository method.
        return await _loanRepository.ApplyDecisionIfSubmittedAsync(loanId, decision, cancellationToken);
    }
}
