using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed class LibraryState
{
    private LibraryState(
        ImmutableHashSet<string> members,
        ImmutableDictionary<string, int> stock,
        ImmutableDictionary<string, LoanState> loans,
        ImmutableDictionary<string, ImmutableList<string>> reservations,
        ImmutableDictionary<string, int> balances)
    {
        Members = members;
        Stock = stock;
        Loans = loans;
        Reservations = reservations;
        Balances = balances;
    }

    public static LibraryState Empty { get; } = new(
        ImmutableHashSet<string>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, LoanState>.Empty,
        ImmutableDictionary<string, ImmutableList<string>>.Empty,
        ImmutableDictionary<string, int>.Empty);

    public ImmutableHashSet<string> Members { get; }

    public ImmutableDictionary<string, int> Stock { get; }

    public ImmutableDictionary<string, LoanState> Loans { get; }

    public ImmutableDictionary<string, ImmutableList<string>> Reservations { get; }

    public ImmutableDictionary<string, int> Balances { get; }

    public int AvailableCopies(string bookId)
    {
        if (!Stock.TryGetValue(bookId, out var total)) return 0;
        var lent = Loans.Values.Count(l => l is LoanState.Active a && a.Checkout.BookId == bookId);
        return total - lent;
    }

    public int ActiveLoanCount(string memberId) =>
        Loans.Values.Count(l => l is LoanState.Active a && a.Checkout.MemberId == memberId);

    public int BalanceOf(string memberId) =>
        Balances.TryGetValue(Clean(memberId), out var amount) ? amount : 0;

    public ImmutableList<string> QueueFor(string bookId) =>
        Reservations.TryGetValue(Clean(bookId), out var queue) ? queue : ImmutableList<string>.Empty;

    public ImmutableArray<Loan> ToLoans() =>
        Loans.Values
            .Select(l => l.ToLoan())
            .OrderBy(l => l.LoanId, StringComparer.Ordinal)
            .ToImmutableArray();

    public Maybe<string> FindMember(string memberId)
    {
        var id = Clean(memberId);
        return Members.Contains(id) ? Maybe.Some(id) : Maybe.None<string>();
    }

    public Maybe<int> FindAvailableCopies(string bookId)
    {
        var id = Clean(bookId);
        return Stock.ContainsKey(id) ? Maybe.Some(AvailableCopies(id)) : Maybe.None<int>();
    }

    public Maybe<LoanState> FindLoan(string loanId)
    {
        var id = Clean(loanId);
        return Loans.TryGetValue(id, out var loan) ? Maybe.Some(loan) : Maybe.None<LoanState>();
    }

    public Result<string> RequireMember(string memberId)
    {
        var id = Clean(memberId);
        return FindMember(id).ToResult(() =>
            new AppError(ErrorCode.MemberNotFound, $"Member '{id}' is not registered."));
    }

    public Result<int> RequireAvailableCopies(string bookId)
    {
        var id = Clean(bookId);
        return FindAvailableCopies(id).ToResult(() =>
            new AppError(ErrorCode.BookNotFound, $"Book '{id}' is not in the catalog."));
    }

    public Result<LoanState> RequireLoan(string loanId)
    {
        var id = Clean(loanId);
        return FindLoan(id).ToResult(() =>
            new AppError(ErrorCode.LoanNotFound, $"Loan '{id}' does not exist."));
    }

    public Result<LibraryState> RegisterMemberChecked(string memberId)
    {
        var id = Clean(memberId);
        if (id.Length == 0)
            return Result.Fail<LibraryState>(ErrorCode.EmptyValue, "Member id is empty.");
        if (Members.Contains(id))
            return Result.Fail<LibraryState>(ErrorCode.DuplicateMember, $"Member '{id}' is already registered.");

        return Result.Ok(With(members: Members.Add(id)));
    }

    public Result<LibraryState> AddBookChecked(string bookId, int copies)
    {
        var id = Clean(bookId);
        if (id.Length == 0)
            return Result.Fail<LibraryState>(ErrorCode.EmptyValue, "Book id is empty.");
        if (copies < 1)
            return Result.Fail<LibraryState>(ErrorCode.InvalidCopies, $"Copies must be at least 1, got {copies}.");
        if (Stock.ContainsKey(id))
            return Result.Fail<LibraryState>(ErrorCode.DuplicateBook, $"Book '{id}' is already in the catalog.");

        return Result.Ok(With(stock: Stock.Add(id, copies)));
    }

    public Result<LibraryState> IssueChecked(ValidatedCheckout checkout)
    {
        ArgumentNullException.ThrowIfNull(checkout);

        var violations = Violations(checkout.LoanId, checkout.MemberId, checkout.BookId);
        if (violations.Length > 0)
            return Result.Fail<LibraryState>(AppError.Combine(violations));

        var queue = QueueFor(checkout.BookId);
        var reservations = queue.Contains(checkout.MemberId)
            ? WithQueue(checkout.BookId, queue.Remove(checkout.MemberId))
            : Reservations;

        return Result.Ok(With(
            loans: Loans.Add(checkout.LoanId, new LoanState.Active(checkout)),
            reservations: reservations));
    }

    public Result<LibraryState> ReturnChecked(ReturnRequest request) =>
        ReturnChecked(request, FinePolicies.NoFine);

    public Result<LibraryState> ReturnChecked(ReturnRequest request, Func<int, int> finePolicy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(finePolicy);

        var loanId = Clean(request.LoanId);

        return RequireLoan(loanId).Bind<LoanState, LibraryState>(current => current switch
        {
            LoanState.Returned r =>
                Result.Fail<LibraryState>(
                    ErrorCode.AlreadyReturned,
                    $"Loan '{loanId}' was already returned on {r.ReturnedOn:yyyy-MM-dd}."),
            LoanState.Active a when request.ReturnDate < a.Checkout.LoanDate =>
                Result.Fail<LibraryState>(
                    ErrorCode.ReturnBeforeLoan,
                    $"Return date {request.ReturnDate:yyyy-MM-dd} is before the loan date {a.Checkout.LoanDate:yyyy-MM-dd}."),
            LoanState.Active a => Result.Ok(CompleteReturn(a, request.ReturnDate, finePolicy)),
            _ => Result.Fail<LibraryState>(ErrorCode.Unknown, "Unknown loan state.")
        });
    }

    public Result<LibraryState> RenewChecked(RenewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loanId = Clean(request.LoanId);

        return RequireLoan(loanId).Bind<LoanState, LibraryState>(current => current switch
        {
            LoanState.Returned r =>
                Result.Fail<LibraryState>(
                    ErrorCode.AlreadyReturned,
                    $"Loan '{loanId}' was already returned on {r.ReturnedOn:yyyy-MM-dd}."),
            LoanState.Active a when request.RenewDate > a.DueDate =>
                Result.Fail<LibraryState>(
                    ErrorCode.LoanOverdue,
                    $"Loan '{loanId}' is overdue since {a.DueDate:yyyy-MM-dd} and cannot be renewed."),
            LoanState.Active a when a.Renewals >= LibraryRules.MaxRenewals =>
                Result.Fail<LibraryState>(
                    ErrorCode.RenewalLimitReached,
                    $"Loan '{loanId}' was already renewed {a.Renewals} times (maximum {LibraryRules.MaxRenewals})."),
            LoanState.Active a when QueueFor(a.Checkout.BookId).Count > 0 =>
                Result.Fail<LibraryState>(
                    ErrorCode.BookReserved,
                    $"Book '{a.Checkout.BookId}' is reserved by another member, so loan '{loanId}' cannot be renewed."),
            LoanState.Active a =>
                Result.Ok(With(loans: Loans.SetItem(loanId, a.Renewed(a.DueDate.AddDays(LibraryRules.RenewalDays))))),
            _ => Result.Fail<LibraryState>(ErrorCode.Unknown, "Unknown loan state.")
        });
    }

    public Result<LibraryState> ReserveChecked(string memberId, string bookId)
    {
        var book = Clean(bookId);

        return RequireMember(memberId).Bind<string, LibraryState>(member =>
            RequireAvailableCopies(book).Bind<int, LibraryState>(copies => Reserve(member, book, copies)));
    }

    public Result<LibraryState> PayChecked(string memberId, int amount) =>
        RequireMember(memberId).Bind<string, LibraryState>(member => Pay(member, amount));

    public TransitionOutcome RegisterMember(string memberId) =>
        ToOutcome(RegisterMemberChecked(memberId), () => LibraryMessages.MemberRegistered(memberId));

    public TransitionOutcome AddBook(string bookId, int copies) =>
        ToOutcome(AddBookChecked(bookId, copies), () => LibraryMessages.BookAdded(bookId, copies));

    public TransitionOutcome Issue(ValidatedCheckout checkout) =>
        ToOutcome(IssueChecked(checkout), () => LibraryMessages.Issued(checkout));

    public TransitionOutcome Return(ReturnRequest request) =>
        ToOutcome(ReturnChecked(request), () => LibraryMessages.Returned(request.LoanId, request.ReturnDate));

    public StateSnapshot ToSnapshot() => new(
        Members.OrderBy(m => m, StringComparer.Ordinal).ToImmutableArray(),
        Stock.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new BookSnapshot(kv.Key, kv.Value))
            .ToImmutableArray(),
        Loans.Values.Select(ToLoanSnapshot)
            .OrderBy(l => l.LoanId, StringComparer.Ordinal)
            .ToImmutableArray(),
        Reservations.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ReservationSnapshot(kv.Key, kv.Value.ToImmutableArray()))
            .ToImmutableArray(),
        Balances.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new BalanceSnapshot(kv.Key, kv.Value))
            .ToImmutableArray());

    public static Result<LibraryState> Restore(StateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Members.IsDefault || snapshot.Books.IsDefault || snapshot.Loans.IsDefault
            || snapshot.Reservations.IsDefault || snapshot.Balances.IsDefault)
            return Result.Fail<LibraryState>(
                ErrorCode.MalformedContent,
                "State must contain members, books, loans, reservations and balances.");

        var problems = new List<string>();

        var members = ImmutableHashSet.CreateBuilder<string>();
        foreach (var raw in snapshot.Members)
        {
            var id = Clean(raw);
            if (id.Length == 0) problems.Add("A member id is empty.");
            else if (!members.Add(id)) problems.Add($"Member '{id}' is listed twice.");
        }

        var stock = ImmutableDictionary.CreateBuilder<string, int>();
        foreach (var book in snapshot.Books)
        {
            var id = Clean(book.BookId);
            if (id.Length == 0) problems.Add("A book id is empty.");
            else if (book.Copies < 1) problems.Add($"Book '{id}' has {book.Copies} copies.");
            else if (stock.ContainsKey(id)) problems.Add($"Book '{id}' is listed twice.");
            else stock.Add(id, book.Copies);
        }

        var loans = ImmutableDictionary.CreateBuilder<string, LoanState>();
        foreach (var l in snapshot.Loans)
        {
            var id = Clean(l.LoanId);
            var memberId = Clean(l.MemberId);
            var bookId = Clean(l.BookId);
            var before = problems.Count;

            if (id.Length == 0) problems.Add("A loan id is empty.");
            else if (loans.ContainsKey(id)) problems.Add($"Loan '{id}' is listed twice.");
            if (!members.Contains(memberId)) problems.Add($"Loan '{id}' refers to unknown member '{memberId}'.");
            if (!stock.ContainsKey(bookId)) problems.Add($"Loan '{id}' refers to unknown book '{bookId}'.");
            if (l.Days < LibraryRules.MinLoanDays || l.Days > LibraryRules.MaxLoanDays)
                problems.Add($"Loan '{id}' has an invalid period of {l.Days} days.");
            if (l.Renewals < 0 || l.Renewals > LibraryRules.MaxRenewals)
                problems.Add($"Loan '{id}' has an invalid renewal count {l.Renewals}.");
            if (l.Fine < 0) problems.Add($"Loan '{id}' has a negative fine.");
            if (l.DueDate != l.LoanDate.AddDays(l.Days + l.Renewals * LibraryRules.RenewalDays))
                problems.Add($"Loan '{id}' has a due date that does not match its period and renewals.");
            if (l.ReturnedOn is DateOnly returnedOn && returnedOn < l.LoanDate)
                problems.Add($"Loan '{id}' was returned before it was issued.");

            if (problems.Count > before) continue;

            var checkout = ValidatedCheckout.Restore(id, memberId, bookId, l.LoanDate, l.Days);
            if (l.ReturnedOn.HasValue)
                loans.Add(id, new LoanState.Returned(checkout, l.ReturnedOn.Value, l.DueDate, l.Fine));
            else
                loans.Add(id, new LoanState.Active(checkout, l.Renewals, l.DueDate));
        }

        foreach (var byMember in loans.Values.OfType<LoanState.Active>().GroupBy(a => a.Checkout.MemberId))
            if (byMember.Count() > LibraryRules.MaxActiveLoans)
                problems.Add($"Member '{byMember.Key}' has more than {LibraryRules.MaxActiveLoans} active loans.");

        foreach (var byBook in loans.Values.OfType<LoanState.Active>().GroupBy(a => a.Checkout.BookId))
            if (stock.TryGetValue(byBook.Key, out var copies) && byBook.Count() > copies)
                problems.Add($"Book '{byBook.Key}' is lent more times than it has copies.");

        var reservations = ImmutableDictionary.CreateBuilder<string, ImmutableList<string>>();
        foreach (var r in snapshot.Reservations)
        {
            var bookId = Clean(r.BookId);
            if (r.Members.IsDefault || r.Members.Length == 0) { problems.Add($"Reservation for '{bookId}' has no members."); continue; }
            if (!stock.ContainsKey(bookId)) { problems.Add($"Reservation refers to unknown book '{bookId}'."); continue; }
            if (reservations.ContainsKey(bookId)) { problems.Add($"Book '{bookId}' has two reservation queues."); continue; }

            var queue = r.Members.Select(Clean).ToImmutableList();
            foreach (var unknown in queue.Where(x => !members.Contains(x)))
                problems.Add($"Reservation for '{bookId}' refers to unknown member '{unknown}'.");
            if (queue.Distinct().Count() != queue.Count)
                problems.Add($"Reservation queue for '{bookId}' lists a member twice.");
            reservations.Add(bookId, queue);
        }

        var balances = ImmutableDictionary.CreateBuilder<string, int>();
        foreach (var b in snapshot.Balances)
        {
            var id = Clean(b.MemberId);
            if (!members.Contains(id)) problems.Add($"Balance refers to unknown member '{id}'.");
            else if (b.Amount < 0) problems.Add($"Member '{id}' has a negative balance.");
            else if (balances.ContainsKey(id)) problems.Add($"Member '{id}' has two balances.");
            else if (b.Amount > 0) balances.Add(id, b.Amount);
        }

        if (problems.Count > 0)
            return Result.Fail<LibraryState>(ErrorCode.MalformedContent, "State is inconsistent: " + string.Join(" ", problems));

        return Result.Ok(new LibraryState(
            members.ToImmutable(),
            stock.ToImmutable(),
            loans.ToImmutable(),
            reservations.ToImmutable(),
            balances.ToImmutable()));
    }

    internal ImmutableArray<AppError> Violations(string loanId, string memberId, string bookId)
    {
        var errors = ImmutableArray.CreateBuilder<AppError>();

        if (Loans.ContainsKey(loanId))
            errors.Add(new AppError(ErrorCode.DuplicateLoanId, $"Loan id '{loanId}' is already used."));

        if (!Members.Contains(memberId))
            errors.Add(new AppError(ErrorCode.MemberNotFound, $"Member '{memberId}' is not registered."));
        else if (ActiveLoanCount(memberId) >= LibraryRules.MaxActiveLoans)
            errors.Add(new AppError(
                ErrorCode.LoanLimitReached,
                $"Member '{memberId}' already has {LibraryRules.MaxActiveLoans} active loans (maximum {LibraryRules.MaxActiveLoans})."));
        else if (BalanceOf(memberId) > LibraryRules.BlockingBalance)
            errors.Add(new AppError(
                ErrorCode.MemberBlocked,
                $"Member '{memberId}' owes {BalanceOf(memberId)} KZT, above the limit of {LibraryRules.BlockingBalance} KZT."));

        if (!Stock.ContainsKey(bookId))
            errors.Add(new AppError(ErrorCode.BookNotFound, $"Book '{bookId}' is not in the catalog."));
        else if (AvailableCopies(bookId) < 1)
            errors.Add(new AppError(ErrorCode.NoCopyAvailable, $"No copy of '{bookId}' is available."));
        else if (QueueFor(bookId) is { Count: > 0 } queue && queue[0] != memberId)
            errors.Add(new AppError(ErrorCode.BookReserved, $"Book '{bookId}' is reserved for '{queue[0]}'."));

        return errors.ToImmutable();
    }

    internal int FineCharged(string loanId) =>
        Loans.TryGetValue(Clean(loanId), out var loan) && loan is LoanState.Returned r ? r.Fine : 0;

    internal DateOnly DueDateOf(string loanId) =>
        Loans.TryGetValue(Clean(loanId), out var loan) ? loan.ToLoan().DueDate : default;

    private LibraryState CompleteReturn(LoanState.Active active, DateOnly returnDate, Func<int, int> finePolicy)
    {
        var daysLate = Math.Max(0, returnDate.DayNumber - active.DueDate.DayNumber);
        var fine = daysLate > 0 ? Math.Max(0, finePolicy(daysLate)) : 0;
        var member = active.Checkout.MemberId;

        return With(
            loans: Loans.SetItem(
                active.Checkout.LoanId,
                new LoanState.Returned(active.Checkout, returnDate, active.DueDate, fine)),
            balances: fine > 0 ? Balances.SetItem(member, BalanceOf(member) + fine) : Balances);
    }

    private Result<LibraryState> Reserve(string member, string book, int copies)
    {
        if (copies > 0)
            return Result.Fail<LibraryState>(
                ErrorCode.CopyAvailable,
                $"A copy of '{book}' is available, borrow it instead of reserving.");

        var queue = QueueFor(book);
        if (queue.Contains(member))
            return Result.Fail<LibraryState>(
                ErrorCode.AlreadyReserved,
                $"Member '{member}' already reserved '{book}'.");

        return Result.Ok(With(reservations: WithQueue(book, queue.Add(member))));
    }

    private Result<LibraryState> Pay(string member, int amount)
    {
        var balance = BalanceOf(member);

        if (amount <= 0)
            return Result.Fail<LibraryState>(ErrorCode.InvalidPayment, $"Payment must be positive, got {amount}.");
        if (amount > balance)
            return Result.Fail<LibraryState>(
                ErrorCode.InvalidPayment,
                $"Payment {amount} KZT is more than the balance of {balance} KZT.");

        var remaining = balance - amount;
        return Result.Ok(With(balances: remaining == 0 ? Balances.Remove(member) : Balances.SetItem(member, remaining)));
    }

    private ImmutableDictionary<string, ImmutableList<string>> WithQueue(string book, ImmutableList<string> queue) =>
        queue.IsEmpty ? Reservations.Remove(book) : Reservations.SetItem(book, queue);

    private LibraryState With(
        ImmutableHashSet<string>? members = null,
        ImmutableDictionary<string, int>? stock = null,
        ImmutableDictionary<string, LoanState>? loans = null,
        ImmutableDictionary<string, ImmutableList<string>>? reservations = null,
        ImmutableDictionary<string, int>? balances = null) =>
        new(
            members ?? Members,
            stock ?? Stock,
            loans ?? Loans,
            reservations ?? Reservations,
            balances ?? Balances);

    private static LoanSnapshot ToLoanSnapshot(LoanState loan) => loan switch
    {
        LoanState.Active a => new LoanSnapshot(
            a.Checkout.LoanId, a.Checkout.MemberId, a.Checkout.BookId,
            a.Checkout.LoanDate, a.Checkout.Days, a.Renewals, a.DueDate, null, 0),
        LoanState.Returned r => new LoanSnapshot(
            r.Checkout.LoanId, r.Checkout.MemberId, r.Checkout.BookId,
            r.Checkout.LoanDate, r.Checkout.Days, RenewalsOf(r), r.DueDate, r.ReturnedOn, r.Fine),
        _ => throw new InvalidOperationException("Unknown loan state.")
    };

    private static int RenewalsOf(LoanState.Returned returned) =>
        (returned.DueDate.DayNumber - returned.Checkout.LoanDate.DayNumber - returned.Checkout.Days) / LibraryRules.RenewalDays;

    private static TransitionOutcome ToOutcome(Result<LibraryState> result, Func<string> successMessage) =>
        result.Match<LibraryState, TransitionOutcome>(
            next => new TransitionOutcome.Applied(next, successMessage()),
            error => new TransitionOutcome.Rejected(error));

    private static string Clean(string? text) => text?.Trim() ?? string.Empty;
}
