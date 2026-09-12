using Loans.Api.Services;
using Microsoft.Extensions.Options;

namespace Loans.Api.BackgroundServices;

/// <summary>
/// Runs in the background for the life of the app, publishing outbox messages to Redis one at a
/// time. This class only owns the loop: when to try again and how long to wait. The publishing
/// itself is in OutboxService.
/// </summary>
public class OutboxPublisher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxPublisherOptions _options;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxPublisherOptions> options,
        ILogger<OutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan backoff = TimeSpan.Zero;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                bool published = await PublishNextAsync(stoppingToken);

                // Keep going while there is work. Only wait once nothing is pending.
                if (published)
                {
                    delay = TimeSpan.Zero;
                }
                else
                {
                    delay = _options.PollInterval;
                }

                backoff = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break; // The app is shutting down.
                }

                // Redis or SQL Server is unavailable. The transaction rolled back, so nothing was
                // counted against the message. Wait longer after each failure in a row.
                backoff = NextBackoff(backoff);
                _logger.LogWarning(ex, "Publishing from the outbox failed, retrying in {Backoff}", backoff);
                delay = backoff;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task<bool> PublishNextAsync(CancellationToken cancellationToken)
    {
        // This class lives for the whole app, but OutboxService and its DbContext are scoped:
        // meant to be used for one unit of work, then thrown away. So each round creates its own
        // scope and gets a fresh OutboxService from it.
        using IServiceScope scope = _scopeFactory.CreateScope();
        IOutboxService outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        return await outboxService.PublishNextAsync(cancellationToken);
    }

    // 2s, 4s, 8s, ... up to MaxBackoff.
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
