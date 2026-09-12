namespace Loans.Api.Models;

public class LoanApplication
{
    public const int BorrowerNameMaxLength = 200;

    public Guid Id { get; set; }

    public string BorrowerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>Three-letter ISO 4217 code, e.g. EUR.</summary>
    public string Currency { get; set; } = string.Empty;

    public LoanStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
