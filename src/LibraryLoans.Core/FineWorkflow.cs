using System.Collections.Immutable;

namespace LibraryLoans.Core;

public static class FineWorkflow
{
    public static ReportBundle Build(
        ImmutableArray<Loan> loans,
        DateOnly asOf,
        Func<int, int> policy,
        Func<FineLine, int> score,
        int minFine)
    {
        if (loans.IsDefault) throw new ArgumentException("Loans must be initialised.", nameof(loans));
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(score);

        var lines = FineStages.ApplyFines(FineStages.SelectOverdue(loans, asOf), policy);

        return new ReportBundle(
            asOf,
            minFine,
            LoanReports.LargeFines(lines, minFine),
            LoanReports.MemberSummaries(lines, score));
    }
}
