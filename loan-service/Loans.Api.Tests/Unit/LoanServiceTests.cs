using System.Globalization;
using System.Text.Json;
using Loans.Api.Models;
using Loans.Api.Services;

namespace Loans.Api.Tests.Unit;

public class LoanServiceTests
{
    private readonly FakeLoanRepository _repository = new FakeLoanRepository();
    private readonly LoanService _service;

    public LoanServiceTests()
    {
        _service = new LoanService(_repository);
    }

    [Fact]
    public async Task SubmitAsync_saves_a_submitted_loan_together_with_its_outbox_message()
    {
        LoanApplication loan = await _service.SubmitAsync("Ada Lovelace", 12500.50m, "EUR", CancellationToken.None);

        Assert.Equal(LoanStatus.Submitted, loan.Status);
        Assert.Equal(12500.50m, loan.Amount);
        Assert.Same(loan, Assert.Single(_repository.AddedLoans));

        OutboxMessage message = Assert.Single(_repository.AddedOutboxMessages);
        Assert.Equal("LoanApplicationSubmitted", message.Type);
        Assert.Equal(loan.CreatedAtUtc, message.OccurredOnUtc);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.AttemptCount);

        // The payload is what credit-check reads, so check the JSON itself, camelCase names included.
        using JsonDocument payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(loan.Id, payload.RootElement.GetProperty("loanId").GetGuid());
        Assert.Equal("Ada Lovelace", payload.RootElement.GetProperty("borrowerName").GetString());
        Assert.Equal(12500.50m, payload.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("EUR", payload.RootElement.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task SubmitAsync_trims_the_borrower_name()
    {
        LoanApplication loan = await _service.SubmitAsync("  Ada Lovelace  ", 100m, "EUR", CancellationToken.None);

        Assert.Equal("Ada Lovelace", loan.BorrowerName);
    }

    [Theory]
    [InlineData("10.123")]
    [InlineData("0.001")]
    public async Task SubmitAsync_rejects_more_than_two_decimal_places(string amount)
    {
        decimal parsedAmount = decimal.Parse(amount, CultureInfo.InvariantCulture);

        var exception = await Assert.ThrowsAsync<LoanValidationException>(
            () => _service.SubmitAsync("Ada Lovelace", parsedAmount, "EUR", CancellationToken.None));

        Assert.Equal("Amount", exception.Field);
        Assert.Empty(_repository.AddedLoans);
        Assert.Empty(_repository.AddedOutboxMessages);
    }

    [Theory]
    [InlineData(ApplyDecisionResult.Applied)]
    [InlineData(ApplyDecisionResult.AlreadyDecided)]
    [InlineData(ApplyDecisionResult.LoanNotFound)]
    public async Task ApplyDecisionAsync_returns_what_the_repository_reports(ApplyDecisionResult expected)
    {
        _repository.DecisionResult = expected;

        ApplyDecisionResult result = await _service.ApplyDecisionAsync(Guid.NewGuid(), LoanDecision.Approved, CancellationToken.None);

        Assert.Equal(expected, result);
    }
}
