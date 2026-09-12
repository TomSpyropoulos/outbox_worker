using Loans.Api.Models;

namespace Loans.Api.Services;

public interface ILoanService
{
    /// <summary>
    /// Saves a new Submitted loan together with its LoanApplicationSubmitted event.
    /// Throws LoanValidationException when the input breaks a business rule.
    /// </summary>
    Task<LoanApplication> SubmitAsync(string borrowerName, decimal amount, string currency, CancellationToken cancellationToken);

    Task<LoanApplication?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ApplyDecisionResult> ApplyDecisionAsync(Guid loanId, LoanDecision decision, CancellationToken cancellationToken);
}
