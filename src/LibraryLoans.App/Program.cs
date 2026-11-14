using System.Globalization;
using LibraryLoans.Boundary;
using LibraryLoans.Core;

var statePath = args.Length > 0 ? args[0] : Path.Combine("data", "library_state.json");

var today = DateOnly.FromDateTime(DateTime.Today);
if (args.Length > 1 && !DateOnly.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out today))
{
    Console.Error.WriteLine($"Invalid date '{args[1]}'. Use yyyy-MM-dd.");
    return 2;
}

var policyName = args.Length > 2 ? args[2] : "standard";
Func<int, int> policy;
if (policyName.Trim().Equals("native", StringComparison.OrdinalIgnoreCase))
{
    var loaded = NativeFineCalculator.TryCreatePolicy(NativeFineCalculator.DefaultPath());
    if (loaded is not Result<Func<int, int>>.Ok nativePolicy)
    {
        Console.Error.WriteLine($"Operational problem [{((Result<Func<int, int>>.Failure)loaded).Error.Code}]: {((Result<Func<int, int>>.Failure)loaded).Error.Message}");
        return 1;
    }
    policy = nativePolicy.Value;
}
else if (!FinePolicies.TryGet(policyName, out policy))
{
    Console.Error.WriteLine($"Unknown fine policy '{policyName}'. Use standard, strict or native.");
    return 2;
}

var reportPath = args.Length > 3 ? args[3] : Path.Combine("out", "fine_report.txt");

return new MenuSession(Console.In, Console.Out, statePath, reportPath, today, policyName.Trim().ToLowerInvariant(), policy).Run();
