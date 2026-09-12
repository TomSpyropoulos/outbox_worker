using Loans.Api.Dtos;
using Loans.Api.Models;
using Loans.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Loans.Api.Controllers;

/// <summary>Only translates between HTTP and LoanService. No business rules live here.</summary>
[ApiController]
[Route("api/loans")]
public class LoansController : ControllerBase
{
    private readonly ILoanService _loanService;

    public LoansController(ILoanService loanService)
    {
        _loanService = loanService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SubmitLoanResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Submit(CreateLoanRequest request, CancellationToken cancellationToken)
    {
        LoanApplication loan;
        try
        {
            loan = await _loanService.SubmitAsync(request.BorrowerName, request.Amount, request.Currency, cancellationToken);
        }
        catch (LoanValidationException ex)
        {
            // Same 400 shape as the one [ApiController] sends when an attribute check fails.
            ModelState.AddModelError(ex.Field, ex.Message);
            return ValidationProblem(ModelState);
        }

        // 202 Accepted, because the credit decision happens later. The Location header points at
        // GET /api/loans/{id}, where the client can watch the status change.
        var response = new SubmitLoanResponse { LoanId = loan.Id };
        return AcceptedAtAction(nameof(Get), new { id = loan.Id }, response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(LoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        LoanApplication? loan = await _loanService.GetAsync(id, cancellationToken);
        if (loan == null)
        {
            return NotFound();
        }

        var response = new LoanResponse
        {
            Id = loan.Id,
            BorrowerName = loan.BorrowerName,
            Amount = loan.Amount,
            Currency = loan.Currency,
            Status = loan.Status,
            CreatedAtUtc = loan.CreatedAtUtc,
        };
        return Ok(response);
    }
}
