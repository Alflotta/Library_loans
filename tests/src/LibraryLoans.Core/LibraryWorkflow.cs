using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed record StepResult(LibraryState State, string Message, bool Succeeded);

public sealed record WorkflowRun(ImmutableArray<StepResult> Steps, LibraryState FinalState);

public static class LibraryWorkflow
{
    public static StepResult Apply(LibraryState state, Operation operation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);

        TransitionOutcome outcome = operation switch
        {
            Operation.RegisterMember m => state.RegisterMember(m.MemberId),
            Operation.AddBook b => state.AddBook(b.BookId, b.Copies),
            Operation.Checkout c => ApplyCheckout(state, c.Request),
            Operation.Return r => state.Return(r.Request),
            _ => new TransitionOutcome.Rejected("Unknown operation.")
        };

        return outcome switch
        {
            TransitionOutcome.Applied applied => new StepResult(applied.State, applied.Message, true),
            TransitionOutcome.Rejected rejected => new StepResult(state, "REJECTED: " + rejected.Reason, false),
            _ => new StepResult(state, "REJECTED: unknown outcome.", false)
        };
    }

    public static WorkflowRun Run(LibraryState initial, IEnumerable<Operation> operations)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(operations);

        var steps = ImmutableArray.CreateBuilder<StepResult>();
        var current = initial;

        foreach (var operation in operations)
        {
            var step = Apply(current, operation);
            steps.Add(step);
            current = step.State;
        }

        return new WorkflowRun(steps.ToImmutable(), current);
    }

    private static TransitionOutcome ApplyCheckout(LibraryState state, CheckoutRequest request)
    {
        CheckoutOutcome checkout = ValidatedCheckout.Create(request, state);

        return checkout switch
        {
            CheckoutOutcome.Valid valid => state.Issue(valid.Value),
            CheckoutOutcome.Invalid invalid =>
                new TransitionOutcome.Rejected("invalid checkout: " + string.Join(" ", invalid.Reasons)),
            _ => new TransitionOutcome.Rejected("Unknown checkout outcome.")
        };
    }
}
