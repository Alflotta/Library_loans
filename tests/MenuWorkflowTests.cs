using LibraryLoans.Boundary;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public sealed class MenuWorkflowTests : IDisposable
{
    private const string SampleState = """
{
  "Members": [
    "Aigerim",
    "Beck",
    "Dana",
    "Erlan",
    "Farida"
  ],
  "Books": [
    {
      "BookId": "Algorithms",
      "Copies": 1
    },
    {
      "BookId": "Atlas",
      "Copies": 1
    },
    {
      "BookId": "Clean Code",
      "Copies": 1
    },
    {
      "BookId": "Dune",
      "Copies": 2
    },
    {
      "BookId": "SICP",
      "Copies": 1
    }
  ],
  "Loans": [
    {
      "LoanId": "L001",
      "MemberId": "Aigerim",
      "BookId": "Clean Code",
      "LoanDate": "2026-10-20",
      "Days": 14,
      "Renewals": 0,
      "DueDate": "2026-11-03",
      "ReturnedOn": null,
      "Fine": 0
    },
    {
      "LoanId": "L002",
      "MemberId": "Beck",
      "BookId": "Dune",
      "LoanDate": "2026-11-05",
      "Days": 14,
      "Renewals": 0,
      "DueDate": "2026-11-19",
      "ReturnedOn": null,
      "Fine": 0
    },
    {
      "LoanId": "L003",
      "MemberId": "Dana",
      "BookId": "SICP",
      "LoanDate": "2026-10-01",
      "Days": 14,
      "Renewals": 0,
      "DueDate": "2026-10-15",
      "ReturnedOn": "2026-10-20",
      "Fine": 250
    },
    {
      "LoanId": "L004",
      "MemberId": "Erlan",
      "BookId": "Atlas",
      "LoanDate": "2026-11-10",
      "Days": 7,
      "Renewals": 0,
      "DueDate": "2026-11-17",
      "ReturnedOn": null,
      "Fine": 0
    },
    {
      "LoanId": "L005",
      "MemberId": "Farida",
      "BookId": "Dune",
      "LoanDate": "2026-11-01",
      "Days": 7,
      "Renewals": 1,
      "DueDate": "2026-11-15",
      "ReturnedOn": null,
      "Fine": 0
    }
  ],
  "Reservations": [
    {
      "BookId": "Clean Code",
      "Members": [
        "Beck"
      ]
    }
  ],
  "Balances": [
    {
      "MemberId": "Dana",
      "Amount": 250
    }
  ]
}
""";

    private const string Script = """
RENEW,L005,2026-11-14
RENEW,L001,2026-11-14
RESERVE,Dana,Dune
RETURN,L001,2026-11-14
RETURN,L002,2026-11-14
CHECKOUT,L006,Dana,Dune,2026-11-14,7
CHECKOUT,L007,Aigerim,SICP,2026-11-14,7
PAY,Aigerim,100
CHECKOUT,L007,Aigerim,SICP,2026-11-14,7
CHECKOUT,L009,Erlan,Clean Code,2026-11-14,7
CHECKOUT,L008,Beck,Clean Code,2026-11-14,7
""";

    private const string LoansCsv = """
LoanId,Member,Book,DueDate,ReturnDate
L201,Aigerim,Clean Code,2026-10-21,
L202,Aigerim,Dune,2026-10-26,2026-10-31
L203,Beck,SICP,2026-10-21,
L204,Beck,Hobbit,2026-11-05,
L205,Dana,Atlas,2026-09-01,
L206,Dana,Dune,2026-10-11,2026-10-31
L207,Erlan,Algorithms,2026-10-29,
L208,Erlan,Refactoring,2026-10-31,
L209,Farida,Clean Code,2026-10-26,2026-10-26
L210,Farida,SICP,2026-10-16,
""";

    private static readonly DateOnly Today = new(2026, 11, 14);

    private readonly string _folder;

    public MenuWorkflowTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "libraryloans-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private (int Exit, string Output) RunWith(string statePath, string policyName, string reportPath, params string[] inputLines)
    {
        FinePolicies.TryGet(policyName, out var policy);
        var input = new StringReader(string.Join("\n", inputLines) + "\n");
        var output = new StringWriter();
        var exit = new MenuSession(input, output, statePath, reportPath, Today, policyName, policy).Run();
        return (exit, output.ToString());
    }

    private (int Exit, string Output) RunMenu(string statePath, params string[] inputLines) =>
        RunWith(statePath, "standard", Path.Combine(_folder, "report.txt"), inputLines);

    private string SamplePath() => Write("state.json", SampleState);

    [Fact]
    public void NormalWorkflow_RenewAndLateReturnAreSavedAndSurviveAReload()
    {
        var path = SamplePath();

        var run = RunMenu(path, "5", "L005", "4", "L001", "0");

        Assert.Equal(0, run.Exit);
        Assert.Contains("Loan L005 renewed, new due date 2026-11-22.", run.Output);
        Assert.Contains("Loan L001 returned on 2026-11-14. Fine charged: 550 KZT.", run.Output);
        Assert.Contains("State saved to", run.Output);

        var reloaded = Outcome.Value(StateStore.Load(path));
        Assert.Equal(550, reloaded.BalanceOf("Aigerim"));
        Assert.Equal(250, reloaded.BalanceOf("Dana"));
        Assert.Equal(new DateOnly(2026, 11, 22), Assert.IsType<LoanState.Active>(reloaded.Loans["L005"]).DueDate);
        Assert.Equal(550, Assert.IsType<LoanState.Returned>(reloaded.Loans["L001"]).Fine);
    }

    [Fact]
    public void NormalWorkflow_ScriptRunsLazilyAppliesAllRulesAndSavesTheFinalState()
    {
        var path = SamplePath();
        var script = Write("script.csv", Script);

        var run = RunMenu(path, "12", script, "0");

        Assert.Equal(0, run.Exit);
        Assert.Contains(" 1. Loan L005 renewed, new due date 2026-11-22.", run.Output);
        Assert.Contains(" 2. REJECTED (LoanOverdue): Loan 'L001' is overdue since 2026-11-03 and cannot be renewed.", run.Output);
        Assert.Contains(" 3. Member Dana reserved 'Dune', queue position 1.", run.Output);
        Assert.Contains(" 4. Loan L001 returned on 2026-11-14. Fine charged: 550 KZT.", run.Output);
        Assert.Contains(" 6. Loan L006 issued to Dana for 'Dune', due 2026-11-21.", run.Output);
        Assert.Contains(" 7. REJECTED (MemberBlocked): Member 'Aigerim' owes 550 KZT, above the limit of 500 KZT.", run.Output);
        Assert.Contains(" 8. Member Aigerim paid 100 KZT, remaining balance 450 KZT.", run.Output);
        Assert.Contains(" 9. Loan L007 issued to Aigerim for 'SICP', due 2026-11-21.", run.Output);
        Assert.Contains("10. REJECTED (BookReserved): Book 'Clean Code' is reserved for 'Beck'.", run.Output);
        Assert.Contains("11. Loan L008 issued to Beck for 'Clean Code', due 2026-11-21.", run.Output);
        Assert.Contains("Script finished: 11 operations, accepted 8, rejected 3.", run.Output);

        var reloaded = Outcome.Value(StateStore.Load(path));
        Assert.Equal(450, reloaded.BalanceOf("Aigerim"));
        Assert.Equal(250, reloaded.BalanceOf("Dana"));
        Assert.Empty(reloaded.Reservations);
        Assert.Equal(new[] { "L001", "L002", "L003", "L004", "L005", "L006", "L007", "L008" }, reloaded.Loans.Keys.OrderBy(k => k));
    }

    [Fact]
    public void FailureWorkflow_ABlockedMemberIsRejectedAndTheSavedStateStaysConsistent()
    {
        var path = SamplePath();

        var run = RunMenu(path, "4", "L001", "3", "L009,Aigerim,SICP,7", "0");

        Assert.Contains("REJECTED (MemberBlocked): Member 'Aigerim' owes 550 KZT, above the limit of 500 KZT.", run.Output);

        var reloaded = Outcome.Value(StateStore.Load(path));
        Assert.False(reloaded.Loans.ContainsKey("L009"));
        Assert.Equal(550, reloaded.BalanceOf("Aigerim"));
        Assert.Equal(1, reloaded.AvailableCopies("SICP"));
    }

    [Fact]
    public void Policy_TheSameLateReturnCostsMoreUnderTheStrictPolicy()
    {
        var standard = RunWith(SamplePath(), "standard", Path.Combine(_folder, "a.txt"), "4", "L001", "0");
        var strict = RunWith(Write("strict.json", SampleState), "strict", Path.Combine(_folder, "b.txt"), "4", "L001", "0");

        Assert.Contains("Fine charged: 550 KZT.", standard.Output);
        Assert.Contains("Fine charged: 1100 KZT.", strict.Output);
    }

    [Fact]
    public void Reports_ShowBothLinqReportsAndTheUnpaidBalances()
    {
        var run = RunMenu(SamplePath(), "8", "0");

        Assert.Contains("Fine reports as of 2026-11-14", run.Output);
        Assert.Contains("L001   Aigerim  Clean Code    11 days   550 KZT", run.Output);
        Assert.Contains("L003   Dana     SICP           5 days   250 KZT", run.Output);
        Assert.Contains("Total: 800 KZT (2 loans)", run.Output);
        Assert.Contains("Aigerim  loans  1 fine   550 KZT max late  11 days score   16", run.Output);
        Assert.Contains("Dana     loans  1 fine   250 KZT max late   5 days score    7", run.Output);
        Assert.Contains("Dana       250 KZT", run.Output);
    }

    [Fact]
    public void Reports_SwitchingTheScoringConfigurationChangesOnlyTheScores()
    {
        var run = RunMenu(SamplePath(), "11", "8", "0");

        Assert.Contains("Scoring configuration is now escalation.", run.Output);
        Assert.Contains("Aigerim  loans  1 fine   550 KZT max late  11 days score   66", run.Output);
        Assert.Contains("Dana     loans  1 fine   250 KZT max late   5 days score   30", run.Output);
    }

    [Fact]
    public void Export_WritesTheSameLinesThatAreShownAndAFailedExportDoesNotStopTheMenu()
    {
        var path = SamplePath();
        var report = Path.Combine(_folder, "out", "report.txt");
        var blocker = Write("blocker", "a file");

        var run = RunWith(path, "standard", report, "9", "0");
        var broken = RunWith(path, "standard", Path.Combine(blocker, "report.txt"), "9", "8", "0");

        Assert.Equal(0, run.Exit);
        var lines = File.ReadAllLines(report);
        Assert.Equal("Fine reports as of 2026-11-14", lines[0]);
        Assert.Contains("Total: 800 KZT (2 loans)", lines);
        Assert.Contains("Dana       250 KZT", lines);
        Assert.Equal(0, broken.Exit);
        Assert.Contains("[ExportFailed]", broken.Output);
        Assert.Contains("Total: 800 KZT (2 loans)", broken.Output);
    }

    [Fact]
    public void Lookup_FoundAndMissingItemsAreExplicit()
    {
        var run = RunMenu(SamplePath(), "10", "member,Dana", "10", "book,Clean Code", "10", "loan,L005", "10", "loan,L999", "10", "member,Zhan", "0");

        Assert.Contains("Member 'Dana' is registered, unpaid balance 250 KZT.", run.Output);
        Assert.Contains("Book 'Clean Code' found, available copies: 0, waiting: 1.", run.Output);
        Assert.Contains("Loan 'L005' is active: Farida has 'Dune', due 2026-11-15, renewals 1.", run.Output);
        Assert.Contains("Loan 'L999' was not found.", run.Output);
        Assert.Contains("Member 'Zhan' was not found.", run.Output);
    }

    [Fact]
    public void InvalidInput_IsRejectedWithoutChangingTheStateAndEndOfInputDoesNotSave()
    {
        var path = SamplePath();
        var before = File.ReadAllText(path);

        var run = RunMenu(path, "3", "L1,Dana", "3", "L1,Dana,Dune,many", "5", "L999", "42");

        Assert.Equal(0, run.Exit);
        Assert.Contains("Expected 4 values separated by commas", run.Output);
        Assert.Contains("Input rejected: Days 'many' is not a whole number.", run.Output);
        Assert.Contains("REJECTED (LoanNotFound): Loan 'L999' does not exist.", run.Output);
        Assert.Contains("Unknown choice '42'.", run.Output);
        Assert.Contains("Input ended. Changes were not saved.", run.Output);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void FinesReportFromACsvFileUsesThePipelineOnExternalData()
    {
        var csv = Write("loans.csv", LoansCsv);
        FinePolicies.TryGet("standard", out var policy);
        var output = new StringWriter();
        var input = new StringReader("13\n" + csv + "\n0\n");

        new MenuSession(input, output, SamplePath(), Path.Combine(_folder, "r.txt"), new DateOnly(2026, 10, 31), "standard", policy).Run();

        Assert.Contains("Fine reports as of 2026-10-31", output.ToString());
        Assert.Contains("Total: 4000 KZT (6 loans)", output.ToString());
    }

    [Fact]
    public void FileFailure_MissingStateFileStopsBeforeAnythingIsCreated()
    {
        var path = Path.Combine(_folder, "missing.json");

        var run = RunMenu(path, "0");

        Assert.Equal(1, run.Exit);
        Assert.Contains("Operational problem [FileNotFound]", run.Output);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void FileFailure_MalformedJsonAndInconsistentStateAreSafeFailures()
    {
        var malformed = Write("malformed.json", "{ \"Members\": [\"Aigerim\", \n");
        var inconsistent = Write("inconsistent.json", SampleState.Replace("\"MemberId\": \"Aigerim\"", "\"MemberId\": \"Nobody\""));
        var empty = Write("empty.json", "null");

        var first = RunMenu(malformed, "0");
        var second = RunMenu(inconsistent, "0");
        var third = RunMenu(empty, "0");

        Assert.Equal(1, first.Exit);
        Assert.Contains("[MalformedContent]", first.Output);
        Assert.Contains("not valid state JSON", first.Output);
        Assert.Equal(1, second.Exit);
        Assert.Contains("refers to unknown member 'Nobody'", second.Output);
        Assert.Equal(1, third.Exit);
        Assert.Contains("is empty", third.Output);
    }

    [Fact]
    public void Persistence_SaveThenLoadGivesTheSameStateAndMissingSectionsAreRejected()
    {
        var path = SamplePath();
        var state = Outcome.Value(StateStore.Load(path));
        var copy = Path.Combine(_folder, "copy.json");

        Outcome.Value(StateStore.Save(copy, state));
        var again = Outcome.Value(StateStore.Load(copy));
        var noSections = Write("sections.json", "{ \"Members\": [\"A\"] }");

        Assert.Equal(state.ToSnapshot().Loans.ToArray(), again.ToSnapshot().Loans.ToArray());
        Assert.Equal(state.QueueFor("Clean Code"), again.QueueFor("Clean Code"));
        Assert.Contains("State must contain", Outcome.Error(StateStore.Load(noSections)).Message);
    }
}
