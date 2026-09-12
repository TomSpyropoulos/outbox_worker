using Loans.Api.Models;
using StackExchange.Redis;

namespace Loans.Api.Tests.Integration;

[Collection(ContainersCollection.Name)]
public class OutboxTests
{
    private readonly ContainersFixture _containers;

    public OutboxTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    [Fact]
    public async Task Publishes_a_pending_message_to_the_stream_and_marks_it_processed()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        Guid loanId = await factory.SubmitLoanAsync();

        Assert.True(await factory.PublishNextAsync());
        Assert.False(await factory.PublishNextAsync()); // Nothing left.

        OutboxMessage message = Assert.Single(await factory.GetOutboxMessagesAsync());
        Assert.NotNull(message.ProcessedAtUtc);

        StreamEntry entry = Assert.Single(await factory.Redis.StreamRangeAsync(factory.LoanEventsStream));
        Assert.Equal(message.Id.ToString(), entry["id"].ToString());
        Assert.Equal("LoanApplicationSubmitted", entry["type"].ToString());
        Assert.Contains(loanId.ToString(), entry["payload"].ToString());
    }

    [Fact]
    public async Task Two_publishers_running_at_once_never_publish_the_same_message()
    {
        const int loanCount = 40;
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        for (int i = 0; i < loanCount; i++)
        {
            await factory.SubmitLoanAsync();
        }

        await Task.WhenAll(PublishAllAsync(factory), PublishAllAsync(factory));

        StreamEntry[] entries = await factory.Redis.StreamRangeAsync(factory.LoanEventsStream);
        Assert.Equal(loanCount, entries.Length);
        int distinctIds = entries.Select(entry => entry["id"].ToString()).Distinct().Count();
        Assert.Equal(loanCount, distinctIds);
        Assert.All(await factory.GetOutboxMessagesAsync(), message => Assert.NotNull(message.ProcessedAtUtc));
    }

    [Fact]
    public async Task A_Redis_outage_does_not_count_as_an_attempt()
    {
        Dictionary<string, string?> settings = _containers.CreateSettings();
        settings["ConnectionStrings:Redis"] = "localhost:1,connectTimeout=500"; // Nothing listens here.
        await using var factory = new LoansApiFactory(settings);
        await factory.SubmitLoanAsync();

        await Assert.ThrowsAnyAsync<RedisException>(() => factory.PublishNextAsync());

        OutboxMessage message = Assert.Single(await factory.GetOutboxMessagesAsync());
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.AttemptCount);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task Messages_that_reached_max_attempts_are_skipped()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        await factory.AddOutboxMessageAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = "LoanApplicationSubmitted",
            Payload = "{}",
            AttemptCount = 5,
            Error = "Failed five times.",
        });

        Assert.False(await factory.PublishNextAsync());
        Assert.Equal(0, await factory.Redis.StreamLengthAsync(factory.LoanEventsStream));
    }

    private static async Task PublishAllAsync(LoansApiFactory factory)
    {
        bool published = true;
        while (published)
        {
            published = await factory.PublishNextAsync();
        }
    }
}
