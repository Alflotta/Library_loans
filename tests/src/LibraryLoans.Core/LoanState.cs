namespace LibraryLoans.Core;

public abstract record LoanState
{
    private protected LoanState() { }

    public Loan ToLoan() => this switch
    {
        Active a => new Loan(a.Checkout.LoanId, a.Checkout.MemberId, a.Checkout.BookId, a.Checkout.DueDate, null),
        Returned r => new Loan(r.Checkout.LoanId, r.Checkout.MemberId, r.Checkout.BookId, r.Checkout.DueDate, r.ReturnedOn),
        _ => throw new InvalidOperationException("Unknown loan state.")
    };

    public sealed record Active : LoanState
    {
        internal Active(ValidatedCheckout checkout)
        {
            Checkout = checkout;
        }

        public ValidatedCheckout Checkout { get; }
    }

    public sealed record Returned : LoanState
    {
        internal Returned(ValidatedCheckout checkout, DateOnly returnedOn)
        {
            Checkout = checkout;
            ReturnedOn = returnedOn;
        }

        public ValidatedCheckout Checkout { get; }

        public DateOnly ReturnedOn { get; }
    }
}
