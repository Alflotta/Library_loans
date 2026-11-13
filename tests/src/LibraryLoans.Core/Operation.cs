namespace LibraryLoans.Core;

public abstract record Operation
{
    private protected Operation() { }

    public sealed record RegisterMember(string MemberId) : Operation;

    public sealed record AddBook(string BookId, int Copies) : Operation;

    public sealed record Checkout(CheckoutRequest Request) : Operation;

    public sealed record Return(ReturnRequest Request) : Operation;
}
