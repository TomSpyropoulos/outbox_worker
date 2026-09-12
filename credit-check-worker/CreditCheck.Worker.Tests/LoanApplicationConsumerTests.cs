using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CreditCheck.Worker.Tests;

public class LoanApplicationConsumerTests : IClassFixture<RedisFixture>
{
    private readonly IDatabase _redis;
    private readonly IConnectionMultiplexer _connection;
    private readonly CreditCheckOptions _options;

    public LoanApplicationConsumerTests(RedisFixture fixture)
    {
        _connection = fixture.Connection;
        _redis = fixture.Connection.GetDatabase();

        // Each test gets its own streams, so tests never see each other's entries.
        string suffix = Guid.NewGuid().ToString("N");
        _options = new CreditCheckOptions
        {
            LoanEventsStream = "loan-events-" + suffix,
            LoanDecisionsStream = "loan-decisions-" + suffix,
            ConsumerName = "credit-check-1",
            PollInterval = TimeSpan.FromMilliseconds(100),
        };
    }

    [Fact]
    public async Task Publishes_a_decision_and_acks_the_event()
    {
        var loanId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await AddLoanEventAsync(eventId, $$"""{"loanId":"{{loanId}}","borrowerName":"Ada","amount":75000,"currency":"USD"}""");

        await RunConsumerUntilAsync(async () => await _redis.StreamLengthAsync(_options.LoanDecisionsStream) == 1);

        // The decision and the ack went out in one MULTI/EXEC, so seeing one means the other happened too.
        StreamPendingInfo pending = await _redis.StreamPendingAsync(_options.LoanEventsStream, _options.ConsumerGroup);
        Assert.Equal(0, pending.PendingMessageCount);

        StreamEntry entry = Assert.Single(await _redis.StreamRangeAsync(_options.LoanDecisionsStream));
        Assert.Equal("LoanDecisionMade", entry["type"].ToString());
        using JsonDocument decision = JsonDocument.Parse(entry["payload"].ToString());
        Assert.Equal(loanId, decision.RootElement.GetProperty("loanId").GetGuid());
        Assert.Equal("Rejected", decision.RootElement.GetProperty("decision").GetString());
        Assert.Equal(eventId, decision.RootElement.GetProperty("causationId").GetGuid());
    }

    [Fact]
    public async Task Malformed_entries_are_acked_without_a_decision()
    {
        await AddLoanEventAsync(Guid.NewGuid(), "this is not JSON");
        await AddLoanEventAsync(Guid.NewGuid(), $$"""{"loanId":"{{Guid.NewGuid()}}","amount":1000,"currency":"USD"}""");

        await RunConsumerUntilAsync(async () =>
        {
            StreamPendingInfo pending = await _redis.StreamPendingAsync(_options.LoanEventsStream, _options.ConsumerGroup);
            long decisions = await _redis.StreamLengthAsync(_options.LoanDecisionsStream);
            return pending.PendingMessageCount == 0 && decisions == 1;
        });

        // Only the valid entry produced a decision.
        Assert.Equal(1, await _redis.StreamLengthAsync(_options.LoanDecisionsStream));
    }

    private async Task RunConsumerUntilAsync(Func<Task<bool>> condition)
    {
        var consumer = new LoanApplicationConsumer(_connection, Options.Create(_options), NullLogger<LoanApplicationConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!await condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The condition was not met in time.");
                }

                await Task.Delay(100);
            }
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private async Task AddLoanEventAsync(Guid eventId, string payload)
    {
        NameValueEntry[] fields =
        {
            new NameValueEntry("id", eventId.ToString()),
            new NameValueEntry("type", "LoanApplicationSubmitted"),
            new NameValueEntry("payload", payload),
        };
        await _redis.StreamAddAsync(_options.LoanEventsStream, fields, new StreamAddOptions());
    }
}
