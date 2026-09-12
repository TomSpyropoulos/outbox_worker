using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreditCheck.Worker.Messages;

/// <summary>
/// Published to the loan-decisions stream for the loan service to apply. The loan service has its
/// own copy of this class. The two only share the type name and the JSON shape.
/// </summary>
public class LoanDecisionMade
{
    /// <summary>
    /// The "type" field on the stream entry. A constant rather than nameof(), so renaming the
    /// class can't silently change what the loan service receives.
    /// </summary>
    public const string TypeName = "LoanDecisionMade";

    // Static, so the options and their type cache are built once and reused.
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Guid LoanId { get; set; }

    public LoanDecision Decision { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>The ID of the LoanApplicationSubmitted message that led to this decision.</summary>
    public Guid CausationId { get; set; }

    public DateTime DecidedAtUtc { get; set; }

    /// <summary>The JSON for the stream entry's "payload" field, with camelCase property names.</summary>
    public string ToPayload()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Write decisions as "Approved" or "Rejected". The loan service refuses numbers.
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
