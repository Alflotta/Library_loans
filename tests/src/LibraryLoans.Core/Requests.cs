namespace LibraryLoans.Core;

public sealed record CheckoutRequest(string LoanId, string MemberId, string BookId, DateOnly LoanDate, int Days);

public sealed record ReturnRequest(string LoanId, DateOnly ReturnDate);
