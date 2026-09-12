using Loans.Api.Models;

namespace Loans.Api.Dtos;

public class LoanResponse
{
    public Guid Id { get; set; }

    public string BorrowerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public LoanStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
