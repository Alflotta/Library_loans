using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed record MemberSummary(string Member, int OverdueLoans, int TotalFine, int MaxDaysLate, int Score);

public sealed record BalanceLine(string Member, int Amount);

public sealed record ReportBundle(DateOnly AsOf, int MinFine, FineReport Fines, ImmutableArray<MemberSummary> Members);

public static class LoanReports
{
    public static FineReport LargeFines(ImmutableArray<FineLine> lines, int minFine)
    {
        if (lines.IsDefault) throw new ArgumentException("Fine lines must be initialised.", nameof(lines));

        var rows = lines
            .Where(l => l.Fine >= minFine)
            .OrderByDescending(l => l.Fine)
            .ThenBy(l => l.LoanId, StringComparer.Ordinal)
            .ToImmutableArray();

        return new FineReport(rows, rows.Sum(l => l.Fine));
    }

    public static ImmutableArray<MemberSummary> MemberSummaries(ImmutableArray<FineLine> lines, Func<FineLine, int> score)
    {
        if (lines.IsDefault) throw new ArgumentException("Fine lines must be initialised.", nameof(lines));
        ArgumentNullException.ThrowIfNull(score);

        return lines
            .GroupBy(l => l.Member, StringComparer.Ordinal)
            .Select(g => new MemberSummary(
                g.Key,
                g.Count(),
                g.Sum(l => l.Fine),
                g.Aggregate(0, (max, l) => Math.Max(max, l.DaysLate)),
                g.Sum(score)))
            .OrderByDescending(m => m.TotalFine)
            .ThenBy(m => m.Member, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public static ImmutableArray<BalanceLine> UnpaidBalances(ImmutableDictionary<string, int> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);

        return balances
            .Where(kv => kv.Value > 0)
            .Select(kv => new BalanceLine(kv.Key, kv.Value))
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.Member, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
