namespace LibraryLoans.Core;

public abstract record TransitionOutcome
{
    private protected TransitionOutcome() { }

    public sealed record Applied(LibraryState State, string Message) : TransitionOutcome;

    public sealed record Rejected(AppError Error) : TransitionOutcome
    {
        public string Reason => Error.Message;
    }
}
