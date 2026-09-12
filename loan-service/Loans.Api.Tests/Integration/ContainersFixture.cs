using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Loans.Api.Tests.Integration;

/// <summary>One SQL Server and one Redis container, started once and shared by every integration test.</summary>
public class ContainersFixture : IAsyncLifetime
{
    // Same images as docker-compose.yml, so one download serves both.
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:alpine").Build();

    /// <summary>
    /// Settings for one test: its own database and its own stream names, so tests never see each
    /// other's data. Short poll intervals keep the tests fast.
    /// </summary>
    public Dictionary<string, string?> CreateSettings()
    {
        string suffix = Guid.NewGuid().ToString("N");
        var database = new SqlConnectionStringBuilder(_sqlServer.GetConnectionString());
        database.InitialCatalog = "Loans_" + suffix;

        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:Loans"] = database.ConnectionString,
            ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
            ["OutboxPublisher:LoanEventsStream"] = "loan-events-" + suffix,
            ["LoanDecisionConsumer:LoanDecisionsStream"] = "loan-decisions-" + suffix,
            ["LoanDecisionConsumer:PollInterval"] = "00:00:00.100",
        };
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sqlServer.StartAsync(), _redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await _sqlServer.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

/// <summary>Tests marked [Collection(ContainersCollection.Name)] share one ContainersFixture.</summary>
[CollectionDefinition(Name)]
public class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Containers";
}
