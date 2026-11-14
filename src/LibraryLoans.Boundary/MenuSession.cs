using System.Collections.Immutable;
using System.Globalization;
using LibraryLoans.Core;

namespace LibraryLoans.Boundary;

public sealed class MenuSession
{
    public const int ReportMinFine = 250;

    private sealed record Command(string Key, string Title, string Prompt, int Fields, Func<string[], string, string> ToLine);

    private static readonly ImmutableArray<Command> Commands = ImmutableArray.Create(
        new Command("1", "Register member", "Member id", 1, (p, _) => $"MEMBER,{p[0]}"),
        new Command("2", "Add book", "Book id,copies", 2, (p, _) => $"BOOK,{p[0]},{p[1]}"),
        new Command("3", "Check out a book", "Loan id,member id,book id,days", 4, (p, date) => $"CHECKOUT,{p[0]},{p[1]},{p[2]},{date},{p[3]}"),
        new Command("4", "Return a book", "Loan id", 1, (p, date) => $"RETURN,{p[0]},{date}"),
        new Command("5", "Renew a loan", "Loan id", 1, (p, date) => $"RENEW,{p[0]},{date}"),
        new Command("6", "Reserve a book", "Member id,book id", 2, (p, _) => $"RESERVE,{p[0]},{p[1]}"),
        new Command("7", "Pay a fine", "Member id,amount", 2, (p, _) => $"PAY,{p[0]},{p[1]}"));

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly string _statePath;
    private readonly string _reportPath;
    private readonly DateOnly _today;
    private readonly string _policyName;
    private readonly Func<int, int> _policy;
    private string _scoreName = "reminder";
    private Func<FineLine, int> _score = Scoring.Reminder;

    public MenuSession(
        TextReader input,
        TextWriter output,
        string statePath,
        string reportPath,
        DateOnly today,
        string policyName,
        Func<int, int> policy)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _statePath = statePath;
        _reportPath = reportPath;
        _today = today;
        _policyName = policyName;
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public int Run() =>
        StateStore.Load(_statePath).Match<LibraryState, int>(Loop, Failed);

    private int Loop(LibraryState initial)
    {
        var state = initial;
        _output.WriteLine($"Loaded state from '{_statePath}': {state.Members.Count} members, {state.Stock.Count} books, {state.Loans.Count} loans.");

        while (true)
        {
            PrintMenu();
            var choice = _input.ReadLine();

            if (choice is null)
            {
                _output.WriteLine("Input ended. Changes were not saved.");
                return 0;
            }

            var key = choice.Trim();
            var command = Commands.FirstOrDefault(c => c.Key == key);

            if (command is not null)
            {
                state = RunCommand(state, command);
                continue;
            }

            switch (key)
            {
                case "0":
                    return Save(state);
                case "8":
                    ShowReports(state);
                    break;
                case "9":
                    ExportReports(state);
                    break;
                case "10":
                    Lookup(state);
                    break;
                case "11":
                    SwitchScoring();
                    break;
                case "12":
                    state = RunScript(state);
                    break;
                case "13":
                    FinesFromCsv();
                    break;
                default:
                    _output.WriteLine($"Unknown choice '{key}'.");
                    break;
            }
        }
    }

    private LibraryState RunCommand(LibraryState state, Command command)
    {
        var text = Ask(command.Prompt);
        if (text is null)
            return state;

        var parts = text.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length != command.Fields)
        {
            _output.WriteLine($"Expected {command.Fields} values separated by commas: {command.Prompt}.");
            return state;
        }

        var date = _today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return OperationParser.ParseLine(command.ToLine(parts, date), 1).Match<Operation, LibraryState>(
            operation =>
            {
                var step = LibraryWorkflow.Apply(state, operation, _policy);
                _output.WriteLine(step.Message);
                return step.State;
            },
            error =>
            {
                _output.WriteLine($"Input rejected: {error.Message.Replace("Line 1: ", string.Empty)}");
                return state;
            });
    }

    private void ShowReports(LibraryState state)
    {
        foreach (var line in BuildReportLines(state))
            _output.WriteLine(line);
    }

    private void ExportReports(LibraryState state)
    {
        SafeFileWriter.WriteLines(_reportPath, BuildReportLines(state)).Match<string, int>(
            path =>
            {
                _output.WriteLine($"Reports saved to '{path}'.");
                return 0;
            },
            error =>
            {
                Print(error, _output);
                return 1;
            });
    }

    private ImmutableArray<string> BuildReportLines(LibraryState state)
    {
        var bundle = FineWorkflow.Build(state.ToLoans(), _today, _policy, _score, ReportMinFine);
        return ReportFormat.ToLines(bundle)
            .AddRange(ReportFormat.BalanceLines(LoanReports.UnpaidBalances(state.Balances)));
    }

    private void Lookup(LibraryState state)
    {
        var text = Ask("member|book|loan,id");
        if (text is null)
            return;

        var parts = text.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length != 2)
        {
            _output.WriteLine("Expected 2 values separated by commas: member|book|loan,id.");
            return;
        }

        _output.WriteLine(parts[0].ToLowerInvariant() switch
        {
            "member" => state.FindMember(parts[1]).Match(
                member => $"Member '{member}' is registered, unpaid balance {state.BalanceOf(member)} KZT.",
                () => $"Member '{parts[1]}' was not found."),
            "book" => state.FindAvailableCopies(parts[1]).Match(
                copies => $"Book '{parts[1]}' found, available copies: {copies}, waiting: {state.QueueFor(parts[1]).Count}.",
                () => $"Book '{parts[1]}' was not found in the catalog."),
            "loan" => state.FindLoan(parts[1]).Match(
                DescribeLoan,
                () => $"Loan '{parts[1]}' was not found."),
            _ => $"Unknown lookup kind '{parts[0]}'."
        });
    }

    private static string DescribeLoan(LoanState loan) => loan switch
    {
        LoanState.Active a =>
            $"Loan '{a.Checkout.LoanId}' is active: {a.Checkout.MemberId} has '{a.Checkout.BookId}', due {a.DueDate:yyyy-MM-dd}, renewals {a.Renewals}.",
        LoanState.Returned r =>
            $"Loan '{r.Checkout.LoanId}' was returned on {r.ReturnedOn:yyyy-MM-dd}, fine {r.Fine} KZT.",
        _ => "Unknown loan state."
    };

    private void SwitchScoring()
    {
        var next = _scoreName == "reminder" ? "escalation" : "reminder";
        if (Scoring.TryGet(next, out var score))
        {
            _score = score;
            _scoreName = next;
        }

        _output.WriteLine($"Scoring configuration is now {_scoreName}.");
    }

    private LibraryState RunScript(LibraryState state)
    {
        var path = Ask("Script file path");
        if (path is null)
            return state;

        return FileBoundary.SnapshotOperations(path.Trim()).Match<ImmutableArray<Operation>, LibraryState>(
            operations =>
            {
                var current = state;
                var accepted = 0;
                var total = 0;

                foreach (var step in LibraryWorkflow.Steps(state, operations, _policy))
                {
                    total++;
                    if (step.Succeeded) accepted++;
                    _output.WriteLine($"{total,2}. {step.Message}");
                    current = step.State;
                }

                _output.WriteLine($"Script finished: {total} operations, accepted {accepted}, rejected {total - accepted}.");
                return current;
            },
            error =>
            {
                Print(error, _output);
                return state;
            });
    }

    private void FinesFromCsv()
    {
        var path = Ask("Loans CSV path");
        if (path is null)
            return;

        FileBoundary.LoadLoans(path.Trim()).Match<ImmutableArray<Loan>, int>(
            loans =>
            {
                var bundle = FineWorkflow.Build(loans, _today, _policy, _score, ReportMinFine);
                foreach (var line in ReportFormat.ToLines(bundle))
                    _output.WriteLine(line);
                return 0;
            },
            error =>
            {
                Print(error, _output);
                return 1;
            });
    }

    private int Save(LibraryState state) =>
        StateStore.Save(_statePath, state).Match<string, int>(
            path =>
            {
                _output.WriteLine($"State saved to '{path}'.");
                return 0;
            },
            error =>
            {
                Print(error, _output);
                return 1;
            });

    private string? Ask(string prompt)
    {
        _output.WriteLine($"{prompt}:");
        return _input.ReadLine();
    }

    private void PrintMenu()
    {
        _output.WriteLine();
        _output.WriteLine($"Library menu. Date {_today:yyyy-MM-dd}, fine policy {_policyName}, scoring {_scoreName}.");
        foreach (var c in Commands)
            _output.WriteLine($"{c.Key,2} {c.Title}");
        _output.WriteLine(" 8 Show reports");
        _output.WriteLine(" 9 Export reports to file");
        _output.WriteLine("10 Look up a member, book or loan");
        _output.WriteLine("11 Switch scoring configuration");
        _output.WriteLine("12 Run an operations script file");
        _output.WriteLine("13 Fines report from a loans CSV file");
        _output.WriteLine(" 0 Save and exit");
        _output.WriteLine("Choose:");
    }

    private int Failed(AppError error)
    {
        Print(error, _output);
        return 1;
    }

    private static void Print(AppError error, TextWriter output)
    {
        output.WriteLine($"{error.Kind} problem [{error.Code}]: {error.Message}");
    }
}
