namespace LibraryLoans.Core;

public static class LoanQueryExtensions
{
    public static IEnumerable<OverdueLoan> WhereOverdue(this IEnumerable<Loan> loans, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(loans);

        return loans
            .Select(l => new OverdueLoan(l, FineStages.DaysLate(l, asOf)))
            .Where(o => o.DaysLate > 0);
    }
}
