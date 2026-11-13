using System.Collections.Immutable;
using System.Globalization;

namespace LibraryLoans.Core;

public static class LoanCsvParser
{
    public const string Header = "LoanId,Member,Book,DueDate,ReturnDate";
    private const string DateFormat = "yyyy-MM-dd";

    public static ParseOutput Parse(IEnumerable<string>? lines)
    {
        if (lines is null)
            return Fail("Input is missing (null).");

        var all = lines.ToArray();
        if (all.Length == 0 || all[0] is null || all[0].Trim() != Header)
            return Fail($"Header line must be exactly '{Header}'.");

        var loans = ImmutableArray.CreateBuilder<Loan>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < all.Length; i++)
        {
            var raw = all[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var lineNo = i + 1;
            var parts = raw.Split(',');
            if (parts.Length != 5)
            {
                errors.Add($"Line {lineNo}: expected 5 fields but found {parts.Length}.");
                continue;
            }

            var id = parts[0].Trim();
            var member = parts[1].Trim();
            var book = parts[2].Trim();
            var rowOk = true;

            if (id.Length == 0) { errors.Add($"Line {lineNo}: LoanId is empty."); rowOk = false; }
            else if (!seenIds.Add(id)) { errors.Add($"Line {lineNo}: duplicate LoanId '{id}'."); rowOk = false; }
            if (member.Length == 0) { errors.Add($"Line {lineNo}: Member is empty."); rowOk = false; }
            if (book.Length == 0) { errors.Add($"Line {lineNo}: Book is empty."); rowOk = false; }

            if (!TryDate(parts[3], out var due))
            {
                errors.Add($"Line {lineNo}: DueDate '{parts[3].Trim()}' is not a valid {DateFormat} date.");
                rowOk = false;
            }

            DateOnly? returned = null;
            var returnText = parts[4].Trim();
            if (returnText.Length > 0)
            {
                if (TryDate(returnText, out var r)) returned = r;
                else
                {
                    errors.Add($"Line {lineNo}: ReturnDate '{returnText}' is not a valid {DateFormat} date.");
                    rowOk = false;
                }
            }

            if (rowOk) loans.Add(new Loan(id, member, book, due, returned));
        }

        return errors.Count > 0
            ? new ParseOutput(ImmutableArray<Loan>.Empty, errors.ToImmutable())
            : new ParseOutput(loans.ToImmutable(), ImmutableArray<string>.Empty);
    }

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static ParseOutput Fail(string message) =>
        new(ImmutableArray<Loan>.Empty, ImmutableArray.Create(message));
}
