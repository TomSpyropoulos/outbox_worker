using System.Globalization;
using CreditCheck.Worker.Messages;

namespace CreditCheck.Worker;

/// <summary>
/// All of this service's business logic. It's a stand-in for a real credit check: it approves
/// anything up to a limit and ignores the currency.
/// </summary>
public static class CreditPolicy
{
    public static CreditDecision Decide(LoanApplicationSubmitted loan, decimal maxApprovedAmount)
    {
        // Invariant culture, so the reason reads the same on every machine ("50000.5", not "50000,5").
        string limit = maxApprovedAmount.ToString(CultureInfo.InvariantCulture);

        if (loan.Amount <= maxApprovedAmount)
        {
            return new CreditDecision
            {
                Decision = LoanDecision.Approved,
                Reason = $"Amount is within the {limit} limit.",
            };
        }

        return new CreditDecision
        {
            Decision = LoanDecision.Rejected,
            Reason = $"Amount exceeds the {limit} limit.",
        };
    }
}
