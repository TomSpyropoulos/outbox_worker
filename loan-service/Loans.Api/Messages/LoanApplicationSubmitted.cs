using System.Text.Json;

namespace Loans.Api.Messages;

/// <summary>
/// Published to the loan-events stream when a loan is submitted. The credit-check worker has
/// its own copy of this class. The two only share the type name and the JSON shape, so both are
/// spelled out here on purpose.
/// </summary>
public class LoanApplicationSubmitted
{
    /// <summary>
    /// The "type" field on the stream entry. A constant rather than nameof(), so renaming the
    /// class can't silently change what credit-check receives.
    /// </summary>
    public const string TypeName = "LoanApplicationSubmitted";

    // Static, so the options and their type cache are built once and reused.
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public Guid LoanId { get; set; }

    public string BorrowerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTime OccurredOnUtc { get; set; }

    /// <summary>The JSON for the stream entry's "payload" field, with camelCase property names.</summary>
    public string ToPayload()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }
}
