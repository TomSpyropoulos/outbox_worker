namespace Loans.Api.Services;

public interface IOutboxService
{
    /// <summary>
    /// Publishes the oldest pending outbox message to Redis. Returns false when nothing was
    /// pending. Throws when Redis or SQL Server is unavailable.
    /// </summary>
    Task<bool> PublishNextAsync(CancellationToken cancellationToken);
}
