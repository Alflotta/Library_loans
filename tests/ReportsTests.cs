using System.Collections.Immutable;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public sealed class ReportsTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 31);

    private static FineLine Line(string id, string member, string book, int days, int fine) =>
        new(id, member, book, days, fine);

    private static ImmutableArray<FineLine> Lines() => ImmutableArray.Create(
        Line("L207", "Erlan", "Algorithms", 2, 100),
        Line("L203", "Beck", "SICP", 10, 500),
        Line("L206", "Dana", "Dune", 20, 1000),
        Line("L201", "Aigerim", "Clean Code", 10, 500),
        Line("L210", "Farida", "SICP", 15, 750),
        Line("L202", "Aigerim", "Dune", 5, 250),
        Line("L205", "Dana", "Atlas", 60, 1000));

    private static Loan LoanOf(string id, string due, string? returned = null) =>
        new(id, "Member", "Book", DateOnly.Parse(due), returned is null ? null : DateOnly.Parse(returned));

    private static IEnumerable<T> Counting<T>(IEnumerable<T> source, Action onItem)
    {
        foreach (var item in source)
        {
            onItem();
            yield return item;
        }
    }

    [Fact]
    public void LargeFines_FilterOrderAndTotalAreCorrect()
    {
        var report = LoanReports.LargeFines(Lines(), 250);

        Assert.Equal(new[] { "L205", "L206", "L210", "L201", "L203", "L202" }, report.Lines.Select(l => l.LoanId));
        Assert.Equal(4000, report.TotalFine);
        Assert.Equal(6, report.Count);
    }

    [Fact]
    public void LargeFines_MinimumZeroKeepsEveryLine()
    {
        var report = LoanReports.LargeFines(Lines(), 0);

        Assert.Equal(7, report.Count);
        Assert.Equal(4100, report.TotalFine);
    }

    [Fact]
    public void LargeFines_EmptyInputGivesEmptyReport()
    {
        var report = LoanReports.LargeFines(ImmutableArray<FineLine>.Empty, 250);

        Assert.Equal(0, report.Count);
        Assert.Equal(0, report.TotalFine);
    }

    [Fact]
    public void LargeFines_NoMatchingLinesGivesEmptyReport()
    {
        var report = LoanReports.LargeFines(Lines(), 5000);

        Assert.Equal(0, report.Count);
        Assert.Equal(0, report.TotalFine);
    }

    [Fact]
    public void LargeFines_EqualFinesAreOrderedByLoanId()
    {
        var reversed = Lines().Reverse().ToImmutableArray();

        var ids = LoanReports.LargeFines(reversed, 250).Lines.Select(l => l.LoanId).ToArray();

        Assert.True(Array.IndexOf(ids, "L205") < Array.IndexOf(ids, "L206"));
        Assert.True(Array.IndexOf(ids, "L201") < Array.IndexOf(ids, "L203"));
    }

    [Fact]
    public void LargeFines_UninitialisedInputIsRejected()
    {
        Assert.Throws<ArgumentException>(() => LoanReports.LargeFines(default, 250));
    }

    [Fact]
    public void MemberSummaries_GroupAndAggregateCorrectly()
    {
        var summaries = LoanReports.MemberSummaries(Lines(), Scoring.Reminder);

        Assert.Equal(
            new[]
            {
                new MemberSummary("Dana", 2, 2000, 60, 100),
                new MemberSummary("Aigerim", 2, 750, 10, 22),
                new MemberSummary("Farida", 1, 750, 15, 22),
                new MemberSummary("Beck", 1, 500, 10, 15),
                new MemberSummary("Erlan", 1, 100, 2, 3)
            },
            summaries.ToArray());
    }

    [Fact]
    public void MemberSummaries_EqualTotalsAreOrderedByMemberName()
    {
        var names = LoanReports.MemberSummaries(Lines().Reverse().ToImmutableArray(), Scoring.Reminder)
            .Select(m => m.Member)
            .ToArray();

        Assert.Equal(new[] { "Dana", "Aigerim", "Farida", "Beck", "Erlan" }, names);
    }

    [Fact]
    public void MemberSummaries_EmptyInputGivesNoRows()
    {
        Assert.Empty(LoanReports.MemberSummaries(ImmutableArray<FineLine>.Empty, Scoring.Reminder));
    }

    [Fact]
    public void Scoring_TwoPartiallyAppliedFunctionsDifferOnTheSameLine()
    {
        var line = Line("L205", "Dana", "Atlas", 60, 1000);
        var shortLine = Line("L202", "Aigerim", "Dune", 5, 250);

        Assert.Equal(70, Scoring.Reminder(line));
        Assert.Equal(320, Scoring.Escalation(line));
        Assert.Equal(7, Scoring.Reminder(shortLine));
        Assert.Equal(30, Scoring.Escalation(shortLine));
    }

    [Fact]
    public void Scoring_CurriedFormGivesSameResultAfterEachSuccessiveCall()
    {
        var line = Line("L205", "Dana", "Atlas", 60, 1000);

        var withDayWeight = Scoring.Curried(5);
        var configured = withDayWeight(50);

        Assert.Equal(320, configured(line));
        Assert.Equal(Scoring.Escalation(line), configured(line));
        Assert.Equal(Scoring.CreateScore(Scoring.ReminderConfig)(line), Scoring.Curried(1)(100)(line));
    }

    [Fact]
    public void Scoring_ConfigurationChangesMemberScoresWithoutChangingFines()
    {
        var reminder = LoanReports.MemberSummaries(Lines(), Scoring.Reminder);
        var escalation = LoanReports.MemberSummaries(Lines(), Scoring.Escalation);

        Assert.Equal(reminder.Select(m => m.TotalFine), escalation.Select(m => m.TotalFine));
        Assert.Equal(100, reminder[0].Score);
        Assert.Equal(440, escalation[0].Score);
    }

    [Fact]
    public void Scoring_InvalidConfigurationIsRejectedAndUnknownNameIsNotFound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.CreateScore(new ScoreConfig(-1, 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Scoring.CreateScore(new ScoreConfig(1, 0)));
        Assert.False(Scoring.TryGet("gentle", out _));
        Assert.True(Scoring.TryGet(" Escalation ", out var found));
        Assert.Equal(Scoring.Escalation(Line("L1", "A", "B", 1, 50)), found(Line("L1", "A", "B", 1, 50)));
    }

    [Fact]
    public void WhereOverdue_KeepsOnlyOverdueLoansWithTheirDays()
    {
        var loans = new[]
        {
            LoanOf("L1", "2026-10-21"),
            LoanOf("L2", "2026-10-26", "2026-10-31"),
            LoanOf("L3", "2026-10-31"),
            LoanOf("L4", "2026-11-05"),
            LoanOf("L5", "2026-10-26", "2026-10-26")
        };

        var overdue = loans.WhereOverdue(AsOf).ToArray();

        Assert.Equal(new[] { "L1", "L2" }, overdue.Select(o => o.Loan.LoanId));
        Assert.Equal(new[] { 10, 5 }, overdue.Select(o => o.DaysLate));
    }

    [Fact]
    public void WhereOverdue_DoesNotTouchTheSourceUntilEnumerated()
    {
        var touched = 0;
        var source = Counting(new[] { LoanOf("L1", "2026-10-21"), LoanOf("L2", "2026-11-05") }, () => touched++);

        var query = source.WhereOverdue(AsOf);

        Assert.Equal(0, touched);
        Assert.Single(query.ToArray());
        Assert.Equal(2, touched);
    }

    [Fact]
    public void WhereOverdue_EmptySourceAndNullSource()
    {
        Assert.Empty(Array.Empty<Loan>().WhereOverdue(AsOf));
        Assert.Throws<ArgumentNullException>(() => ((IEnumerable<Loan>)null!).WhereOverdue(AsOf));
    }

    [Fact]
    public void CollectResults_AllSuccessesGiveOneArrayInOrder()
    {
        var results = new[] { Result.Ok(1), Result.Ok(2), Result.Ok(3) };

        Assert.Equal(new[] { 1, 2, 3 }, Outcome.Value(results.CollectResults()));
        Assert.Empty(Outcome.Value(Array.Empty<Result<int>>().CollectResults()));
    }

    [Fact]
    public void CollectResults_StopsAtTheFirstFailureAndDoesNotReadFurther()
    {
        var pulled = 0;
        var results = Counting(
            new[]
            {
                Result.Ok(1),
                Result.Fail<int>(ErrorCode.MalformedContent, "second"),
                Result.Fail<int>(ErrorCode.FileUnreadable, "third"),
                Result.Ok(4)
            },
            () => pulled++);

        var error = Outcome.Error(results.CollectResults());

        Assert.Equal(ErrorCode.MalformedContent, error.Code);
        Assert.Equal("second", error.Message);
        Assert.Equal(2, pulled);
    }

    [Fact]
    public void MapError_ChangesOnlyFailuresAndKeepsSuccesses()
    {
        var failed = Result.Fail<int>(ErrorCode.FileNotFound, "x").MapError(e => e with { Message = "y" });
        var succeeded = Result.Ok(5).MapError(e => e with { Message = "y" });

        Assert.Equal("y", Outcome.Error(failed).Message);
        Assert.Equal(ErrorCode.FileNotFound, Outcome.Error(failed).Code);
        Assert.Equal(5, Outcome.Value(succeeded));
    }

    [Fact]
    public void ReportFormat_PrintsBothReportsAndTotals()
    {
        var bundle = new ReportBundle(
            AsOf,
            250,
            LoanReports.LargeFines(Lines(), 250),
            LoanReports.MemberSummaries(Lines(), Scoring.Reminder));

        var lines = ReportFormat.ToLines(bundle);

        Assert.Equal("Fine reports as of 2026-10-31", lines[0]);
        Assert.StartsWith("L205", lines[2]);
        Assert.Contains("1000 KZT", lines[2]);
        Assert.Equal("Total: 4000 KZT (6 loans)", lines[8]);
        Assert.StartsWith("Dana", lines[10]);
        Assert.Contains("score  100", lines[10]);
        Assert.Equal(15, lines.Length);
    }

    [Fact]
    public void ReportFormat_EmptyReportsSayThatNothingMatched()
    {
        var bundle = new ReportBundle(
            AsOf,
            250,
            LoanReports.LargeFines(ImmutableArray<FineLine>.Empty, 250),
            ImmutableArray<MemberSummary>.Empty);

        var lines = ReportFormat.ToLines(bundle);

        Assert.Contains("No fines at or above 250 KZT.", lines);
        Assert.Contains("No member has overdue loans.", lines);
    }
}
