using System.Globalization;
using LibraryLoans.Core;

if (args.Length > 0 && args[0] is not ("library" or "fines"))
{
    Console.Error.WriteLine("Usage: <library|fines> [csvPath] [yyyy-MM-dd]");
    return 2;
}

var mode = args.Length > 0 ? args[0] : "library";
var path = args.Length > 1 ? args[1] : Path.Combine("data", mode == "fines" ? "loans.csv" : "operations.csv");

DateOnly asOf;
if (args.Length > 2)
{
    if (!DateOnly.TryParseExact(args[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out asOf))
    {
        Console.Error.WriteLine($"Invalid date '{args[2]}'. Use yyyy-MM-dd.");
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

return mode == "fines" ? RunFines(lines, path, asOf) : RunLibrary(lines, path, asOf);

static int RunFines(string[] lines, string path, DateOnly asOf)
{
    var parsed = LoanCsvParser.Parse(lines);
    if (!parsed.IsValid)
    {
        PrintErrors(path, parsed.Errors);
        return 1;
    }

    Console.WriteLine($"Loaded {parsed.Loans.Length} loans from '{path}'. Fines calculated as of {asOf:yyyy-MM-dd}.");
    PrintFineReports(parsed.Loans, asOf);
    return 0;
}

static int RunLibrary(string[] lines, string path, DateOnly asOf)
{
    var parsed = OperationParser.Parse(lines);
    if (!parsed.IsValid)
    {
        PrintErrors(path, parsed.Errors);
        return 1;
    }

    var run = LibraryWorkflow.Run(LibraryState.Empty, parsed.Operations);

    for (var i = 0; i < run.Steps.Length; i++)
    {
        Console.WriteLine($"{i + 1,2}. {Describe(parsed.Operations[i])}");
        Console.WriteLine($"    {run.Steps[i].Message}");
    }

    var accepted = run.Steps.Count(s => s.Succeeded);
    var loans = run.FinalState.ToLoans();

    Console.WriteLine();
    Console.WriteLine($"Operations: {run.Steps.Length}, accepted: {accepted}, rejected: {run.Steps.Length - accepted}.");
    Console.WriteLine($"Loans in final state: {loans.Length}. Fines calculated as of {asOf:yyyy-MM-dd}.");
    PrintFineReports(loans, asOf);
    return 0;
}

static string Describe(Operation operation) => operation switch
{
    Operation.RegisterMember m => $"MEMBER {m.MemberId}",
    Operation.AddBook b => $"BOOK {b.BookId}, copies {b.Copies}",
    Operation.Checkout c => $"CHECKOUT {c.Request.LoanId} {c.Request.MemberId} '{c.Request.BookId}' {c.Request.LoanDate:yyyy-MM-dd} for {c.Request.Days} days",
    Operation.Return r => $"RETURN {r.Request.LoanId} {r.Request.ReturnDate:yyyy-MM-dd}",
    _ => "UNKNOWN OPERATION"
};

static void PrintErrors(string path, IEnumerable<string> errors)
{
    Console.Error.WriteLine($"Input '{path}' was rejected, nothing was processed:");
    foreach (var error in errors) Console.Error.WriteLine($"  - {error}");
}

static void PrintFineReports(IReadOnlyList<Loan> loans, DateOnly asOf)
{
    foreach (var policyName in new[] { "standard", "strict" })
    {
        FinePolicies.TryGet(policyName, out var policy);
        var report = FinePipeline.Run(loans, asOf, policy);
        PrintReport(policyName, report);
    }
}

static void PrintReport(string policyName, FineReport report)
{
    Console.WriteLine();
    Console.WriteLine($"Policy: {policyName}");
    if (report.Count == 0)
    {
        Console.WriteLine("No overdue loans. Total fine: 0");
        return;
    }

    foreach (var l in report.Lines)
        Console.WriteLine($"{l.LoanId,-6} {l.Member,-10} {l.Book,-14} {l.DaysLate,3} days  {l.Fine,6} KZT");
    Console.WriteLine($"Total fine: {report.TotalFine} KZT ({report.Count} loans)");
}
