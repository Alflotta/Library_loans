using System.Collections.Immutable;

namespace LibraryLoans.Core;

public abstract record CheckoutOutcome
{
    private protected CheckoutOutcome() { }

    public sealed record Valid(ValidatedCheckout Value) : CheckoutOutcome;

    public sealed record Invalid(ImmutableArray<AppError> Errors) : CheckoutOutcome
    {
        public ImmutableArray<string> Reasons => Errors.Select(e => e.Message).ToImmutableArray();
    }
}

public sealed class ValidatedCheckout
{
    private ValidatedCheckout(string loanId, string memberId, string bookId, DateOnly loanDate, int days)
    {
        LoanId = loanId;
        MemberId = memberId;
        BookId = bookId;
        LoanDate = loanDate;
        Days = days;
    }

    public string LoanId { get; }

    public string MemberId { get; }

    public string BookId { get; }

    public DateOnly LoanDate { get; }

    public int Days { get; }

    public DateOnly DueDate => LoanDate.AddDays(Days);

    internal static ValidatedCheckout Restore(string loanId, string memberId, string bookId, DateOnly loanDate, int days) =>
        new(loanId, memberId, bookId, loanDate, days);

    public static CheckoutOutcome Create(CheckoutRequest request, LibraryState library)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(library);

        var loanId = Clean(request.LoanId);
        var memberId = Clean(request.MemberId);
        var bookId = Clean(request.BookId);
        var errors = ImmutableArray.CreateBuilder<AppError>();

        if (loanId.Length == 0) errors.Add(new AppError(ErrorCode.EmptyValue, "Loan id is empty."));
        if (memberId.Length == 0) errors.Add(new AppError(ErrorCode.EmptyValue, "Member id is empty."));
        if (bookId.Length == 0) errors.Add(new AppError(ErrorCode.EmptyValue, "Book id is empty."));

        if (request.Days < LibraryRules.MinLoanDays || request.Days > LibraryRules.MaxLoanDays)
            errors.Add(new AppError(
                ErrorCode.InvalidPeriod,
                $"Loan period must be between {LibraryRules.MinLoanDays} and {LibraryRules.MaxLoanDays} days, got {request.Days}."));

        if (loanId.Length > 0 && memberId.Length > 0 && bookId.Length > 0)
            errors.AddRange(library.Violations(loanId, memberId, bookId));

        if (errors.Count > 0)
            return new CheckoutOutcome.Invalid(errors.ToImmutable());

        return new CheckoutOutcome.Valid(new ValidatedCheckout(loanId, memberId, bookId, request.LoanDate, request.Days));
    }

    private static string Clean(string? text) => text?.Trim() ?? string.Empty;
}
