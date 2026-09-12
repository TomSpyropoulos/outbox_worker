using System.Globalization;
using CreditCheck.Worker.Messages;

namespace CreditCheck.Worker.Tests;

public class CreditPolicyTests
{
    [Theory]
    [InlineData("1000", LoanDecision.Approved)]
    [InlineData("50000", LoanDecision.Approved)]
    [InlineData("50000.01", LoanDecision.Rejected)]
    public void Approves_up_to_the_limit_and_rejects_above_it(string amount, LoanDecision expected)
    {
        var loan = new LoanApplicationSubmitted
        {
            LoanId = Guid.NewGuid(),
            Amount = decimal.Parse(amount, CultureInfo.InvariantCulture),
            Currency = "USD",
        };

        CreditDecision decision = CreditPolicy.Decide(loan, 50_000m);

        Assert.Equal(expected, decision.Decision);
        Assert.Contains("50000", decision.Reason);
    }
}
