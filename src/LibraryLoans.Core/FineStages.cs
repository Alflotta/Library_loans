using System.Collections.Immutable;

namespace LibraryLoans.Core;

public static class FineStages
{
    public static int DaysLate(Loan loan, DateOnly asOf)
    {
        var end = loan.ReturnDate ?? asOf;
        return Math.Max(0, end.DayNumber - loan.DueDate.DayNumber);
    }

    public static ImmutableArray<OverdueLoan> SelectOverdue(IEnumerable<Loan> loans, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(loans);
        return loans
            .Select(l => new OverdueLoan(l, DaysLate(l, asOf)))
            .Where(o => o.DaysLate > 0)
            .ToImmutableArray();
    }

    public static ImmutableArray<FineLine> ApplyFines(ImmutableArray<OverdueLoan> overdue, Func<int, int> fineForDaysLate)
    {
        ArgumentNullException.ThrowIfNull(fineForDaysLate);
        if (overdue.IsDefault) throw new ArgumentException("Overdue list must be initialised.", nameof(overdue));

        return overdue
            .Select(o => new FineLine(o.Loan.LoanId, o.Loan.Member, o.Loan.Book, o.DaysLate, fineForDaysLate(o.DaysLate)))
            .ToImmutableArray();
    }

    public static FineReport BuildReport(ImmutableArray<FineLine> lines)
    {
        if (lines.IsDefault) throw new ArgumentException("Fine lines must be initialised.", nameof(lines));

        var ordered = lines
            .OrderByDescending(l => l.Fine)
            .ThenBy(l => l.LoanId, StringComparer.Ordinal)
            .ToImmutableArray();

        return new FineReport(ordered, ordered.Sum(l => l.Fine));
    }
}
