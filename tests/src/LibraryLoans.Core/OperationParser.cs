using System.Collections.Immutable;
using System.Globalization;

namespace LibraryLoans.Core;

public sealed record OperationParseOutput(ImmutableArray<Operation> Operations, ImmutableArray<string> Errors)
{
    public bool IsValid => Errors.Length == 0;
}

public static class OperationParser
{
    private const string DateFormat = "yyyy-MM-dd";

    public static OperationParseOutput Parse(IEnumerable<string>? lines)
    {
        if (lines is null)
            return new OperationParseOutput(ImmutableArray<Operation>.Empty, ImmutableArray.Create("Input is missing (null)."));

        var operations = ImmutableArray.CreateBuilder<Operation>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var lineNo = 0;

        foreach (var raw in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var parts = raw.Split(',').Select(p => p.Trim()).ToArray();
            var command = parts[0].ToUpperInvariant();

            switch (command)
            {
                case "MEMBER":
                {
                    if (parts.Length != 2)
                        errors.Add(WrongFieldCount(lineNo, command, 2, parts.Length));
                    else
                        operations.Add(new Operation.RegisterMember(parts[1]));
                    break;
                }
                case "BOOK":
                {
                    if (parts.Length != 3)
                        errors.Add(WrongFieldCount(lineNo, command, 3, parts.Length));
                    else if (TryWhole(lineNo, "Copies", parts[2], errors, out var copies))
                        operations.Add(new Operation.AddBook(parts[1], copies));
                    break;
                }
                case "CHECKOUT":
                {
                    if (parts.Length != 6)
                    {
                        errors.Add(WrongFieldCount(lineNo, command, 6, parts.Length));
                        break;
                    }

                    var dateOk = TryDate(lineNo, parts[4], errors, out var loanDate);
                    var daysOk = TryWhole(lineNo, "Days", parts[5], errors, out var days);
                    if (dateOk && daysOk)
                        operations.Add(new Operation.Checkout(new CheckoutRequest(parts[1], parts[2], parts[3], loanDate, days)));
                    break;
                }
                case "RETURN":
                {
                    if (parts.Length != 3)
                        errors.Add(WrongFieldCount(lineNo, command, 3, parts.Length));
                    else if (TryDate(lineNo, parts[2], errors, out var returnDate))
                        operations.Add(new Operation.Return(new ReturnRequest(parts[1], returnDate)));
                    break;
                }
                default:
                {
                    errors.Add($"Line {lineNo}: Unknown command '{parts[0]}'.");
                    break;
                }
            }
        }

        return errors.Count > 0
            ? new OperationParseOutput(ImmutableArray<Operation>.Empty, errors.ToImmutable())
            : new OperationParseOutput(operations.ToImmutable(), ImmutableArray<string>.Empty);
    }

    private static string WrongFieldCount(int lineNo, string command, int expected, int found) =>
        $"Line {lineNo}: {command} expects {expected} fields but found {found}.";

    private static bool TryWhole(int lineNo, string name, string text, ImmutableArray<string>.Builder errors, out int value)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        errors.Add($"Line {lineNo}: {name} '{text}' is not a whole number.");
        return false;
    }

    private static bool TryDate(int lineNo, string text, ImmutableArray<string>.Builder errors, out DateOnly date)
    {
        if (DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        errors.Add($"Line {lineNo}: Date '{text}' is not a valid {DateFormat} date.");
        return false;
    }
}
