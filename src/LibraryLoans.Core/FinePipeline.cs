namespace LibraryLoans.Core;

public static class FinePipeline
{
    public static FineReport Run(IReadOnlyList<Loan> loans, DateOnly asOf, Func<int, int> policy) =>
        FineStages.BuildReport(
            FineStages.ApplyFines(
                FineStages.SelectOverdue(loans, asOf),
                policy));
}
