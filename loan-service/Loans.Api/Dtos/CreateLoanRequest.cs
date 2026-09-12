using System.ComponentModel.DataAnnotations;
using Loans.Api.Models;

namespace Loans.Api.Dtos;

/// <summary>
/// The body of POST /api/loans. [ApiController] checks these attributes before the action runs,
/// and answers with a 400 on its own if any fail.
/// </summary>
public class CreateLoanRequest
{
    [Required]
    [StringLength(LoanApplication.BorrowerNameMaxLength, MinimumLength = 1)]
    public string BorrowerName { get; set; } = string.Empty;

    // The invariant-culture flags make "0.01" parse the same way on every machine, including
    // ones whose culture writes it as "0,01".
    [Range(typeof(decimal), "0.01", "1000000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }

    [Required]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency must be a three-letter ISO 4217 code, e.g. USD.")]
    public string Currency { get; set; } = string.Empty;
}
