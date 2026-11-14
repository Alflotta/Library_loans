using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed record BookSnapshot(string BookId, int Copies);

public sealed record LoanSnapshot(
    string LoanId,
    string MemberId,
    string BookId,
    DateOnly LoanDate,
    int Days,
    int Renewals,
    DateOnly DueDate,
    DateOnly? ReturnedOn,
    int Fine);

public sealed record ReservationSnapshot(string BookId, ImmutableArray<string> Members);

public sealed record BalanceSnapshot(string MemberId, int Amount);

public sealed record StateSnapshot(
    ImmutableArray<string> Members,
    ImmutableArray<BookSnapshot> Books,
    ImmutableArray<LoanSnapshot> Loans,
    ImmutableArray<ReservationSnapshot> Reservations,
    ImmutableArray<BalanceSnapshot> Balances);
