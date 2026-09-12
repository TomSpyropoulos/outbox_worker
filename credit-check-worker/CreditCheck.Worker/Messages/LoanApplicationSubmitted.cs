using System.Text.Json;

namespace CreditCheck.Worker.Messages;

/// <summary>
/// Read from the loan-events stream. The loan service publishes it from its own copy of this
/// class. The two only share the type name and the JSON shape.
/// </summary>
public class LoanApplicationSubmitted
{
    /// <summary>
    /// The "type" field on the stream entry. A constant rather than nameof(), so renaming the
    /// class can't silently change which entries we accept.
    /// </summary>
    public const string TypeName = "LoanApplicationSubmitted";

    // Static, so the options and their type cache are built once and reused.
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public Guid LoanId { get; set; }

    public string BorrowerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTime OccurredOnUtc { get; set; }

    /// <summary>Reads the stream entry's "payload" field. Returns null when it isn't a valid LoanApplicationSubmitted.</summary>
    public static LoanApplicationSubmitted? FromPayload(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<LoanApplicationSubmitted>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
