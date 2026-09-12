using Loans.Api.BackgroundServices;
using Loans.Api.Models;
using Loans.Api.Repositories;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Loans.Api.Services;

public class OutboxService : IOutboxService
{
    private readonly IOutboxRepository _outboxRepository;
    private readonly IConnectionMultiplexer _redis;
    private readonly OutboxPublisherOptions _options;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(
        IOutboxRepository outboxRepository,
        IConnectionMultiplexer redis,
        IOptions<OutboxPublisherOptions> options,
        ILogger<OutboxService> logger)
    {
        _outboxRepository = outboxRepository;
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> PublishNextAsync(CancellationToken cancellationToken)
    {
        // The SQL transaction stays open while we publish to Redis, so the row stays locked the
        // whole time. If this process dies mid-publish, SQL Server rolls the transaction back and
        // another worker picks the row up. `await using` rolls back on any early exit, including
        // an exception, unless CommitAsync ran.
        await using IDbContextTransaction transaction = await _outboxRepository.BeginTransactionAsync(cancellationToken);

        OutboxMessage? message = await _outboxRepository.GetNextForUpdateAsync(_options.MaxAttempts, cancellationToken);
        if (message == null)
        {
            return false;
        }

        try
        {
            await PublishToRedisAsync(message);
            message.ProcessedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // Redis being down says nothing about this message. Rethrow, so the transaction rolls
            // back without counting an attempt, and OutboxPublisher backs off.
            if (IsRedisUnavailable(ex))
            {
                throw;
            }

            // The failure points at this message. Count the attempt and commit, so the messages
            // behind it keep moving.
            message.AttemptCount++;
            message.Error = Truncate(ex.Message, OutboxMessage.ErrorMaxLength);
            _logger.LogError(
                ex,
                "Outbox message {MessageId} failed, attempt {Attempt} of {MaxAttempts}",
                message.Id,
                message.AttemptCount,
                _options.MaxAttempts);
        }

        await _outboxRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task PublishToRedisAsync(OutboxMessage message)
    {
        NameValueEntry[] fields =
        {
            new NameValueEntry("id", message.Id.ToString()),
            new NameValueEntry("type", message.Type),
            new NameValueEntry("payload", message.Payload),
        };

        // Approximate trimming lets Redis drop old entries in whole blocks, which is much cheaper
        // than keeping the stream at an exact length.
        var addOptions = new StreamAddOptions
        {
            MaxLength = _options.LoanEventsStreamMaxLength,
            Approximate = true,
        };

        await _redis.GetDatabase().StreamAddAsync(_options.LoanEventsStream, fields, addOptions);
    }

    // Connection errors, timeouts and server errors are Redis problems, not message problems.
    // A timeout can even mean the XADD went through, so the retry may publish a duplicate.
    // Consumers have to handle duplicates anyway.
    private static bool IsRedisUnavailable(Exception ex)
    {
        return ex is RedisException || ex is TimeoutException || ex is OperationCanceledException;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength);
    }
}
