namespace Loans.Api.Models;

/// <summary>
/// An event waiting to be published to Redis. It's written in the same SaveChanges as the change
/// that caused it, so the two are saved together or not at all.
/// </summary>
public class OutboxMessage
{
    public const int ErrorMaxLength = 2000;

    /// <summary>Also the message ID that consumers see.</summary>
    public Guid Id { get; set; }

    public DateTime OccurredOnUtc { get; set; }

    /// <summary>The event's name, e.g. LoanApplicationSubmitted.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The event, serialized as JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Null while the message is waiting to be published.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>How many times publishing failed because of the message itself.</summary>
    public int AttemptCount { get; set; }

    /// <summary>The last failure, for whoever inspects a stuck message.</summary>
    public string? Error { get; set; }
}
