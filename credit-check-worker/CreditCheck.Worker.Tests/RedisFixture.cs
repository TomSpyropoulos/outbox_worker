using StackExchange.Redis;
using Testcontainers.Redis;

namespace CreditCheck.Worker.Tests;

/// <summary>One Redis container, shared by the tests of a class that uses IClassFixture&lt;RedisFixture&gt;.</summary>
public class RedisFixture : IAsyncLifetime
{
    // Same image as docker-compose.yml, so one download serves both.
    private readonly RedisContainer _redis = new RedisBuilder("redis:alpine").Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
