using System.Text.Json.Serialization;
using Loans.Api.BackgroundServices;
using Loans.Api.Data;
using Loans.Api.Repositories;
using Loans.Api.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Controllers, with enums in JSON as text ("Approved") instead of numbers.
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Connection strings are read inside these lambdas, which run the first time the service is
// needed rather than right now. That way, settings the integration tests add after this code
// runs still apply.
builder.Services.AddDbContext<LoansDbContext>(options =>
    options.UseSqlServer(GetConnectionString(builder.Configuration, "Loans")));
builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
    var redisOptions = ConfigurationOptions.Parse(GetConnectionString(builder.Configuration, "Redis"));

    // Start even when Redis is down. The background services retry until it's back.
    redisOptions.AbortOnConnectFail = false;

    // By default, commands sent while disconnected wait in a queue until they time out. The
    // outbox holds a SQL row lock during XADD, so fail right away instead and let it back off.
    redisOptions.BacklogPolicy = BacklogPolicy.FailFast;

    // Limits how long a slow Redis can hold that row lock. The default is 5 seconds.
    redisOptions.AsyncTimeout = 2000;

    return ConnectionMultiplexer.Connect(redisOptions);
});

// Scoped: a new instance per HTTP request, or per scope in the background services. They share
// that scope's DbContext.
builder.Services.AddScoped<ILoanRepository, LoanRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();
builder.Services.AddScoped<ILoanService, LoanService>();
builder.Services.AddScoped<IOutboxService, OutboxService>();

// Background services start with the app and run until it stops. Each one reads its settings
// from the appsettings.json section of the same name.
builder.Services.Configure<OutboxPublisherOptions>(builder.Configuration.GetSection(OutboxPublisherOptions.SectionName));
builder.Services.Configure<LoanDecisionConsumerOptions>(builder.Configuration.GetSection(LoanDecisionConsumerOptions.SectionName));
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<LoanDecisionConsumer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Create or update the database schema before serving requests. Fine with one instance. With
// several, run migrations as a separate deployment step, so instances don't race each other.
using (IServiceScope scope = app.Services.CreateScope())
{
    LoansDbContext db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
    await db.Database.MigrateAsync();
}

app.MapControllers();

await app.RunAsync();

static string GetConnectionString(IConfiguration configuration, string name)
{
    string? connectionString = configuration.GetConnectionString(name);
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException($"Connection string '{name}' is missing.");
    }

    return connectionString;
}
