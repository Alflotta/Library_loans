namespace LibraryLoans.Core;

public abstract record LoanState
{
    private protected LoanState() { }

    public Loan ToLoan() => this switch
    {
        Active a => new Loan(a.Checkout.LoanId, a.Checkout.MemberId, a.Checkout.BookId, a.DueDate, null),
        Returned r => new Loan(r.Checkout.LoanId, r.Checkout.MemberId, r.Checkout.BookId, r.DueDate, r.ReturnedOn),
        _ => throw new InvalidOperationException("Unknown loan state.")
    };

    public sealed record Active : LoanState
    {
        internal Active(ValidatedCheckout checkout) : this(checkout, 0, checkout.DueDate) { }

        internal Active(ValidatedCheckout checkout, int renewals, DateOnly dueDate)
        {
            Checkout = checkout;
            Renewals = renewals;
            DueDate = dueDate;
        }

        public ValidatedCheckout Checkout { get; }

        public int Renewals { get; }

        public DateOnly DueDate { get; }

        internal Active Renewed(DateOnly newDueDate) => new Active(Checkout, Renewals + 1, newDueDate);
    }

    public sealed record Returned : LoanState
    {
        internal Returned(ValidatedCheckout checkout, DateOnly returnedOn, DateOnly dueDate, int fine)
        {
            Checkout = checkout;
            ReturnedOn = returnedOn;
            DueDate = dueDate;
            Fine = fine;
        }

        public ValidatedCheckout Checkout { get; }

        public DateOnly ReturnedOn { get; }

        public DateOnly DueDate { get; }

        public int Fine { get; }
    }
}
