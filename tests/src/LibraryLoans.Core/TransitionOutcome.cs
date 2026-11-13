namespace LibraryLoans.Core;

public abstract record TransitionOutcome
{
    private protected TransitionOutcome() { }

    public sealed record Applied(LibraryState State, string Message) : TransitionOutcome;

    public sealed record Rejected(string Reason) : TransitionOutcome;
}
