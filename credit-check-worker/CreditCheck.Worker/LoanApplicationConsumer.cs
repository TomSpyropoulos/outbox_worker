using CreditCheck.Worker.Messages;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CreditCheck.Worker;

/// <summary>
/// Reads LoanApplicationSubmitted entries, decides each loan, and publishes a LoanDecisionMade.
/// This service has no database, so there's no dual write to protect with an outbox. The
/// publish and the ack both go to Redis and can share one Redis transaction.
/// </summary>
public class LoanApplicationConsumer : BackgroundService
{
    // With XREADGROUP, "0" returns the entries this consumer read earlier but never acked.
    private const string OwnPendingEntries = "0";

    private readonly IConnectionMultiplexer _redis;
    private readonly CreditCheckOptions _options;
    private readonly ILogger<LoanApplicationConsumer> _logger;

    public LoanApplicationConsumer(
        IConnectionMultiplexer redis,
        IOptions<CreditCheckOptions> options,
        ILogger<LoanApplicationConsumer> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool groupExists = false;
        bool replayingPending = true;
        TimeSpan backoff = TimeSpan.Zero;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = TimeSpan.Zero;
            try
            {
                if (!groupExists)
                {
                    await CreateConsumerGroupIfMissingAsync();
                    groupExists = true;
                }

                // After a restart, first finish our own unacked entries, then ask for new ones.
                RedisValue position;
                if (replayingPending)
                {
                    position = OwnPendingEntries;
                }
                else
                {
                    position = StreamPosition.NewMessages;
                }

                // StackExchange.Redis shares one connection between all callers, so it can't
                // offer a blocking XREADGROUP. We poll instead.
                StreamEntry[] entries = await _redis.GetDatabase().StreamReadGroupAsync(
                    _options.LoanEventsStream,
                    _options.ConsumerGroup,
                    _options.ConsumerName,
                    position,
                    _options.BatchSize);

                foreach (StreamEntry entry in entries)
                {
                    await HandleEntryAsync(entry);
                }

                if (entries.Length == 0)
                {
                    if (replayingPending)
                    {
                        replayingPending = false; // No pending entries left. Ask for new ones next.
                    }
                    else
                    {
                        delay = _options.PollInterval;
                    }
                }

                backoff = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break; // The app is shutting down.
                }

                // Whatever we didn't ack is still pending in Redis. Replay it once Redis is back.
                groupExists = false;
                replayingPending = true;
                backoff = NextBackoff(backoff);
                _logger.LogWarning(ex, "Consuming {Stream} failed, retrying in {Backoff}", _options.LoanEventsStream, backoff);
                delay = backoff;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task HandleEntryAsync(StreamEntry entry)
    {
        IDatabase db = _redis.GetDatabase();

        LoanApplicationSubmitted? loan = ParseLoan(entry);
        bool hasEventId = Guid.TryParse(entry["id"].ToString(), out Guid eventId);
        if (loan == null || !hasEventId)
        {
            // Retrying will never fix a malformed entry. Ack it so it doesn't block the stream.
            _logger.LogError("Skipping entry {EntryId} on {Stream}: not a valid LoanApplicationSubmitted", entry.Id, _options.LoanEventsStream);
            await db.StreamAcknowledgeAsync(_options.LoanEventsStream, _options.ConsumerGroup, entry.Id);
            return;
        }

        CreditDecision decision = CreditPolicy.Decide(loan, _options.MaxApprovedAmount);
        var message = new LoanDecisionMade
        {
            LoanId = loan.LoanId,
            Decision = decision.Decision,
            Reason = decision.Reason,
            CausationId = eventId,
            DecidedAtUtc = DateTime.UtcNow,
        };

        NameValueEntry[] fields =
        {
            new NameValueEntry("id", Guid.NewGuid().ToString()),
            new NameValueEntry("type", LoanDecisionMade.TypeName),
            new NameValueEntry("payload", message.ToPayload()),
        };
        var addOptions = new StreamAddOptions
        {
            MaxLength = _options.LoanDecisionsStreamMaxLength,
            Approximate = true,
        };

        // MULTI/EXEC: Redis runs both commands or neither. If we crash before ExecuteAsync,
        // nothing was published and the input is still pending, so it gets decided again.
        // The commands only run when ExecuteAsync sends them, so their tasks can't be awaited
        // before that.
        ITransaction transaction = db.CreateTransaction();
        Task<RedisValue> publishTask = transaction.StreamAddAsync(_options.LoanDecisionsStream, fields, addOptions);
        Task<long> ackTask = transaction.StreamAcknowledgeAsync(_options.LoanEventsStream, _options.ConsumerGroup, entry.Id);

        bool committed = await transaction.ExecuteAsync();
        if (!committed)
        {
            throw new InvalidOperationException($"Redis did not run the transaction for entry {entry.Id}.");
        }

        await publishTask;
        await ackTask;
        _logger.LogInformation("Loan {LoanId} {Decision}: {Reason}", loan.LoanId, decision.Decision, decision.Reason);
    }

    /// <summary>Returns null when the entry isn't a valid LoanApplicationSubmitted.</summary>
    private static LoanApplicationSubmitted? ParseLoan(StreamEntry entry)
    {
        if (entry["type"] != LoanApplicationSubmitted.TypeName)
        {
            return null;
        }

        return LoanApplicationSubmitted.FromPayload(entry["payload"].ToString());
    }

    private async Task CreateConsumerGroupIfMissingAsync()
    {
        try
        {
            // Start at the beginning of the stream ("0"), so loans submitted before this service
            // first ran are still decided. createStream makes an empty stream if there isn't one.
            await _redis.GetDatabase().StreamCreateConsumerGroupAsync(
                _options.LoanEventsStream,
                _options.ConsumerGroup,
                "0",
                createStream: true);
        }
        catch (RedisServerException ex)
        {
            // BUSYGROUP means another instance or an earlier run already created the group.
            if (!ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
            {
                throw;
            }
        }
    }

    // PollInterval, then doubling up to MaxBackoff.
    private TimeSpan NextBackoff(TimeSpan current)
    {
        if (current == TimeSpan.Zero)
        {
            return _options.PollInterval;
        }

        TimeSpan doubled = current * 2;
        if (doubled > _options.MaxBackoff)
        {
            return _options.MaxBackoff;
        }

        return doubled;
    }
}
