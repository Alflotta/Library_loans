using System.Collections.Immutable;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public class LibraryWorkflowTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 20);

    private static readonly string[] Script =
    {
        "MEMBER,Aigerim",
        "MEMBER,Beck",
        "MEMBER,Dana",
        "MEMBER,Beck",
        "BOOK,Clean Code,1",
        "BOOK,Dune,2",
        "BOOK,SICP,1",
        "CHECKOUT,L101,Aigerim,Clean Code,2026-10-01,14",
        "CHECKOUT,L102,Beck,Clean Code,2026-10-02,14",
        "CHECKOUT,L103,Beck,Dune,2026-10-02,30",
        "CHECKOUT,L104,Dana,SICP,2026-10-03,31",
        "CHECKOUT,L105,Zhan,Dune,2026-10-03,7",
        "CHECKOUT,L107,Dana,SICP,2026-10-01,7",
        "RETURN,L103,2026-10-01",
        "RETURN,L101,2026-10-20",
        "RETURN,L101,2026-10-21",
        "RETURN,L999,2026-10-20",
        "CHECKOUT,L106,Aigerim,Clean Code,2026-10-20,14",
    };

    private static LibraryState Applied(TransitionOutcome outcome) =>
        Assert.IsType<TransitionOutcome.Applied>(outcome).State;

    private static LibraryState Seed()
    {
        var state = LibraryState.Empty;
        state = Applied(state.RegisterMember("Aigerim"));
        state = Applied(state.RegisterMember("Beck"));
        state = Applied(state.AddBook("Clean Code", 1));
        state = Applied(state.AddBook("Dune", 2));
        return state;
    }

    private static CheckoutRequest Request(string loanId, string member, string book, string date, int days) =>
        new(loanId, member, book, DateOnly.Parse(date), days);

    private static ValidatedCheckout Valid(CheckoutRequest request, LibraryState state) =>
        Assert.IsType<CheckoutOutcome.Valid>(ValidatedCheckout.Create(request, state)).Value;

    private static ImmutableArray<string> Reasons(CheckoutRequest request, LibraryState state) =>
        Assert.IsType<CheckoutOutcome.Invalid>(ValidatedCheckout.Create(request, state)).Reasons;

    private static LibraryState Issue(LibraryState state, CheckoutRequest request) =>
        Applied(state.Issue(Valid(request, state)));

    [Fact]
    public void Factory_ValidRequest_CreatesUsableValidatedCheckout()
    {
        var checkout = Valid(Request("L1", "Aigerim", "Clean Code", "2026-10-01", 14), Seed());

        Assert.Equal("L1", checkout.LoanId);
        Assert.Equal("Aigerim", checkout.MemberId);
        Assert.Equal("Clean Code", checkout.BookId);
        Assert.Equal(14, checkout.Days);
        Assert.Equal(new DateOnly(2026, 10, 15), checkout.DueDate);
    }

    [Fact]
    public void Factory_RejectsInvalidDays_AndAcceptsBoundaryDays()
    {
        var state = Seed();

        Valid(Request("L1", "Aigerim", "Dune", "2026-10-01", 1), state);
        Valid(Request("L2", "Aigerim", "Dune", "2026-10-01", 30), state);

        var zero = Reasons(Request("L3", "Aigerim", "Dune", "2026-10-01", 0), state);
        var over = Reasons(Request("L4", "Aigerim", "Dune", "2026-10-01", 31), state);

        Assert.Single(zero);
        Assert.Contains("between 1 and 30", zero[0]);
        Assert.Single(over);
        Assert.Contains("got 31", over[0]);
    }

    [Fact]
    public void Factory_RejectsUnknownMemberUnknownBookAndEmptyId()
    {
        var state = Seed();

        var unknownMember = Reasons(Request("L1", "Nobody", "Dune", "2026-10-01", 7), state);
        var unknownBook = Reasons(Request("L1", "Aigerim", "Missing", "2026-10-01", 7), state);
        var emptyId = Reasons(Request("  ", "Aigerim", "Dune", "2026-10-01", 7), state);
        var everything = Reasons(Request("L1", "Nobody", "Missing", "2026-10-01", 0), state);

        Assert.Single(unknownMember);
        Assert.Contains("'Nobody' is not registered", unknownMember[0]);
        Assert.Single(unknownBook);
        Assert.Contains("'Missing' is not in the catalog", unknownBook[0]);
        Assert.Single(emptyId);
        Assert.Contains("Loan id is empty", emptyId[0]);
        Assert.Equal(3, everything.Length);
    }

    [Fact]
    public void Factory_RejectsWhenNoCopyIsAvailable()
    {
        var state = Issue(Seed(), Request("L1", "Aigerim", "Clean Code", "2026-10-01", 14));

        var reasons = Reasons(Request("L2", "Beck", "Clean Code", "2026-10-02", 14), state);

        Assert.Single(reasons);
        Assert.Contains("No copy of 'Clean Code'", reasons[0]);
    }

    [Fact]
    public void Factory_EnforcesMemberLoanLimit_AtBoundary()
    {
        var state = Applied(Seed().AddBook("Hobbit", 5));
        state = Issue(state, Request("L1", "Beck", "Hobbit", "2026-10-01", 7));
        state = Issue(state, Request("L2", "Beck", "Hobbit", "2026-10-01", 7));
        state = Issue(state, Request("L3", "Beck", "Hobbit", "2026-10-01", 7));

        var reasons = Reasons(Request("L4", "Beck", "Hobbit", "2026-10-01", 7), state);

        Assert.Equal(3, state.ActiveLoanCount("Beck"));
        Assert.Single(reasons);
        Assert.Contains("maximum 3", reasons[0]);
    }

    [Fact]
    public void ValidatedValuesAndStates_HaveNoPublicConstructors()
    {
        Assert.Empty(typeof(ValidatedCheckout).GetConstructors());
        Assert.Empty(typeof(LoanState.Active).GetConstructors());
        Assert.Empty(typeof(LoanState.Returned).GetConstructors());
    }

    [Fact]
    public void DistinctCases_AreReachableAndMatched()
    {
        var state = Seed();
        var good = Request("L1", "Aigerim", "Dune", "2026-10-01", 14);
        var bad = Request("L2", "Nobody", "Dune", "2026-10-01", 14);

        Assert.IsType<CheckoutOutcome.Valid>(ValidatedCheckout.Create(good, state));
        Assert.IsType<CheckoutOutcome.Invalid>(ValidatedCheckout.Create(bad, state));

        var issued = LibraryWorkflow.Apply(state, new Operation.Checkout(good));
        var refused = LibraryWorkflow.Apply(state, new Operation.Checkout(bad));

        Assert.True(issued.Succeeded);
        Assert.StartsWith("Loan L1 issued", issued.Message);
        Assert.False(refused.Succeeded);
        Assert.StartsWith("REJECTED: invalid checkout", refused.Message);

        var active = Assert.IsType<LoanState.Active>(issued.State.Loans["L1"]);
        Assert.Null(active.ToLoan().ReturnDate);

        var returnDate = new DateOnly(2026, 10, 10);
        var returned = LibraryWorkflow.Apply(issued.State, new Operation.Return(new ReturnRequest("L1", returnDate)));
        var returnedState = Assert.IsType<LoanState.Returned>(returned.State.Loans["L1"]);

        Assert.Equal(returnDate, returnedState.ReturnedOn);
        Assert.Equal(returnDate, returnedState.ToLoan().ReturnDate);
        Assert.IsType<TransitionOutcome.Applied>(state.Issue(Valid(good, state)));
        Assert.IsType<TransitionOutcome.Rejected>(state.Return(new ReturnRequest("L404", returnDate)));
    }

    [Fact]
    public void NormalOperation_ScriptProducesDocumentedStatesAndFines()
    {
        var parsed = OperationParser.Parse(Script);
        Assert.True(parsed.IsValid);

        var run = LibraryWorkflow.Run(LibraryState.Empty, parsed.Operations);

        Assert.Equal(18, run.Steps.Length);
        Assert.Equal(11, run.Steps.Count(s => s.Succeeded));
        Assert.Equal(
            new[] { 4, 9, 11, 12, 14, 16, 17 },
            run.Steps.Select((s, i) => (s, i)).Where(p => !p.s.Succeeded).Select(p => p.i + 1).ToArray());

        var loans = run.FinalState.ToLoans();
        Assert.Equal(new[] { "L101", "L103", "L106", "L107" }, loans.Select(l => l.LoanId).ToArray());

        var standard = FinePipeline.Run(loans, AsOf, FinePolicies.Standard);
        var strict = FinePipeline.Run(loans, AsOf, FinePolicies.Strict);

        Assert.Equal(new[] { ("L107", 600), ("L101", 250) }, standard.Lines.Select(l => (l.LoanId, l.Fine)).ToArray());
        Assert.Equal(850, standard.TotalFine);
        Assert.Equal(new[] { ("L107", 1200), ("L101", 500) }, strict.Lines.Select(l => (l.LoanId, l.Fine)).ToArray());
        Assert.Equal(1700, strict.TotalFine);
    }

    [Fact]
    public void MissingPrerequisite_ReturnOfUnknownLoan_IsRejected()
    {
        var state = Seed();
        var request = new ReturnRequest("L999", new DateOnly(2026, 10, 20));

        var rejected = Assert.IsType<TransitionOutcome.Rejected>(state.Return(request));
        var step = LibraryWorkflow.Apply(state, new Operation.Return(request));

        Assert.Contains("does not exist", rejected.Reason);
        Assert.False(step.Succeeded);
        Assert.Same(state, step.State);
    }

    [Fact]
    public void Return_BeforeLoanDate_IsRejected_ReturnOnLoanDateIsAccepted()
    {
        var state = Issue(Seed(), Request("L1", "Aigerim", "Dune", "2026-10-05", 14));

        var early = state.Return(new ReturnRequest("L1", new DateOnly(2026, 10, 4)));
        var sameDay = Applied(state.Return(new ReturnRequest("L1", new DateOnly(2026, 10, 5))));

        Assert.IsType<TransitionOutcome.Rejected>(early);
        Assert.IsType<LoanState.Active>(state.Loans["L1"]);
        Assert.IsType<LoanState.Returned>(sameDay.Loans["L1"]);
    }

    [Fact]
    public void RepeatedOperations_FollowDocumentedRules()
    {
        var state = Issue(Seed(), Request("L1", "Aigerim", "Dune", "2026-10-01", 14));
        var returnedOnce = Applied(state.Return(new ReturnRequest("L1", new DateOnly(2026, 10, 10))));

        var returnedAgain = Assert.IsType<TransitionOutcome.Rejected>(
            returnedOnce.Return(new ReturnRequest("L1", new DateOnly(2026, 10, 12))));

        Assert.Contains("already returned on 2026-10-10", returnedAgain.Reason);
        Assert.Equal(
            new DateOnly(2026, 10, 10),
            Assert.IsType<LoanState.Returned>(returnedOnce.Loans["L1"]).ReturnedOn);

        var checkout = Valid(Request("L2", "Beck", "Dune", "2026-10-01", 7), state);
        var issuedOnce = Applied(state.Issue(checkout));
        var issuedTwice = Assert.IsType<TransitionOutcome.Rejected>(issuedOnce.Issue(checkout));

        Assert.Contains("already used", issuedTwice.Reason);
        Assert.Equal(2, issuedOnce.Loans.Count);

        var memberTwice = Assert.IsType<TransitionOutcome.Rejected>(state.RegisterMember("Aigerim"));
        Assert.Contains("already registered", memberTwice.Reason);
    }

    [Fact]
    public void FailedOperations_PreserveOriginalState()
    {
        var state = Issue(Seed(), Request("L1", "Aigerim", "Clean Code", "2026-10-01", 14));
        var operations = new Operation[]
        {
            new Operation.Checkout(Request("L2", "Beck", "Clean Code", "2026-10-02", 14)),
            new Operation.Checkout(Request("L3", "Nobody", "Dune", "2026-10-02", 7)),
            new Operation.Return(new ReturnRequest("L404", new DateOnly(2026, 10, 20))),
            new Operation.Return(new ReturnRequest("L1", new DateOnly(2026, 9, 1))),
            new Operation.RegisterMember("Beck"),
            new Operation.AddBook("Dune", 3),
        };

        var run = LibraryWorkflow.Run(state, operations);

        Assert.All(run.Steps, s => Assert.False(s.Succeeded));
        Assert.Same(state, run.FinalState);
        Assert.Equal(1, run.FinalState.Loans.Count);
        Assert.IsType<LoanState.Active>(run.FinalState.Loans["L1"]);
        Assert.Equal(2, run.FinalState.Members.Count);
        Assert.Equal(2, run.FinalState.Stock["Dune"]);
        Assert.Equal(0, run.FinalState.AvailableCopies("Clean Code"));
    }

    [Fact]
    public void StaleValidation_IssueChecksPrerequisitesAgain()
    {
        var state = Seed();
        var first = Valid(Request("L1", "Aigerim", "Clean Code", "2026-10-01", 14), state);
        var second = Valid(Request("L2", "Beck", "Clean Code", "2026-10-01", 14), state);

        var afterFirst = Applied(state.Issue(first));
        var rejected = Assert.IsType<TransitionOutcome.Rejected>(afterFirst.Issue(second));

        Assert.Contains("No copy of 'Clean Code'", rejected.Reason);
        Assert.False(afterFirst.Loans.ContainsKey("L2"));
    }

    [Fact]
    public void Catalog_AddBookRejectsInvalidCopiesEmptyIdAndDuplicates()
    {
        var state = Seed();

        var noCopies = Assert.IsType<TransitionOutcome.Rejected>(state.AddBook("Hobbit", 0));
        var emptyId = Assert.IsType<TransitionOutcome.Rejected>(state.AddBook(" ", 1));
        var duplicate = Assert.IsType<TransitionOutcome.Rejected>(state.AddBook("Dune", 1));
        var added = Applied(state.AddBook("Hobbit", 1));

        Assert.Contains("at least 1", noCopies.Reason);
        Assert.Contains("empty", emptyId.Reason);
        Assert.Contains("already in the catalog", duplicate.Reason);
        Assert.Equal(1, added.AvailableCopies("Hobbit"));
        Assert.Equal(0, state.AvailableCopies("Hobbit"));
    }

    [Fact]
    public void OperationParser_ParsesAllCommandsInOrder()
    {
        var parsed = OperationParser.Parse(new[]
        {
            "MEMBER,Aigerim",
            "BOOK,Dune,2",
            "CHECKOUT,L1,Aigerim,Dune,2026-10-01,14",
            "RETURN,L1,2026-10-10",
        });

        Assert.True(parsed.IsValid);
        Assert.Equal(4, parsed.Operations.Length);
        Assert.Equal("Aigerim", Assert.IsType<Operation.RegisterMember>(parsed.Operations[0]).MemberId);

        var book = Assert.IsType<Operation.AddBook>(parsed.Operations[1]);
        Assert.Equal(("Dune", 2), (book.BookId, book.Copies));

        var checkout = Assert.IsType<Operation.Checkout>(parsed.Operations[2]);
        Assert.Equal(new CheckoutRequest("L1", "Aigerim", "Dune", new DateOnly(2026, 10, 1), 14), checkout.Request);

        var giveBack = Assert.IsType<Operation.Return>(parsed.Operations[3]);
        Assert.Equal(new ReturnRequest("L1", new DateOnly(2026, 10, 10)), giveBack.Request);
    }

    [Fact]
    public void OperationParser_RejectsMalformedLinesWithLineNumbers()
    {
        var parsed = OperationParser.Parse(new[]
        {
            "MEMBER,Aigerim",
            "BOOK,Dune,two",
            "CHECKOUT,L1,Aigerim,Dune,10/10/2026,7",
            "RETURN,L1",
            "FLY,L1",
            "CHECKOUT,L2,Aigerim,Dune,2026-10-01,seven",
        });

        Assert.False(parsed.IsValid);
        Assert.Empty(parsed.Operations);
        Assert.Equal(5, parsed.Errors.Length);
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 2") && e.Contains("Copies"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 3") && e.Contains("valid yyyy-MM-dd date"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 4") && e.Contains("expects 3 fields"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 5") && e.Contains("Unknown command"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Line 6") && e.Contains("Days"));
    }

    [Fact]
    public void OperationParser_RejectsNullInput_AndAcceptsEmptyInput()
    {
        var missing = OperationParser.Parse(null);
        var empty = OperationParser.Parse(Array.Empty<string>());

        Assert.False(missing.IsValid);
        Assert.Empty(missing.Operations);
        Assert.Contains("missing", missing.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.True(empty.IsValid);
        Assert.Empty(empty.Operations);
        Assert.Empty(LibraryWorkflow.Run(LibraryState.Empty, empty.Operations).Steps);
    }
}
