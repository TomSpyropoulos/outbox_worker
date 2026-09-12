using Loans.Api.BackgroundServices;
using Loans.Api.Data;
using Loans.Api.Models;
using Loans.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Loans.Api.Tests.Integration;

/// <summary>
/// The real loan service, running in memory against the test containers. Its Program.cs runs as
/// usual, including the migrations. The two background services are removed, so each test
/// decides exactly when the outbox publishes and when decisions are consumed.
/// </summary>
public class LoansApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public LoansApiFactory(Dictionary<string, string?> settings)
    {
        _settings = settings;
    }

    public string LoanEventsStream => Services.GetRequiredService<IOptions<OutboxPublisherOptions>>().Value.LoanEventsStream;

    public string LoanDecisionsStream => Services.GetRequiredService<IOptions<LoanDecisionConsumerOptions>>().Value.LoanDecisionsStream;

    public IDatabase Redis => Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            RemoveHostedService(services, typeof(OutboxPublisher));
            RemoveHostedService(services, typeof(LoanDecisionConsumer));
        });
    }

    public async Task<Guid> SubmitLoanAsync(decimal amount = 1000m)
    {
        using IServiceScope scope = Services.CreateScope();
        ILoanService loanService = scope.ServiceProvider.GetRequiredService<ILoanService>();
        LoanApplication loan = await loanService.SubmitAsync("Ada Lovelace", amount, "USD", CancellationToken.None);
        return loan.Id;
    }

    /// <summary>One round of what OutboxPublisher does in a loop.</summary>
    public async Task<bool> PublishNextAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        IOutboxService outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        return await outboxService.PublishNextAsync(CancellationToken.None);
    }

    public async Task<ApplyDecisionResult> ApplyDecisionAsync(Guid loanId, LoanDecision decision)
    {
        using IServiceScope scope = Services.CreateScope();
        ILoanService loanService = scope.ServiceProvider.GetRequiredService<ILoanService>();
        return await loanService.ApplyDecisionAsync(loanId, decision, CancellationToken.None);
    }

    public async Task<LoanApplication> GetLoanAsync(Guid id)
    {
        using IServiceScope scope = Services.CreateScope();
        LoansDbContext db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        return await db.LoanApplications.AsNoTracking().SingleAsync(l => l.Id == id);
    }

    public async Task<List<OutboxMessage>> GetOutboxMessagesAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        LoansDbContext db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    public async Task AddOutboxMessageAsync(OutboxMessage message)
    {
        using IServiceScope scope = Services.CreateScope();
        LoansDbContext db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
    }

    /// <summary>A consumer wired like the real one. Call StartAsync and StopAsync on it.</summary>
    public LoanDecisionConsumer CreateDecisionConsumer()
    {
        return new LoanDecisionConsumer(
            Services.GetRequiredService<IServiceScopeFactory>(),
            Services.GetRequiredService<IConnectionMultiplexer>(),
            Services.GetRequiredService<IOptions<LoanDecisionConsumerOptions>>(),
            NullLogger<LoanDecisionConsumer>.Instance);
    }

    private static void RemoveHostedService(IServiceCollection services, Type hostedServiceType)
    {
        ServiceDescriptor? descriptor = services.FirstOrDefault(
            d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == hostedServiceType);
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }
}
