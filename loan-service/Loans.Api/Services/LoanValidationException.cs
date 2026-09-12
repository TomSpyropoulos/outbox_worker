namespace Loans.Api.Services;

/// <summary>Input broke a business rule. The controller turns this into a 400 response.</summary>
public class LoanValidationException : Exception
{
    public LoanValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    /// <summary>The request field at fault, e.g. "Amount".</summary>
    public string Field { get; }
}
