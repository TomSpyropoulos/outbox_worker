namespace Loans.Api.BackgroundServices;

/// <summary>Settings from the "LoanDecisionConsumer" section of appsettings.json.</summary>
public class LoanDecisionConsumerOptions
{
    public const string SectionName = "LoanDecisionConsumer";

    public string LoanDecisionsStream { get; set; } = "loan-decisions";

    public string ConsumerGroup { get; set; } = "loan-service";

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
}
