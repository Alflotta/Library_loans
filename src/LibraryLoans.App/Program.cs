using System.Globalization;
using LibraryLoans.Core;

var path = args.Length > 0 ? args[0] : Path.Combine("data", "loans.csv");

DateOnly asOf;
if (args.Length > 1)
{
    if (!DateOnly.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
    {
        Console.Error.WriteLine($"Invalid date '{args[1]}'. Use yyyy-MM-dd.");
        return 2;
    }
}
else
{
    asOf = DateOnly.FromDateTime(DateTime.Today);
}

string[] lines;
try
{
    lines = File.ReadAllLines(path);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Cannot read '{path}': {ex.Message}");
    return 1;
}

var parsed = LoanCsvParser.Parse(lines);
if (!parsed.IsValid)
{
    Console.Error.WriteLine($"Input '{path}' was rejected, no report produced:");
    foreach (var error in parsed.Errors) Console.Error.WriteLine($"  - {error}");
    return 1;
}

Console.WriteLine($"Loaded {parsed.Loans.Length} loans from '{path}'. Fines calculated as of {asOf:yyyy-MM-dd}.");

foreach (var policyName in new[] { "standard", "strict" })
{
    FinePolicies.TryGet(policyName, out var policy);
    var report = FinePipeline.Run(parsed.Loans, asOf, policy);
    PrintReport(policyName, report);
}

return 0;

static void PrintReport(string policyName, FineReport report)
{
    Console.WriteLine();
    Console.WriteLine($"Policy: {policyName} ");
    if (report.Count == 0)
    {
        Console.WriteLine("No overdue loans. Total fine: 0");
        return;
    }

    foreach (var l in report.Lines)
        Console.WriteLine($"{l.LoanId,-6} {l.Member,-10} {l.Book,-14} {l.DaysLate,3} days  {l.Fine,6} KZT");
    Console.WriteLine($"Total fine: {report.TotalFine} KZT ({report.Count} loans)");
}
