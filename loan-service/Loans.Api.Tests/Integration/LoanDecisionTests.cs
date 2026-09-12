using Loans.Api.BackgroundServices;
using Loans.Api.Messages;
using Loans.Api.Models;
using StackExchange.Redis;

namespace Loans.Api.Tests.Integration;

[Collection(ContainersCollection.Name)]
public class LoanDecisionTests
{
    private const string ConsumerGroup = "loan-service";

    private readonly ContainersFixture _containers;

    public LoanDecisionTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    [Fact]
    public async Task Concurrent_decisions_for_one_loan_are_applied_exactly_once()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        Guid loanId = await factory.SubmitLoanAsync();

        // Ten decisions at the same time, half Approved and half Rejected.
        var tasks = new List<Task<ApplyDecisionResult>>();
        for (int i = 0; i < 10; i++)
        {
            LoanDecision decision = i % 2 == 0 ? LoanDecision.Approved : LoanDecision.Rejected;
            tasks.Add(factory.ApplyDecisionAsync(loanId, decision));
        }

        ApplyDecisionResult[] results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result == ApplyDecisionResult.Applied));
        Assert.Equal(9, results.Count(result => result == ApplyDecisionResult.AlreadyDecided));
        LoanApplication loan = await factory.GetLoanAsync(loanId);
        Assert.NotEqual(LoanStatus.Submitted, loan.Status);
    }

    [Fact]
    public async Task A_decision_for_an_unknown_loan_changes_nothing()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());

        ApplyDecisionResult result = await factory.ApplyDecisionAsync(Guid.NewGuid(), LoanDecision.Approved);

        Assert.Equal(ApplyDecisionResult.LoanNotFound, result);
    }

    [Fact]
    public async Task Duplicate_decision_entries_change_the_status_once()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        Guid loanId = await factory.SubmitLoanAsync();
        await AddDecisionEntryAsync(factory, DecisionPayload(loanId, "Approved"));
        RedisValue lastEntryId = await AddDecisionEntryAsync(factory, DecisionPayload(loanId, "Rejected"));

        await RunConsumerUntilAllAckedAsync(factory, lastEntryId);

        LoanApplication loan = await factory.GetLoanAsync(loanId);
        Assert.Equal(LoanStatus.Approved, loan.Status);
    }

    [Fact]
    public async Task Malformed_entries_are_acked_and_skipped()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        Guid loanId = await factory.SubmitLoanAsync();
        await AddDecisionEntryAsync(factory, "this is not JSON");
        // Decisions must be text. As a number, 1 would mean Rejected if it were accepted.
        await AddDecisionEntryAsync(factory, $$"""{"loanId":"{{loanId}}","decision":1}""");
        RedisValue lastEntryId = await AddDecisionEntryAsync(factory, DecisionPayload(loanId, "Approved"));

        await RunConsumerUntilAllAckedAsync(factory, lastEntryId);

        LoanApplication loan = await factory.GetLoanAsync(loanId);
        Assert.Equal(LoanStatus.Approved, loan.Status);
    }

    [Fact]
    public async Task The_consumer_replays_entries_it_read_but_never_acked()
    {
        Dictionary<string, string?> settings = _containers.CreateSettings();
        settings["LoanDecisionConsumer:ConsumerName"] = "loans-1";
        await using var factory = new LoansApiFactory(settings);
        Guid loanId = await factory.SubmitLoanAsync();
        await factory.Redis.StreamCreateConsumerGroupAsync(factory.LoanDecisionsStream, ConsumerGroup, "0", createStream: true);
        await AddDecisionEntryAsync(factory, DecisionPayload(loanId, "Approved"));

        // Simulate a crash: loans-1 reads the entry and dies before acking it. Asking for new
        // entries (">") will never return it again. Only replaying pending ones ("0") does.
        StreamEntry[] read = await factory.Redis.StreamReadGroupAsync(
            factory.LoanDecisionsStream, ConsumerGroup, "loans-1", StreamPosition.NewMessages);
        Assert.Single(read);

        LoanDecisionConsumer consumer = factory.CreateDecisionConsumer();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Wait.UntilAsync(async () =>
            {
                StreamPendingInfo pending = await factory.Redis.StreamPendingAsync(factory.LoanDecisionsStream, ConsumerGroup);
                return pending.PendingMessageCount == 0;
            });
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        LoanApplication loan = await factory.GetLoanAsync(loanId);
        Assert.Equal(LoanStatus.Approved, loan.Status);
    }

    /// <summary>Runs the consumer until it has read every entry up to lastEntryId and acked them all.</summary>
    private static async Task RunConsumerUntilAllAckedAsync(LoansApiFactory factory, RedisValue lastEntryId)
    {
        LoanDecisionConsumer consumer = factory.CreateDecisionConsumer();
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Wait.UntilAsync(async () =>
            {
                StreamGroupInfo[] groups = await factory.Redis.StreamGroupInfoAsync(factory.LoanDecisionsStream);
                return groups.Length == 1
                    && groups[0].LastDeliveredId == lastEntryId.ToString()
                    && groups[0].PendingMessageCount == 0;
            });
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private static string DecisionPayload(Guid loanId, string decision)
    {
        return $$"""
            {"loanId":"{{loanId}}","decision":"{{decision}}","reason":"Test decision.","causationId":"{{Guid.NewGuid()}}","decidedAtUtc":"2026-01-01T00:00:00Z"}
            """;
    }

    private static async Task<RedisValue> AddDecisionEntryAsync(LoansApiFactory factory, string payload)
    {
        NameValueEntry[] fields =
        {
            new NameValueEntry("id", Guid.NewGuid().ToString()),
            new NameValueEntry("type", LoanDecisionMade.TypeName),
            new NameValueEntry("payload", payload),
        };
        return await factory.Redis.StreamAddAsync(factory.LoanDecisionsStream, fields, new StreamAddOptions());
    }
}
