namespace Loans.Api.Models;

public enum ApplyDecisionResult
{
    /// <summary>The loan was Submitted and now has the decision's status.</summary>
    Applied,

    /// <summary>The loan already had a decision. Nothing changed. This is what a duplicate delivery looks like.</summary>
    AlreadyDecided,

    /// <summary>No loan has this ID. Nothing changed.</summary>
    LoanNotFound,
}
