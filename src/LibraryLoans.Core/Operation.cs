namespace LibraryLoans.Core;

public abstract record Operation
{
    private protected Operation() { }

    public sealed record RegisterMember(string MemberId) : Operation;

    public sealed record AddBook(string BookId, int Copies) : Operation;

    public sealed record Checkout(CheckoutRequest Request) : Operation;

    public sealed record Return(ReturnRequest Request) : Operation;

    public sealed record Renew(RenewRequest Request) : Operation;

    public sealed record Reserve(string MemberId, string BookId) : Operation;

    public sealed record PayFine(string MemberId, int Amount) : Operation;
}
