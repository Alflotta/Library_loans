using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed class LibraryState
{
    private LibraryState(
        ImmutableHashSet<string> members,
        ImmutableDictionary<string, int> stock,
        ImmutableDictionary<string, LoanState> loans)
    {
        Members = members;
        Stock = stock;
        Loans = loans;
    }

    public static LibraryState Empty { get; } = new(
        ImmutableHashSet<string>.Empty,
        ImmutableDictionary<string, int>.Empty,
        ImmutableDictionary<string, LoanState>.Empty);

    public ImmutableHashSet<string> Members { get; }

    public ImmutableDictionary<string, int> Stock { get; }

    public ImmutableDictionary<string, LoanState> Loans { get; }

    public int AvailableCopies(string bookId)
    {
        if (!Stock.TryGetValue(bookId, out var total)) return 0;
        var lent = Loans.Values.Count(l => l is LoanState.Active a && a.Checkout.BookId == bookId);
        return total - lent;
    }

    public int ActiveLoanCount(string memberId) =>
        Loans.Values.Count(l => l is LoanState.Active a && a.Checkout.MemberId == memberId);

    public ImmutableArray<Loan> ToLoans() =>
        Loans.Values
            .Select(l => l.ToLoan())
            .OrderBy(l => l.LoanId, StringComparer.Ordinal)
            .ToImmutableArray();

    public TransitionOutcome RegisterMember(string memberId)
    {
        var id = (memberId ?? string.Empty).Trim();
        if (id.Length == 0)
            return new TransitionOutcome.Rejected("Member id is empty.");
        if (Members.Contains(id))
            return new TransitionOutcome.Rejected($"Member '{id}' is already registered.");

        return new TransitionOutcome.Applied(new LibraryState(Members.Add(id), Stock, Loans), $"Member {id} registered.");
    }

    public TransitionOutcome AddBook(string bookId, int copies)
    {
        var id = (bookId ?? string.Empty).Trim();
        if (id.Length == 0)
            return new TransitionOutcome.Rejected("Book id is empty.");
        if (copies < 1)
            return new TransitionOutcome.Rejected($"Copies must be at least 1, got {copies}.");
        if (Stock.ContainsKey(id))
            return new TransitionOutcome.Rejected($"Book '{id}' is already in the catalog.");

        return new TransitionOutcome.Applied(new LibraryState(Members, Stock.Add(id, copies), Loans), $"Book {id} added, copies: {copies}.");
    }

    public TransitionOutcome Issue(ValidatedCheckout checkout)
    {
        ArgumentNullException.ThrowIfNull(checkout);

        var violations = StateViolations(checkout.LoanId, checkout.MemberId, checkout.BookId);
        if (violations.Length > 0)
            return new TransitionOutcome.Rejected(string.Join(" ", violations));

        var active = new LoanState.Active(checkout);
        var next = new LibraryState(Members, Stock, Loans.Add(checkout.LoanId, active));
        var message = $"Loan {checkout.LoanId} issued to {checkout.MemberId} for '{checkout.BookId}', due {checkout.DueDate:yyyy-MM-dd}.";
        return new TransitionOutcome.Applied(next, message);
    }

    public TransitionOutcome Return(ReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loanId = (request.LoanId ?? string.Empty).Trim();
        if (!Loans.TryGetValue(loanId, out var current))
            return new TransitionOutcome.Rejected($"Loan '{loanId}' does not exist, nothing to return.");

        return current switch
        {
            LoanState.Returned r =>
                new TransitionOutcome.Rejected($"Loan '{loanId}' was already returned on {r.ReturnedOn:yyyy-MM-dd}."),
            LoanState.Active a when request.ReturnDate < a.Checkout.LoanDate =>
                new TransitionOutcome.Rejected($"Return date {request.ReturnDate:yyyy-MM-dd} is before the loan date {a.Checkout.LoanDate:yyyy-MM-dd}."),
            LoanState.Active a =>
                new TransitionOutcome.Applied(
                    new LibraryState(Members, Stock, Loans.SetItem(loanId, new LoanState.Returned(a.Checkout, request.ReturnDate))),
                    $"Loan {loanId} returned on {request.ReturnDate:yyyy-MM-dd}."),
            _ => new TransitionOutcome.Rejected("Unknown loan state.")
        };
    }

    internal ImmutableArray<string> StateViolations(string loanId, string memberId, string bookId)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();

        if (Loans.ContainsKey(loanId))
            reasons.Add($"Loan id '{loanId}' is already used.");

        if (!Members.Contains(memberId))
            reasons.Add($"Member '{memberId}' is not registered.");
        else if (ActiveLoanCount(memberId) >= LibraryRules.MaxActiveLoans)
            reasons.Add($"Member '{memberId}' already has {LibraryRules.MaxActiveLoans} active loans (maximum {LibraryRules.MaxActiveLoans}).");

        if (!Stock.ContainsKey(bookId))
            reasons.Add($"Book '{bookId}' is not in the catalog.");
        else if (AvailableCopies(bookId) < 1)
            reasons.Add($"No copy of '{bookId}' is available.");

        return reasons.ToImmutable();
    }
}
