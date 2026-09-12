using CreditCheck.Worker.Messages;

namespace CreditCheck.Worker;

public class CreditDecision
{
    public LoanDecision Decision { get; set; }

    public string Reason { get; set; } = string.Empty;
}
