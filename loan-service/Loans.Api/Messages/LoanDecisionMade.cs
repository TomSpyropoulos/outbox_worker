using System.Text.Json;
using System.Text.Json.Serialization;
using Loans.Api.Models;

namespace Loans.Api.Messages;

/// <summary>
/// Read from the loan-decisions stream. The credit-check worker publishes it from its own copy of
/// this class. The two only share the type name and the JSON shape.
/// </summary>
public class LoanDecisionMade
{
    /// <summary>
    /// The "type" field on the stream entry. A constant rather than nameof(), so renaming the
    /// class can't silently change which entries we accept.
    /// </summary>
    public const string TypeName = "LoanDecisionMade";

    // Static, so the options and their type cache are built once and reused.
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Guid LoanId { get; set; }

    public LoanDecision Decision { get; set; }

    public string? Reason { get; set; }

    /// <summary>The ID of the LoanApplicationSubmitted message that led to this decision.</summary>
    public Guid CausationId { get; set; }

    public DateTime DecidedAtUtc { get; set; }

    /// <summary>Reads the stream entry's "payload" field. Returns null when it isn't a valid LoanDecisionMade.</summary>
    public static LoanDecisionMade? FromPayload(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<LoanDecisionMade>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Decisions must arrive as "Approved" or "Rejected". Numbers like 0 or 1 are refused, so a
        // reordered enum can't silently flip a decision.
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
}
