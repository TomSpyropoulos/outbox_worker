namespace CreditCheck.Worker;

/// <summary>Settings from the "CreditCheck" section of appsettings.json.</summary>
public class CreditCheckOptions
{
    public const string SectionName = "CreditCheck";

    public string LoanEventsStream { get; set; } = "loan-events";

    public string LoanDecisionsStream { get; set; } = "loan-decisions";

    public string ConsumerGroup { get; set; } = "credit-check";

    /// <summary>
    /// Must stay the same across restarts. Entries this consumer read but never acked belong to
    /// this name, and a consumer with a new name never sees them. docker-compose.yml sets a fixed
    /// name, because in a container the machine name is the container ID, which changes on every
    /// recreate.
    /// </summary>
    public string ConsumerName { get; set; } = Environment.MachineName;

    public int BatchSize { get; set; } = 10;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

    public int LoanDecisionsStreamMaxLength { get; set; } = 100_000;

    /// <summary>Loans up to this amount are approved. Anything above is rejected.</summary>
    public decimal MaxApprovedAmount { get; set; } = 50_000m;
}
