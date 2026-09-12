using CreditCheck.Worker;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<CreditCheckOptions>(builder.Configuration.GetSection(CreditCheckOptions.SectionName));

string? redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrEmpty(redisConnectionString))
{
    throw new InvalidOperationException("Connection string 'Redis' is missing.");
}

var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
// Start even when Redis is down. The consumer retries until it's back.
redisOptions.AbortOnConnectFail = false;
builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider => ConnectionMultiplexer.Connect(redisOptions));

builder.Services.AddHostedService<LoanApplicationConsumer>();

var host = builder.Build();
await host.RunAsync();
