using System.Collections.Immutable;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public sealed class FinalRulesTests
{
    private static DateOnly D(string text) => DateOnly.Parse(text);

    private static Operation Reg(string member) => new Operation.RegisterMember(member);

    private static Operation Book(string book, int copies) => new Operation.AddBook(book, copies);

    private static Operation Out(string id, string member, string book, string date, int days) =>
        new Operation.Checkout(new CheckoutRequest(id, member, book, D(date), days));

    private static Operation In(string id, string date) => new Operation.Return(new ReturnRequest(id, D(date)));

    private static Operation Renew(string id, string date) => new Operation.Renew(new RenewRequest(id, D(date)));

    private static Operation Reserve(string member, string book) => new Operation.Reserve(member, book);

    private static Operation Pay(string member, int amount) => new Operation.PayFine(member, amount);

    private static LibraryState Run(params Operation[] operations) =>
        LibraryWorkflow.Run(LibraryState.Empty, operations, FinePolicies.Standard).FinalState;

    private static StepResult Step(LibraryState state, Operation operation) =>
        LibraryWorkflow.Apply(state, operation, FinePolicies.Standard);

    private static LibraryState OneLoan() =>
        Run(Reg("A"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14));

    private static LibraryState Queue() =>
        Run(Reg("A"), Reg("B"), Reg("C"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14), Reserve("B", "Dune"));

    private static IEnumerable<T> Counting<T>(IEnumerable<T> source, Action onItem)
    {
        foreach (var item in source)
        {
            onItem();
            yield return item;
        }
    }

    private static Loan LoanOf(string id, string due, string? returned = null) =>
        new(id, "Member", "Book", D(due), returned is null ? null : D(returned));

    [Fact]
    public void Renew_ExtendsTheDueDateByAWeekAndCountsTheRenewal()
    {
        var state = OneLoan();

        var step = Step(state, Renew("L1", "2026-11-10"));

        Assert.True(step.Succeeded);
        Assert.Equal("Loan L1 renewed, new due date 2026-11-22.", step.Message);
        var active = Assert.IsType<LoanState.Active>(step.State.Loans["L1"]);
        Assert.Equal(1, active.Renewals);
        Assert.Equal(D("2026-11-22"), active.DueDate);
        Assert.Equal(D("2026-11-15"), Assert.IsType<LoanState.Active>(state.Loans["L1"]).DueDate);
    }

    [Fact]
    public void Renew_OnTheDueDateIsAllowedAndTheDayAfterIsOverdue()
    {
        var state = OneLoan();

        Assert.True(Step(state, Renew("L1", "2026-11-15")).Succeeded);

        var late = Step(state, Renew("L1", "2026-11-16"));
        Assert.False(late.Succeeded);
        Assert.Equal(ErrorCode.LoanOverdue, late.Error!.Code);
        Assert.Same(state, late.State);
    }

    [Fact]
    public void Renew_ThirdRenewalIsRejected()
    {
        var state = Run(Reg("A"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14),
            Renew("L1", "2026-11-10"), Renew("L1", "2026-11-20"));

        var third = Step(state, Renew("L1", "2026-11-25"));

        Assert.Equal(2, Assert.IsType<LoanState.Active>(state.Loans["L1"]).Renewals);
        Assert.Equal(D("2026-11-29"), Assert.IsType<LoanState.Active>(state.Loans["L1"]).DueDate);
        Assert.Equal(ErrorCode.RenewalLimitReached, third.Error!.Code);
    }

    [Fact]
    public void Renew_IsRejectedWhileAnotherMemberWaitsForTheBook()
    {
        var step = Step(Queue(), Renew("L1", "2026-11-10"));

        Assert.False(step.Succeeded);
        Assert.Equal(ErrorCode.BookReserved, step.Error!.Code);
    }

    [Fact]
    public void Renew_ReturnedAndUnknownLoansAreRejectedWithDifferentCodes()
    {
        var returned = Run(Reg("A"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14), In("L1", "2026-11-05"));

        Assert.Equal(ErrorCode.AlreadyReturned, Step(returned, Renew("L1", "2026-11-06")).Error!.Code);
        Assert.Equal(ErrorCode.LoanNotFound, Step(returned, Renew("L9", "2026-11-06")).Error!.Code);
    }

    [Fact]
    public void Reserve_IsOnlyPossibleWhenNoCopyIsAvailable()
    {
        var free = Run(Reg("A"), Book("Dune", 1));

        var step = Step(free, Reserve("A", "Dune"));

        Assert.Equal(ErrorCode.CopyAvailable, step.Error!.Code);
        Assert.Empty(step.State.Reservations);
    }

    [Fact]
    public void Reserve_QueuesMembersInOrderAndRejectsDuplicates()
    {
        var state = Queue();

        var second = Step(state, Reserve("C", "Dune"));
        var duplicate = Step(state, Reserve("B", "Dune"));

        Assert.Equal("Member C reserved 'Dune', queue position 2.", second.Message);
        Assert.Equal(new[] { "B", "C" }, second.State.QueueFor("Dune"));
        Assert.Equal(ErrorCode.AlreadyReserved, duplicate.Error!.Code);
        Assert.Equal(new[] { "B" }, state.QueueFor("Dune"));
    }

    [Fact]
    public void Reserve_UnknownMemberStopsBeforeTheBookIsChecked()
    {
        var state = Run(Reg("A"), Book("Dune", 1));

        var unknownBoth = Step(state, Reserve("Nobody", "Ghost"));
        var unknownBook = Step(state, Reserve("A", "Ghost"));

        Assert.Equal(ErrorCode.MemberNotFound, unknownBoth.Error!.Code);
        Assert.Equal(ErrorCode.BookNotFound, unknownBook.Error!.Code);
    }

    [Fact]
    public void Reservation_GivesTheReturnedCopyToTheFirstMemberInTheQueue()
    {
        var returned = Step(Queue(), In("L1", "2026-11-05")).State;

        var other = Step(returned, Out("L2", "C", "Dune", "2026-11-06", 7));
        var head = Step(returned, Out("L3", "B", "Dune", "2026-11-06", 7));

        Assert.Equal(ErrorCode.BookReserved, other.Error!.Code);
        Assert.Equal("REJECTED (BookReserved): Book 'Dune' is reserved for 'B'.", other.Message);
        Assert.True(head.Succeeded);
        Assert.Empty(head.State.QueueFor("Dune"));
        Assert.Empty(head.State.Reservations);
    }

    [Fact]
    public void Return_LateChargesTheFineFromThePolicyAndAddsItToTheBalance()
    {
        var step = Step(OneLoan(), In("L1", "2026-11-20"));

        Assert.True(step.Succeeded);
        Assert.Equal("Loan L1 returned on 2026-11-20. Fine charged: 250 KZT.", step.Message);
        Assert.Equal(250, step.State.BalanceOf("A"));
        Assert.Equal(250, Assert.IsType<LoanState.Returned>(step.State.Loans["L1"]).Fine);
    }

    [Fact]
    public void Return_OnTimeOrWithoutAPolicyChargesNothing()
    {
        var onTime = Step(OneLoan(), In("L1", "2026-11-15"));
        var noPolicy = LibraryWorkflow.Apply(OneLoan(), In("L1", "2026-11-20"));

        Assert.Equal("Loan L1 returned on 2026-11-15.", onTime.Message);
        Assert.Empty(onTime.State.Balances);
        Assert.Equal("Loan L1 returned on 2026-11-20.", noPolicy.Message);
        Assert.Empty(noPolicy.State.Balances);
    }

    [Fact]
    public void Return_RenewedLoanIsFinedFromTheRenewedDueDate()
    {
        var state = Run(Reg("A"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14), Renew("L1", "2026-11-10"));

        var step = Step(state, In("L1", "2026-11-24"));

        Assert.Equal(100, step.State.BalanceOf("A"));
    }

    [Fact]
    public void Policy_TheSameReturnBlocksTheMemberOnlyUnderTheStrictPolicy()
    {
        var operations = new[]
        {
            Reg("A"), Book("Dune", 1), Book("Atlas", 1),
            Out("L1", "A", "Dune", "2026-11-01", 14),
            In("L1", "2026-11-21"),
            Out("L2", "A", "Atlas", "2026-11-21", 7)
        };

        var standard = LibraryWorkflow.Run(LibraryState.Empty, operations, FinePolicies.Standard);
        var strict = LibraryWorkflow.Run(LibraryState.Empty, operations, FinePolicies.Strict);

        Assert.True(standard.Steps[5].Succeeded);
        Assert.Equal(300, standard.FinalState.BalanceOf("A"));
        Assert.False(strict.Steps[5].Succeeded);
        Assert.Equal(ErrorCode.MemberBlocked, strict.Steps[5].Error!.Code);
        Assert.Equal(600, strict.FinalState.BalanceOf("A"));
    }

    [Fact]
    public void Blocking_ABalanceOfExactlyTheLimitIsAllowedAndOneKztMoreIsNot()
    {
        LibraryState Returned(string returnDate) =>
            Run(Reg("A"), Book("Dune", 1), Book("Atlas", 1), Out("L1", "A", "Dune", "2026-11-01", 14), In("L1", returnDate));

        var atLimit = Returned("2026-11-25");
        var above = Returned("2026-11-26");

        Assert.Equal(500, atLimit.BalanceOf("A"));
        Assert.True(Step(atLimit, Out("L2", "A", "Atlas", "2026-11-26", 7)).Succeeded);
        Assert.Equal(550, above.BalanceOf("A"));
        var blocked = Step(above, Out("L2", "A", "Atlas", "2026-11-26", 7));
        Assert.Equal(ErrorCode.MemberBlocked, blocked.Error!.Code);
        Assert.Equal("REJECTED (MemberBlocked): Member 'A' owes 550 KZT, above the limit of 500 KZT.", blocked.Message);
    }

    [Fact]
    public void Pay_ReducesTheBalanceAndUnblocksTheMember()
    {
        var state = Run(Reg("A"), Book("Dune", 1), Book("Atlas", 1), Out("L1", "A", "Dune", "2026-11-01", 14), In("L1", "2026-11-26"));

        var paid = Step(state, Pay("A", 100));

        Assert.Equal("Member A paid 100 KZT, remaining balance 450 KZT.", paid.Message);
        Assert.Equal(450, paid.State.BalanceOf("A"));
        Assert.True(Step(paid.State, Out("L2", "A", "Atlas", "2026-11-26", 7)).Succeeded);
    }

    [Fact]
    public void Pay_FullAmountRemovesTheBalanceEntry()
    {
        var state = Run(Reg("A"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14), In("L1", "2026-11-26"));

        var paid = Step(state, Pay("A", 550));

        Assert.True(paid.Succeeded);
        Assert.Empty(paid.State.Balances);
        Assert.Equal(0, paid.State.BalanceOf("A"));
    }

    [Fact]
    public void Pay_InvalidAmountsAndUnknownMembersAreRejected()
    {
        var state = Run(Reg("A"), Book("Dune", 1), Out("L1", "A", "Dune", "2026-11-01", 14), In("L1", "2026-11-26"));

        Assert.Equal(ErrorCode.InvalidPayment, Step(state, Pay("A", 0)).Error!.Code);
        Assert.Equal(ErrorCode.InvalidPayment, Step(state, Pay("A", -5)).Error!.Code);
        Assert.Equal(ErrorCode.InvalidPayment, Step(state, Pay("A", 551)).Error!.Code);
        Assert.Equal(ErrorCode.MemberNotFound, Step(state, Pay("Nobody", 10)).Error!.Code);
        Assert.Equal(550, state.BalanceOf("A"));
    }

    [Fact]
    public void FailedCheckoutReportsOnlyTheFirstFailedStage()
    {
        var step = Step(LibraryState.Empty, Out("L1", "Nobody", "Ghost", "2026-11-01", 7));

        Assert.Equal("REJECTED (MemberNotFound): Member 'Nobody' is not registered.", step.Message);
    }

    [Fact]
    public void Snapshot_RoundTripKeepsLoansRenewalsReservationsAndBalances()
    {
        var state = Run(
            Reg("A"), Reg("B"), Reg("C"), Book("Dune", 1), Book("Atlas", 2),
            Out("L1", "A", "Dune", "2026-11-01", 14), Renew("L1", "2026-11-10"),
            Out("L2", "B", "Atlas", "2026-11-02", 7), In("L2", "2026-11-20"),
            Reserve("B", "Dune"), Reserve("C", "Dune"));
        var snapshot = state.ToSnapshot();

        var restored = Outcome.Value(LibraryState.Restore(snapshot));
        var again = restored.ToSnapshot();

        Assert.Equal(snapshot.Members.ToArray(), again.Members.ToArray());
        Assert.Equal(snapshot.Books.ToArray(), again.Books.ToArray());
        Assert.Equal(snapshot.Loans.ToArray(), again.Loans.ToArray());
        Assert.Equal(snapshot.Balances.ToArray(), again.Balances.ToArray());
        Assert.Equal(new[] { "B", "C" }, restored.QueueFor("Dune"));
        Assert.Equal(550, restored.BalanceOf("B"));
        var l1 = Assert.IsType<LoanState.Active>(restored.Loans["L1"]);
        Assert.Equal(1, l1.Renewals);
        Assert.Equal(D("2026-11-22"), l1.DueDate);
        Assert.Equal(550, Assert.IsType<LoanState.Returned>(restored.Loans["L2"]).Fine);
    }

    [Fact]
    public void Restore_ReportsEveryInconsistencyInsteadOfStoppingAtTheFirst()
    {
        var bad = new StateSnapshot(
            ImmutableArray.Create("A", "A"),
            ImmutableArray.Create(new BookSnapshot("Dune", 0)),
            ImmutableArray.Create(new LoanSnapshot("L1", "Nobody", "Atlas", D("2026-11-01"), 40, 5, D("2026-11-02"), null, -1)),
            ImmutableArray.Create(new ReservationSnapshot("Ghost", ImmutableArray.Create("A"))),
            ImmutableArray.Create(new BalanceSnapshot("Nobody", 5)));

        var error = Outcome.Error(LibraryState.Restore(bad));

        Assert.Equal(ErrorCode.MalformedContent, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains("Member 'A' is listed twice.", error.Message);
        Assert.Contains("Book 'Dune' has 0 copies.", error.Message);
        Assert.Contains("unknown member 'Nobody'", error.Message);
        Assert.Contains("unknown book 'Atlas'", error.Message);
        Assert.Contains("invalid period of 40 days", error.Message);
        Assert.Contains("invalid renewal count 5", error.Message);
        Assert.Contains("negative fine", error.Message);
        Assert.Contains("due date that does not match", error.Message);
        Assert.Contains("unknown book 'Ghost'", error.Message);
        Assert.Contains("Balance refers to unknown member 'Nobody'", error.Message);
    }

    [Fact]
    public void Restore_RejectsOverbookedStockAndMissingSections()
    {
        var overbooked = new StateSnapshot(
            ImmutableArray.Create("A", "B"),
            ImmutableArray.Create(new BookSnapshot("Dune", 1)),
            ImmutableArray.Create(
                new LoanSnapshot("L1", "A", "Dune", D("2026-11-01"), 7, 0, D("2026-11-08"), null, 0),
                new LoanSnapshot("L2", "B", "Dune", D("2026-11-01"), 7, 0, D("2026-11-08"), null, 0)),
            ImmutableArray<ReservationSnapshot>.Empty,
            ImmutableArray<BalanceSnapshot>.Empty);

        Assert.Contains("lent more times than it has copies", Outcome.Error(LibraryState.Restore(overbooked)).Message);
        Assert.Contains(
            "State must contain",
            Outcome.Error(LibraryState.Restore(new StateSnapshot(default, default, default, default, default))).Message);
    }

    [Fact]
    public void Steps_NothingRunsUntilIterationAndEarlyStopReadsOnlyWhatIsNeeded()
    {
        var pulled = 0;
        var source = Counting(new[] { Reg("A"), Reg("B"), Reg("C") }, () => pulled++);

        var steps = LibraryWorkflow.Steps(LibraryState.Empty, source, FinePolicies.Standard);

        Assert.Equal(0, pulled);
        var first = steps.First();
        Assert.Equal(1, pulled);
        Assert.True(first.Succeeded);
        Assert.Empty(LibraryState.Empty.Members);
    }

    [Fact]
    public void Steps_RunMaterialisesOnceAndEnumeratingTheLazySequenceAgainRepeatsTheWork()
    {
        var pulled = 0;
        var source = Counting(new[] { Reg("A"), Reg("B"), Reg("C") }, () => pulled++);
        var steps = LibraryWorkflow.Steps(LibraryState.Empty, source, FinePolicies.Standard);

        var snapshot = steps.ToImmutableArray();
        var total = snapshot.Count(s => s.Succeeded);
        var again = snapshot.Count(s => s.Succeeded);

        Assert.Equal(3, pulled);
        Assert.Equal(3, total);
        Assert.Equal(3, again);
        steps.ToArray();
        Assert.Equal(6, pulled);
    }

    [Fact]
    public void Run_EmptyScriptReturnsTheInitialStateItself()
    {
        var run = LibraryWorkflow.Run(LibraryState.Empty, Array.Empty<Operation>(), FinePolicies.Standard);

        Assert.Empty(run.Steps);
        Assert.Same(LibraryState.Empty, run.FinalState);
    }

    [Fact]
    public void Parser_UnderstandsRenewReserveAndPay()
    {
        var parsed = OperationParser.Parse(new[] { "RENEW,L1,2026-11-10", "RESERVE,Dana,Dune", "pay,Dana,100" });

        Assert.True(parsed.IsValid);
        Assert.Equal(
            new Operation[]
            {
                new Operation.Renew(new RenewRequest("L1", D("2026-11-10"))),
                new Operation.Reserve("Dana", "Dune"),
                new Operation.PayFine("Dana", 100)
            },
            parsed.Operations);
    }

    [Fact]
    public void Parser_ReportsWrongFieldCountsBadNumbersAndBadDates()
    {
        var parsed = OperationParser.Parse(new[] { "RENEW,L1", "PAY,Dana,ten", "RENEW,L1,11/10/2026", "RESERVE,Dana" });

        Assert.False(parsed.IsValid);
        Assert.Equal(
            new[]
            {
                "Line 1: RENEW expects 3 fields but found 2.",
                "Line 2: Amount 'ten' is not a whole number.",
                "Line 3: Date '11/10/2026' is not a valid yyyy-MM-dd date.",
                "Line 4: RESERVE expects 3 fields but found 2."
            },
            parsed.Errors);
    }

    [Fact]
    public void ParseLine_KeepsTheGivenLineNumberAndRejectsBlankLines()
    {
        Assert.Equal(new Operation.PayFine("Dana", 5), Outcome.Value(OperationParser.ParseLine("PAY,Dana,5", 3)));
        Assert.Contains("Line 7: Amount 'x'", Outcome.Error(OperationParser.ParseLine("PAY,Dana,x", 7)).Message);
        Assert.Contains("Line 3: no operation found.", Outcome.Error(OperationParser.ParseLine("   ", 3)).Message);
    }

    [Fact]
    public void Build_PolicyIsAHigherOrderFunctionThatChangesTheReportAmounts()
    {
        var loans = new[] { LoanOf("L1", "2026-10-21"), LoanOf("L2", "2026-10-26", "2026-10-31"), LoanOf("L3", "2026-11-05") }
            .ToImmutableArray();
        var asOf = D("2026-10-31");

        var standard = FineWorkflow.Build(loans, asOf, FinePolicies.Standard, Scoring.Reminder, 0);
        var strict = FineWorkflow.Build(loans, asOf, FinePolicies.Strict, Scoring.Reminder, 0);
        var filtered = FineWorkflow.Build(loans, asOf, FinePolicies.Standard, Scoring.Reminder, 300);

        Assert.Equal(750, standard.Fines.TotalFine);
        Assert.Equal(1500, strict.Fines.TotalFine);
        Assert.Equal(new[] { "L1" }, filtered.Fines.Lines.Select(l => l.LoanId));
        Assert.Equal(750, standard.Members[0].TotalFine);
    }

    [Fact]
    public void Build_NothingOverdueAndUninitialisedInputAreHandled()
    {
        var none = FineWorkflow.Build(
            ImmutableArray.Create(LoanOf("L3", "2026-11-05")), D("2026-10-31"), FinePolicies.Standard, Scoring.Reminder, 0);

        Assert.Equal(0, none.Fines.Count);
        Assert.Empty(none.Members);
        Assert.Throws<ArgumentException>(() =>
            FineWorkflow.Build(default, D("2026-10-31"), FinePolicies.Standard, Scoring.Reminder, 0));
    }

    [Fact]
    public void UnpaidBalances_AreOrderedByAmountThenName()
    {
        var balances = ImmutableDictionary<string, int>.Empty
            .Add("Dana", 250).Add("Beck", 250).Add("Aigerim", 450).Add("Erlan", 0);

        var lines = LoanReports.UnpaidBalances(balances);

        Assert.Equal(new[] { "Aigerim", "Beck", "Dana" }, lines.Select(l => l.Member));
        Assert.Contains("No unpaid balances.", ReportFormat.BalanceLines(LoanReports.UnpaidBalances(ImmutableDictionary<string, int>.Empty)));
    }
}
