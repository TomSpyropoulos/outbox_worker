namespace Loans.Api.BackgroundServices;

/// <summary>
/// Settings from the "OutboxPublisher" section of appsettings.json. OutboxService reads them too.
/// </summary>
public class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    public string LoanEventsStream { get; set; } = "loan-events";

    /// <summary>How long to wait when there is nothing to publish.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest wait between retries while Redis or SQL Server is unavailable.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Messages that failed this many times stay in the table for someone to inspect.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int LoanEventsStreamMaxLength { get; set; } = 100_000;
}
