using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Loans.Api.Models;

namespace Loans.Api.Tests.Integration;

[Collection(ContainersCollection.Name)]
public class LoansApiTests
{
    private readonly ContainersFixture _containers;

    public LoansApiTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    [Fact]
    public async Task Submitting_a_loan_returns_202_and_saves_it_with_a_pending_outbox_message()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/loans",
            new { borrowerName = "Ada Lovelace", amount = 12500m, currency = "EUR" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        JsonElement loan = await client.GetFromJsonAsync<JsonElement>(response.Headers.Location);
        Assert.Equal("Submitted", loan.GetProperty("status").GetString());
        Guid loanId = loan.GetProperty("id").GetGuid();

        OutboxMessage message = Assert.Single(await factory.GetOutboxMessagesAsync());
        Assert.Equal("LoanApplicationSubmitted", message.Type);
        Assert.Contains(loanId.ToString(), message.Payload);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.AttemptCount);
    }

    [Theory]
    [InlineData("""{"borrowerName":"Ada","amount":0,"currency":"USD"}""", "amount")]
    [InlineData("""{"borrowerName":"Ada","amount":10.123,"currency":"USD"}""", "amount")]
    [InlineData("""{"borrowerName":"Ada","amount":100,"currency":"usd"}""", "currency")]
    [InlineData("""{"borrowerName":"","amount":100,"currency":"USD"}""", "borrowerName")]
    public async Task Invalid_requests_get_a_400_naming_the_field(string body, string field)
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            "/api/loans",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        List<string> fields = problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToList();
        Assert.Contains(fields, name => name.Equals(field, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await factory.GetOutboxMessagesAsync());
    }

    [Fact]
    public async Task An_unknown_loan_returns_404()
    {
        await using var factory = new LoansApiFactory(_containers.CreateSettings());
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/loans/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
