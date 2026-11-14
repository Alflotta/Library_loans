using System.Collections.Immutable;
using LibraryLoans.Boundary;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public sealed class ResultHandlingTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2026, 10, 20);

    private readonly string _folder;

    public ResultHandlingTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "libraryloans-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static T Value<T>(Result<T> result) => Assert.IsType<Result<T>.Ok>(result).Value;

    private static AppError Error<T>(Result<T> result) => Assert.IsType<Result<T>.Failure>(result).Error;

    private static LibraryState Seed() =>
        LibraryWorkflow.Run(LibraryState.Empty, new Operation[]
        {
            new Operation.RegisterMember("Aigerim"),
            new Operation.RegisterMember("Beck"),
            new Operation.AddBook("Clean Code", 1),
            new Operation.AddBook("Dune", 2),
        }).FinalState;

    private static Operation.Checkout Checkout(string loanId, string member, string book, string date, int days) =>
        new(new CheckoutRequest(loanId, member, book, DateOnly.Parse(date), days));

    private static Operation.Return GiveBack(string loanId, string date) =>
        new(new ReturnRequest(loanId, DateOnly.Parse(date)));

    private string WriteFile(string name, params string[] lines)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void CompleteSuccess_CheckoutChainProducesNextStateAndReceipt()
    {
        var state = Seed();

        var issued = Value(LibraryWorkflow.Execute(state, Checkout("L1", "Aigerim", "Clean Code", "2026-10-01", 14)));

        Assert.Equal("Loan L1 issued to Aigerim for 'Clean Code', due 2026-10-15.", issued.Message);
        Assert.IsType<LoanState.Active>(issued.State.Loans["L1"]);
        Assert.Equal(0, issued.State.AvailableCopies("Clean Code"));
        Assert.Empty(state.Loans);

        var returned = Value(LibraryWorkflow.Execute(issued.State, GiveBack("L1", "2026-10-10")));

        Assert.Equal("Loan L1 returned on 2026-10-10.", returned.Message);
        Assert.IsType<LoanState.Returned>(returned.State.Loans["L1"]);
        Assert.IsType<LoanState.Active>(issued.State.Loans["L1"]);
    }

    [Fact]
    public void DomainFailures_EachHasItsOwnCodeAndNamesTheRuleOrItem()
    {
        var state = Value(LibraryWorkflow.Execute(Seed(), Checkout("L1", "Aigerim", "Clean Code", "2026-10-01", 14))).State;

        var period = Error(LibraryWorkflow.Execute(Seed(), Checkout("L2", "Aigerim", "Dune", "2026-10-01", 31)));
        var noMember = Error(LibraryWorkflow.Execute(Seed(), Checkout("L2", "Nobody", "Dune", "2026-10-01", 7)));
        var noBook = Error(LibraryWorkflow.Execute(Seed(), Checkout("L2", "Aigerim", "Atlas", "2026-10-01", 7)));
        var noCopy = Error(LibraryWorkflow.Execute(state, Checkout("L2", "Beck", "Clean Code", "2026-10-02", 14)));
        var noLoan = Error(LibraryWorkflow.Execute(state, GiveBack("L404", "2026-10-20")));
        var early = Error(LibraryWorkflow.Execute(state, GiveBack("L1", "2026-09-01")));
        var twice = Error(LibraryWorkflow.Execute(
            Value(LibraryWorkflow.Execute(state, GiveBack("L1", "2026-10-10"))).State,
            GiveBack("L1", "2026-10-12")));

        Assert.Equal(ErrorCode.InvalidPeriod, period.Code);
        Assert.Contains("between 1 and 30", period.Message);
        Assert.Contains("got 31", period.Message);

        Assert.Equal(ErrorCode.MemberNotFound, noMember.Code);
        Assert.Contains("'Nobody'", noMember.Message);

        Assert.Equal(ErrorCode.BookNotFound, noBook.Code);
        Assert.Contains("'Atlas'", noBook.Message);

        Assert.Equal(ErrorCode.NoCopyAvailable, noCopy.Code);
        Assert.Contains("'Clean Code'", noCopy.Message);

        Assert.Equal(ErrorCode.LoanNotFound, noLoan.Code);
        Assert.Contains("'L404'", noLoan.Message);

        Assert.Equal(ErrorCode.ReturnBeforeLoan, early.Code);
        Assert.Contains("2026-09-01", early.Message);

        Assert.Equal(ErrorCode.AlreadyReturned, twice.Code);
        Assert.Contains("2026-10-10", twice.Message);

        var all = new[] { period, noMember, noBook, noCopy, noLoan, early, twice };
        Assert.Equal(all.Length, all.Select(e => e.Code).Distinct().Count());
        Assert.All(all, e => Assert.Equal(ErrorKind.Domain, e.Kind));
    }

    [Fact]
    public void DomainFailures_MemberLoanLimitAndCatalogRulesHaveCodes()
    {
        var run = LibraryWorkflow.Run(Seed(), new Operation[]
        {
            new Operation.AddBook("Hobbit", 5),
            Checkout("L1", "Beck", "Hobbit", "2026-10-01", 7),
            Checkout("L2", "Beck", "Hobbit", "2026-10-01", 7),
            Checkout("L3", "Beck", "Hobbit", "2026-10-01", 7),
            Checkout("L4", "Beck", "Hobbit", "2026-10-01", 7),
            new Operation.AddBook("Hobbit", 1),
            new Operation.AddBook("Poems", 0),
            new Operation.RegisterMember("Beck"),
            new Operation.RegisterMember(" "),
        });

        Assert.True(run.Steps[3].Succeeded);
        Assert.Equal(ErrorCode.LoanLimitReached, Assert.IsType<AppError>(run.Steps[4].Error).Code);
        Assert.Equal(ErrorCode.DuplicateBook, Assert.IsType<AppError>(run.Steps[5].Error).Code);
        Assert.Equal(ErrorCode.InvalidCopies, Assert.IsType<AppError>(run.Steps[6].Error).Code);
        Assert.Equal(ErrorCode.DuplicateMember, Assert.IsType<AppError>(run.Steps[7].Error).Code);
        Assert.Equal(ErrorCode.EmptyValue, Assert.IsType<AppError>(run.Steps[8].Error).Code);
        Assert.Equal(3, run.FinalState.ActiveLoanCount("Beck"));
    }

    [Fact]
    public void LookupPresentAndAbsent_ZeroCopiesIsAValueNotAbsence()
    {
        var state = Value(LibraryWorkflow.Execute(Seed(), Checkout("L1", "Aigerim", "Clean Code", "2026-10-01", 14))).State;

        var none = Assert.IsType<Maybe<int>.Some>(state.FindAvailableCopies("Clean Code"));
        var two = Assert.IsType<Maybe<int>.Some>(state.FindAvailableCopies("Dune"));

        Assert.Equal(0, none.Value);
        Assert.Equal(2, two.Value);
        Assert.IsType<Maybe<int>.None>(state.FindAvailableCopies("Atlas"));

        Assert.Equal("Aigerim", Assert.IsType<Maybe<string>.Some>(state.FindMember(" Aigerim ")).Value);
        Assert.IsType<Maybe<string>.None>(state.FindMember("Nobody"));
        Assert.IsType<Maybe<string>.None>(state.FindMember(""));

        Assert.IsType<Maybe<LoanState>.Some>(state.FindLoan("L1"));
        Assert.IsType<Maybe<LoanState>.None>(state.FindLoan("L9"));

        Assert.Equal(0, Value(state.RequireAvailableCopies("Clean Code")));
        Assert.Equal(ErrorCode.BookNotFound, Error(state.RequireAvailableCopies("Atlas")).Code);
        Assert.Equal(ErrorCode.MemberNotFound, Error(state.RequireMember("Nobody")).Code);
        Assert.Equal(ErrorCode.LoanNotFound, Error(state.RequireLoan("L9")).Code);
    }

    [Fact]
    public void Maybe_LegitimatePrimitiveValuesAreNotAbsence()
    {
        Assert.IsType<Maybe<int>.Some>(Maybe.Some(0));
        Assert.IsType<Maybe<bool>.Some>(Maybe.Some(false));
        Assert.IsType<Maybe<string>.Some>(Maybe.Some(string.Empty));
        Assert.IsType<Maybe<int>.None>(Maybe.None<int>());

        var missingCalls = 0;
        AppError OnMissing()
        {
            missingCalls++;
            return new AppError(ErrorCode.BookNotFound, "missing");
        }

        Assert.Equal(0, Value(Maybe.Some(0).ToResult(OnMissing)));
        Assert.False(Value(Maybe.Some(false).ToResult(OnMissing)));
        Assert.Equal(string.Empty, Value(Maybe.Some(string.Empty).ToResult(OnMissing)));
        Assert.Equal(0, missingCalls);

        Assert.Equal(ErrorCode.BookNotFound, Error(Maybe.None<int>().ToResult(OnMissing)).Code);
        Assert.Equal(1, missingCalls);

        var shown = Maybe.Some(7).Match(v => $"value {v}", () => "nothing");
        var absent = Maybe.None<int>().Match(v => $"value {v}", () => "nothing");
        Assert.Equal("value 7", shown);
        Assert.Equal("nothing", absent);
    }

    [Fact]
    public void Map_OnSuccess_ReceivesTheValueAndReturnsTheTransformedValue()
    {
        var received = -1;

        var doubled = Result.Ok(21).Map(n =>
        {
            received = n;
            return n * 2;
        });
        var text = Result.Ok(5).Map(n => $"n={n}");

        Assert.Equal(21, received);
        Assert.Equal(42, Value(doubled));
        Assert.Equal("n=5", Value(text));
    }

    [Fact]
    public void Bind_OnSuccess_ReceivesTheValueAndKeepsTheNextResult()
    {
        var received = -1;

        var next = Result.Ok(4).Bind(n =>
        {
            received = n;
            return Result.Ok(n * 10);
        });
        var failing = Result.Ok(4).Bind(n => Result.Fail<int>(ErrorCode.InvalidPeriod, "next step rejected " + n));

        Assert.Equal(4, received);
        Assert.Equal(40, Value(next));
        Assert.Equal(ErrorCode.InvalidPeriod, Error(failing).Code);
        Assert.Equal("next step rejected 4", Error(failing).Message);
    }

    [Fact]
    public void FailurePropagation_MapAndBindDoNotCallTheirFunctions()
    {
        var failure = Result.Fail<int>(ErrorCode.MemberNotFound, "member is missing");
        var calls = 0;

        var mapped = failure.Map(n =>
        {
            calls++;
            return n + 1;
        });
        var bound = failure.Bind(n =>
        {
            calls++;
            return Result.Ok(n + 1);
        });
        var chained = failure
            .Map(n =>
            {
                calls++;
                return n;
            })
            .Bind(n =>
            {
                calls++;
                return Result.Ok(n);
            });

        Assert.Equal(0, calls);
        Assert.Equal(ErrorCode.MemberNotFound, Error(mapped).Code);
        Assert.Equal("member is missing", Error(bound).Message);
        Assert.Equal(Error(failure), Error(chained));
    }

    [Fact]
    public void ChainStopsAtFirstFailure_DependentStepsAreSkipped()
    {
        var state = Seed();

        var memberFirst = LibraryWorkflow.Execute(state, Checkout("", "Nobody", "Atlas", "2026-10-01", 0));
        var bookSecond = LibraryWorkflow.Execute(state, Checkout("", "Aigerim", "Atlas", "2026-10-01", 0));
        var validationThird = LibraryWorkflow.Execute(state, Checkout("L1", "Aigerim", "Dune", "2026-10-01", 0));

        Assert.Equal(ErrorCode.MemberNotFound, Error(memberFirst).Code);
        Assert.Equal(ErrorCode.BookNotFound, Error(bookSecond).Code);
        Assert.Equal(ErrorCode.InvalidPeriod, Error(validationThird).Code);

        var step = LibraryWorkflow.Apply(state, Checkout("L1", "Nobody", "Dune", "2026-10-01", 0));
        Assert.False(step.Succeeded);
        Assert.Same(state, step.State);
        Assert.Empty(step.State.Loans);
    }

    [Fact]
    public void ResultAndMaybe_CannotRepresentContradictoryStates()
    {
        Assert.Empty(typeof(Result<int>.Ok).GetConstructors());
        Assert.Empty(typeof(Result<int>.Failure).GetConstructors());
        Assert.Empty(typeof(Maybe<int>.Some).GetConstructors());
        Assert.Empty(typeof(Maybe<int>.None).GetConstructors());

        Assert.Throws<ArgumentNullException>(() => Result.Ok<string>(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Fail<int>(null!));
        Assert.Throws<ArgumentNullException>(() => Maybe.Some<string>(null!));
    }

    [Fact]
    public void FactoryOutcome_ConvertsToResult_WithFirstCodeAndAllMessages()
    {
        var state = Seed();
        var date = new DateOnly(2026, 10, 1);

        var good = ValidatedCheckout.Create(new CheckoutRequest("L1", "Aigerim", "Dune", date, 7), state).ToResult();
        var bad = ValidatedCheckout.Create(new CheckoutRequest("L1", "Nobody", "Missing", date, 0), state).ToResult();

        Assert.Equal("L1", Value(good).LoanId);
        Assert.Equal(ErrorCode.InvalidPeriod, Error(bad).Code);
        Assert.Contains("'Nobody' is not registered", Error(bad).Message);
        Assert.Contains("'Missing' is not in the catalog", Error(bad).Message);
    }

    [Fact]
    public void ErrorKind_SeparatesDomainFailuresFromOperationalProblems()
    {
        Assert.Equal(ErrorKind.Domain, new AppError(ErrorCode.NoCopyAvailable, "x").Kind);
        Assert.Equal(ErrorKind.Domain, new AppError(ErrorCode.InvalidPeriod, "x").Kind);
        Assert.Equal(ErrorKind.Operational, new AppError(ErrorCode.FileNotFound, "x").Kind);
        Assert.Equal(ErrorKind.Operational, new AppError(ErrorCode.FileUnreadable, "x").Kind);
        Assert.Equal(ErrorKind.Operational, new AppError(ErrorCode.MalformedContent, "x").Kind);
    }

    [Fact]
    public void MissingFile_IsReportedAsOperationalFailure_AndStateIsKept()
    {
        var state = Seed();
        var missing = Path.Combine(_folder, "nothing.csv");
        var inMissingFolder = Path.Combine(_folder, "no-such-folder", "ops.csv");

        var loaded = FileBoundary.LoadOperations(missing);
        var error = Error(loaded);

        Assert.Equal(ErrorCode.FileNotFound, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains(missing, error.Message);

        var after = loaded.Match<ImmutableArray<Operation>, LibraryState>(
            operations => LibraryWorkflow.Run(state, operations).FinalState,
            _ => state);
        Assert.Same(state, after);
        Assert.False(File.Exists(missing));

        Assert.Equal(ErrorCode.FileNotFound, Error(FileBoundary.LoadOperations(inMissingFolder)).Code);
        Assert.Equal(ErrorCode.FileNotFound, Error(FileBoundary.LoadLoans(missing)).Code);
        Assert.Equal(ErrorCode.FileNotFound, Error(FileBoundary.LoadOperations("  ")).Code);
    }

    [Fact]
    public void MalformedFile_BecomesMalformedContentFailure_WithoutPartialUpdate()
    {
        var state = Seed();
        var operations = WriteFile(
            "bad-operations.csv",
            "MEMBER,Zhan",
            "BOOK,Dune,two",
            "FLY,L1");
        var loans = WriteFile(
            "bad-loans.csv",
            "LoanId,Member,Book,DueDate,ReturnDate",
            "L1,Aigerim,Dune,12/10/2026,");

        var badOperations = Error(FileBoundary.LoadOperations(operations));
        var badLoans = Error(FileBoundary.LoadLoans(loans));

        Assert.Equal(ErrorCode.MalformedContent, badOperations.Code);
        Assert.Equal(ErrorKind.Operational, badOperations.Kind);
        Assert.Contains(operations, badOperations.Message);
        Assert.Contains("Line 2", badOperations.Message);
        Assert.Contains("Line 3", badOperations.Message);

        Assert.Equal(ErrorCode.MalformedContent, badLoans.Code);
        Assert.Contains(loans, badLoans.Message);
        Assert.Contains("Line 2", badLoans.Message);

        var after = FileBoundary.LoadOperations(operations).Match<ImmutableArray<Operation>, LibraryState>(
            ops => LibraryWorkflow.Run(state, ops).FinalState,
            _ => state);
        Assert.Same(state, after);
        Assert.False(state.Members.Contains("Zhan"));
    }

    [Fact]
    public void UnreadablePath_IsReportedAsFileUnreadable()
    {
        var error = Error(FileBoundary.LoadOperations(_folder));

        Assert.Equal(ErrorCode.FileUnreadable, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains(_folder, error.Message);
    }

    [Fact]
    public void ValidFiles_LoadAndFlowThroughWorkflowAndFinePipeline()
    {
        var script = WriteFile(
            "script.csv",
            "MEMBER,Aigerim",
            "BOOK,Dune,1",
            "CHECKOUT,L1,Aigerim,Dune,2026-10-01,7");
        var empty = WriteFile("empty.csv");
        var loansFile = WriteFile(
            "loans.csv",
            "LoanId,Member,Book,DueDate,ReturnDate",
            "L1,Aigerim,Dune,2026-10-08,");

        var operations = Value(FileBoundary.LoadOperations(script));
        var final = LibraryWorkflow.Run(LibraryState.Empty, operations).FinalState;
        var standard = FinePipeline.Run(final.ToLoans(), AsOf, FinePolicies.Standard);
        var strict = FinePipeline.Run(final.ToLoans(), AsOf, FinePolicies.Strict);

        Assert.Equal(3, operations.Length);
        Assert.Equal(600, standard.TotalFine);
        Assert.Equal(1200, strict.TotalFine);

        Assert.Empty(Value(FileBoundary.LoadOperations(empty)));
        Assert.Single(Value(FileBoundary.LoadLoans(loansFile)));
    }
}
