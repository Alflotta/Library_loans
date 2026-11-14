using System.Collections.Immutable;
using System.Globalization;

namespace LibraryLoans.Core;

public static class ReportFormat
{
    public static ImmutableArray<string> ToLines(ReportBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var lines = ImmutableArray.CreateBuilder<string>();
        var date = bundle.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        lines.Add($"Fine reports as of {date}");
        lines.Add($"Report 1: fines of at least {bundle.MinFine} KZT, largest first");

        if (bundle.Fines.Count == 0)
        {
            lines.Add($"No fines at or above {bundle.MinFine} KZT.");
        }
        else
        {
            foreach (var l in bundle.Fines.Lines)
                lines.Add($"{l.LoanId,-6} {l.Member,-8} {l.Book,-12} {l.DaysLate,3} days {l.Fine,5} KZT");
            lines.Add($"Total: {bundle.Fines.TotalFine} KZT ({bundle.Fines.Count} loans)");
        }

        lines.Add("Report 2: overdue loans by member, largest total first");

        if (bundle.Members.Length == 0)
        {
            lines.Add("No member has overdue loans.");
        }
        else
        {
            foreach (var m in bundle.Members)
                lines.Add($"{m.Member,-8} loans {m.OverdueLoans,2} fine {m.TotalFine,5} KZT max late {m.MaxDaysLate,3} days score {m.Score,4}");
        }

        return lines.ToImmutable();
    }

    public static ImmutableArray<string> BalanceLines(ImmutableArray<BalanceLine> balances)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add("Unpaid balances, largest first");

        if (balances.IsDefaultOrEmpty)
        {
            lines.Add("No unpaid balances.");
        }
        else
        {
            foreach (var b in balances)
                lines.Add($"{b.Member,-8} {b.Amount,5} KZT");
        }

        return lines.ToImmutable();
    }
}
