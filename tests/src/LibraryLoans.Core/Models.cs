using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed record Loan(string LoanId, string Member, string Book, DateOnly DueDate, DateOnly? ReturnDate);

public sealed record OverdueLoan(Loan Loan, int DaysLate);

public sealed record FineLine(string LoanId, string Member, string Book, int DaysLate, int Fine);

public sealed record FineReport(ImmutableArray<FineLine> Lines, int TotalFine)
{
    public int Count => Lines.IsDefault ? 0 : Lines.Length;
}

public sealed record ParseOutput(ImmutableArray<Loan> Loans, ImmutableArray<string> Errors)
{
    public bool IsValid => Errors.Length == 0;
}
