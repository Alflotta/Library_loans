namespace LibraryLoans.Core;

internal static class LibraryMessages
{
    public static string MemberRegistered(string memberId) =>
        $"Member {memberId.Trim()} registered.";

    public static string BookAdded(string bookId, int copies) =>
        $"Book {bookId.Trim()} added, copies: {copies}.";

    public static string Issued(ValidatedCheckout checkout) =>
        $"Loan {checkout.LoanId} issued to {checkout.MemberId} for '{checkout.BookId}', due {checkout.DueDate:yyyy-MM-dd}.";

    public static string Returned(string loanId, DateOnly returnDate) =>
        $"Loan {loanId.Trim()} returned on {returnDate:yyyy-MM-dd}.";

    public static string Returned(string loanId, DateOnly returnDate, int fine) =>
        fine > 0
            ? $"{Returned(loanId, returnDate)} Fine charged: {fine} KZT."
            : Returned(loanId, returnDate);

    public static string Renewed(string loanId, DateOnly dueDate) =>
        $"Loan {loanId.Trim()} renewed, new due date {dueDate:yyyy-MM-dd}.";

    public static string Reserved(string memberId, string bookId, int position) =>
        $"Member {memberId.Trim()} reserved '{bookId.Trim()}', queue position {position}.";

    public static string Paid(string memberId, int amount, int remaining) =>
        $"Member {memberId.Trim()} paid {amount} KZT, remaining balance {remaining} KZT.";
}
