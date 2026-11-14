using System.Collections.Immutable;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public class FinePipelineTests
{
    private static readonly string[] SampleLines =
    {
        "LoanId,Member,Book,DueDate,ReturnDate",
        "L001,Aigerim,Clean Code,2026-10-10,2026-10-15",
        "L002,Beck,SICP,2026-10-12,2026-10-12",
        "L003,Dana,Dune,2026-10-05,",
        "L004,Erlan,Algorithms,2026-09-01,",
        "L005,Farida,Refactoring,2026-10-18,2026-10-16",
        "L006,Gulnaz,Hobbit,2026-10-19,",
    };

    private static readonly DateOnly AsOf = new(2026, 10, 20);

    private static ImmutableArray<Loan> SampleLoans()
    {
        var parsed = LoanCsvParser.Parse(SampleLines);
        Assert.True(parsed.IsValid);
        return parsed.Loans;
    }

    private static Loan MakeLoan(string id, string due, string? returned) =>
        new(id, "Member", "Book", DateOnly.Parse(due), returned is null ? null : DateOnly.Parse(returned));

    private static (string Id, int Fine)[] Summary(FineReport r) =>
        r.Lines.Select(l => (l.LoanId, l.Fine)).ToArray();

    [Fact]
    public void NormalWorkflow_StandardPolicy_ProducesDocumentedReport()
    {
        var report = FinePipeline.Run(SampleLoans(), AsOf, FinePolicies.Standard);

        Assert.Equal(
            new[] { ("L004", 1000), ("L003", 750), ("L001", 250), ("L006", 50) },
            Summary(report));
        Assert.Equal(2050, report.TotalFine);
        Assert.Equal(4, report.Count);
    }

    [Fact]
    public void EmptyDataset_ProducesEmptyReport_NotAnError()
    {
        var parsed = LoanCsvParser.Parse(new[] { LoanCsvParser.Header });

        Assert.True(parsed.IsValid);
        Assert.Empty(parsed.Loans);

        var report = FinePipeline.Run(parsed.Loans, AsOf, FinePolicies.Standard);
        Assert.Equal(0, report.Count);
        Assert.Equal(0, report.TotalFine);
    }

    [Fact]
    public void Boundary_ReturnedOnDueDate_IsNotOverdue_OneDayLateIsFined()
    {
        var onDueDate = MakeLoan("A", "2026-10-12", "2026-10-12");
        var oneDayLate = MakeLoan("B", "2026-10-12", "2026-10-13");

        var overdue = FineStages.SelectOverdue(new[] { onDueDate, oneDayLate }, AsOf);

        var only = Assert.Single(overdue);
        Assert.Equal("B", only.Loan.LoanId);
        Assert.Equal(1, only.DaysLate);
        Assert.Equal(50, FinePolicies.Standard(only.DaysLate));
    }

    [Fact]
    public void Boundary_FineCapIsReachedExactlyAtItsThreshold()
    {
        Assert.Equal(950, FinePolicies.Standard(19));
        Assert.Equal(1000, FinePolicies.Standard(20));
        Assert.Equal(1000, FinePolicies.Standard(21));
        Assert.Equal(3000, FinePolicies.Strict(30));
        Assert.Equal(3000, FinePolicies.Strict(31));
    }

    [Fact]
    public void Parser_RejectsNullInput_WithoutFabricatingLoans()
    {
        var parsed = LoanCsvParser.Parse(null);

        Assert.False(parsed.IsValid);
        Assert.Empty(parsed.Loans);
        Assert.Contains("missing", parsed.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parser_RejectsMalformedRows_AndReturnsNoLoansAtAll()
    {
        var lines = new[]
        {
            LoanCsvParser.Header,
            "L001,Aigerim,Clean Code,2026-10-10,2026-10-15",
            "L002,Beck,SICP,12/10/2026,",
            "L003,,Dune,2026-10-05",
            "L004,Erlan,Algorithms,2026-09-01,not-a-date",
            "L001,Dana,Dune,2026-10-05,",
        };

        var parsed = LoanCsvParser.Parse(lines);

        Assert.False(parsed.IsValid);
        Assert.Empty(parsed.Loans);
        Assert.Equal(4, parsed.Errors.Length);
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 3") && e.Contains("DueDate"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 4") && e.Contains("expected 5 fields"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 5") && e.Contains("ReturnDate"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 6") && e.Contains("duplicate"));
    }

    [Fact]
    public void Parser_RejectsMissingHeader_AndEmptyInput()
    {
        Assert.False(LoanCsvParser.Parse(Array.Empty<string>()).IsValid);
        Assert.False(LoanCsvParser.Parse(new[] { "L001,Aigerim,Clean Code,2026-10-10,2026-10-15" }).IsValid);
    }

    [Fact]
    public void Parser_EmptyReturnDate_MeansStillOut_NotAnError()
    {
        var parsed = LoanCsvParser.Parse(new[] { LoanCsvParser.Header, "L003,Dana,Dune,2026-10-05," });

        Assert.True(parsed.IsValid);
        Assert.Null(Assert.Single(parsed.Loans).ReturnDate);
    }

    [Fact]
    public void Stages_RejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => FineStages.SelectOverdue(null!, AsOf));
        Assert.Throws<ArgumentNullException>(() =>
            FineStages.ApplyFines(ImmutableArray<OverdueLoan>.Empty, (Func<int, int>)null!));
    }

    [Fact]
    public void TwoPolicies_SameInput_GiveDifferentDocumentedResults()
    {
        var loans = SampleLoans();

        var standard = FinePipeline.Run(loans, AsOf, FinePolicies.Standard);
        var strict = FinePipeline.Run(loans, AsOf, FinePolicies.Strict);

        Assert.Equal(2050, standard.TotalFine);
        Assert.Equal(5100, strict.TotalFine);
        Assert.Equal(
            new[] { ("L004", 3000), ("L003", 1500), ("L001", 500), ("L006", 100) },
            Summary(strict));

        Assert.Equal(standard.Lines.Select(l => l.LoanId), strict.Lines.Select(l => l.LoanId));
        Assert.NotEqual(standard.Lines[0].Fine, strict.Lines[0].Fine);
    }

    [Fact]
    public void HigherOrderStage_InvokesThePolicyDelegate_OncePerOverdueLoan()
    {
        var overdue = FineStages.SelectOverdue(SampleLoans(), AsOf);
        var seenDays = new List<int>();

        var lines = FineStages.ApplyFines(overdue, days => { seenDays.Add(days); return days; });

        Assert.Equal(4, seenDays.Count);
        Assert.Equal(new[] { 5, 15, 49, 1 }, seenDays);
        Assert.Equal(new[] { 5, 15, 49, 1 }, lines.Select(l => l.Fine).ToArray());
    }

    [Fact]
    public void Stages_DoNotMutateCallerOwnedCollections()
    {
        var callerList = new List<Loan>(SampleLoans());
        var snapshot = callerList.ToArray();

        var overdue = FineStages.SelectOverdue(callerList, AsOf);
        var lines = FineStages.ApplyFines(overdue, FinePolicies.Standard);
        var overdueSnapshot = overdue.ToArray();
        var linesSnapshot = lines.ToArray();
        FineStages.BuildReport(lines);
        FinePipeline.Run(callerList, AsOf, FinePolicies.Strict);

        Assert.Equal(snapshot, callerList.ToArray());
        Assert.Equal(overdueSnapshot, overdue.ToArray());
        Assert.Equal(linesSnapshot, lines.ToArray());
    }

    [Fact]
    public void Stage1_SelectOverdue_ReturnsOnlyLateLoansWithDaysLate()
    {
        var overdue = FineStages.SelectOverdue(SampleLoans(), AsOf);

        Assert.Equal(
            new[] { ("L001", 5), ("L003", 15), ("L004", 49), ("L006", 1) },
            overdue.Select(o => (o.Loan.LoanId, o.DaysLate)).ToArray());
    }

    [Fact]
    public void Stage3_BuildReport_BreaksTiesByLoanId_AndIsDeterministic()
    {
        var lines = ImmutableArray.Create(
            new FineLine("L009", "M", "B", 2, 100),
            new FineLine("L002", "M", "B", 2, 100),
            new FineLine("L005", "M", "B", 9, 450));

        var first = FineStages.BuildReport(lines);
        var second = FineStages.BuildReport(lines);

        Assert.Equal(new[] { ("L005", 450), ("L002", 100), ("L009", 100) }, Summary(first));
        Assert.Equal(Summary(first), Summary(second));
        Assert.Equal(650, first.TotalFine);
    }

    [Fact]
    public void SamePolicyAndInput_AlwaysGiveSameOutput()
    {
        var loans = SampleLoans();

        var a = FinePipeline.Run(loans, AsOf, FinePolicies.Standard);
        var b = FinePipeline.Run(loans, AsOf, FinePolicies.Standard);

        Assert.Equal(Summary(a), Summary(b));
        Assert.Equal(a.TotalFine, b.TotalFine);
    }

    [Fact]
    public void PolicyLookup_KnownAndUnknownNames()
    {
        Assert.True(FinePolicies.TryGet("Strict", out var strict));
        Assert.Equal(100, strict(1));
        Assert.False(FinePolicies.TryGet("lenient", out _));
    }
}
