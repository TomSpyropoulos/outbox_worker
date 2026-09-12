using Loans.Api.Messages;
using Loans.Api.Models;
using Loans.Api.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Loans.Api.BackgroundServices;

/// <summary>
/// Runs in the background for the life of the app, reading LoanDecisionMade entries from Redis.
/// Like LoansController does for HTTP, this class only handles the transport: reading, parsing
/// and acking. Applying the decision is up to LoanService.
/// </summary>
public class LoanDecisionConsumer : BackgroundService
{
    // With XREADGROUP, "0" returns the entries this consumer read earlier but never acked.
    private const string OwnPendingEntries = "0";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly LoanDecisionConsumerOptions _options;
    private readonly ILogger<LoanDecisionConsumer> _logger;

    public LoanDecisionConsumer(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        IOptions<LoanDecisionConsumerOptions> options,
        ILogger<LoanDecisionConsumer> logger)
    {
        _scopeFactory = scopeFactory;
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
                    _options.LoanDecisionsStream,
                    _options.ConsumerGroup,
                    _options.ConsumerName,
                    position,
                    _options.BatchSize);

                foreach (StreamEntry entry in entries)
                {
                    await HandleEntryAsync(entry, stoppingToken);
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

                // Whatever we didn't ack is still pending in Redis. Replay it once things recover.
                groupExists = false;
                replayingPending = true;
                backoff = NextBackoff(backoff);
                _logger.LogWarning(ex, "Consuming {Stream} failed, retrying in {Backoff}", _options.LoanDecisionsStream, backoff);
                delay = backoff;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task HandleEntryAsync(StreamEntry entry, CancellationToken cancellationToken)
    {
        LoanDecisionMade? message = ParseEntry(entry);
        if (message == null)
        {
            // Retrying will never fix a malformed entry. It gets acked below, so it doesn't block the stream.
            _logger.LogError("Skipping entry {EntryId} on {Stream}: not a valid LoanDecisionMade", entry.Id, _options.LoanDecisionsStream);
        }
        else
        {
            ApplyDecisionResult result = await ApplyDecisionAsync(message, cancellationToken);
            switch (result)
            {
                case ApplyDecisionResult.Applied:
                    _logger.LogInformation("Loan {LoanId} {Decision}", message.LoanId, message.Decision);
                    break;
                case ApplyDecisionResult.AlreadyDecided:
                    _logger.LogInformation("Loan {LoanId} was already decided, ignoring duplicate decision", message.LoanId);
                    break;
                case ApplyDecisionResult.LoanNotFound:
                    _logger.LogWarning("Decision for unknown loan {LoanId}, ignoring it", message.LoanId);
                    break;
            }
        }

        // Ack only after the database update committed. If we crash before this line, Redis
        // delivers the entry again and the second update changes nothing.
        await _redis.GetDatabase().StreamAcknowledgeAsync(_options.LoanDecisionsStream, _options.ConsumerGroup, entry.Id);
    }

    private async Task<ApplyDecisionResult> ApplyDecisionAsync(LoanDecisionMade message, CancellationToken cancellationToken)
    {
        // This class lives for the whole app, but LoanService and its DbContext are scoped, so
        // each message gets its own scope.
        using IServiceScope scope = _scopeFactory.CreateScope();
        ILoanService loanService = scope.ServiceProvider.GetRequiredService<ILoanService>();
        return await loanService.ApplyDecisionAsync(message.LoanId, message.Decision, cancellationToken);
    }

    /// <summary>Returns null when the entry isn't a valid LoanDecisionMade.</summary>
    private static LoanDecisionMade? ParseEntry(StreamEntry entry)
    {
        if (entry["type"] != LoanDecisionMade.TypeName)
        {
            return null;
        }

        return LoanDecisionMade.FromPayload(entry["payload"].ToString());
    }

    private async Task CreateConsumerGroupIfMissingAsync()
    {
        try
        {
            // Start at the beginning of the stream ("0"), so decisions published before the group
            // existed are still read. createStream makes an empty stream if there isn't one yet.
            await _redis.GetDatabase().StreamCreateConsumerGroupAsync(
                _options.LoanDecisionsStream,
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
