using Loans.Api.Messages;
using Loans.Api.Models;

namespace Loans.Api.Tests.Unit;

public class LoanDecisionMadeTests
{
    [Fact]
    public void FromPayload_reads_a_valid_decision()
    {
        var loanId = Guid.NewGuid();
        string payload = $$"""{"loanId":"{{loanId}}","decision":"Rejected","reason":"Too high."}""";

        LoanDecisionMade? message = LoanDecisionMade.FromPayload(payload);

        Assert.NotNull(message);
        Assert.Equal(loanId, message.LoanId);
        Assert.Equal(LoanDecision.Rejected, message.Decision);
        Assert.Equal("Too high.", message.Reason);
    }

    [Theory]
    [InlineData("this is not JSON")]
    [InlineData("")]
    [InlineData("""{"loanId":"00000000-0000-0000-0000-000000000001","decision":1}""")] // Numbers are refused.
    [InlineData("""{"loanId":"00000000-0000-0000-0000-000000000001","decision":"Maybe"}""")]
    public void FromPayload_returns_null_for_an_invalid_payload(string payload)
    {
        Assert.Null(LoanDecisionMade.FromPayload(payload));
    }
}
