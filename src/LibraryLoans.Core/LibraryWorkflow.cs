using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed record StepSuccess(LibraryState State, string Message);

public sealed record StepResult(LibraryState State, string Message, bool Succeeded, AppError? Error = null);

public sealed record WorkflowRun(ImmutableArray<StepResult> Steps, LibraryState FinalState);

public static class LibraryWorkflow
{
    public static Result<StepSuccess> Execute(LibraryState state, Operation operation) =>
        Execute(state, operation, FinePolicies.NoFine);

    public static Result<StepSuccess> Execute(LibraryState state, Operation operation, Func<int, int> finePolicy)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(finePolicy);

        return operation switch
        {
            Operation.RegisterMember m =>
                state.RegisterMemberChecked(m.MemberId)
                    .Map(next => new StepSuccess(next, LibraryMessages.MemberRegistered(m.MemberId))),
            Operation.AddBook b =>
                state.AddBookChecked(b.BookId, b.Copies)
                    .Map(next => new StepSuccess(next, LibraryMessages.BookAdded(b.BookId, b.Copies))),
            Operation.Checkout c => CheckoutChain(state, c.Request),
            Operation.Return r =>
                state.ReturnChecked(r.Request, finePolicy)
                    .Map(next => new StepSuccess(
                        next,
                        LibraryMessages.Returned(r.Request.LoanId, r.Request.ReturnDate, next.FineCharged(r.Request.LoanId)))),
            Operation.Renew n =>
                state.RenewChecked(n.Request)
                    .Map(next => new StepSuccess(next, LibraryMessages.Renewed(n.Request.LoanId, next.DueDateOf(n.Request.LoanId)))),
            Operation.Reserve v =>
                state.ReserveChecked(v.MemberId, v.BookId)
                    .Map(next => new StepSuccess(
                        next,
                        LibraryMessages.Reserved(v.MemberId, v.BookId, next.QueueFor(v.BookId).IndexOf(v.MemberId.Trim()) + 1))),
            Operation.PayFine p =>
                state.PayChecked(p.MemberId, p.Amount)
                    .Map(next => new StepSuccess(next, LibraryMessages.Paid(p.MemberId, p.Amount, next.BalanceOf(p.MemberId)))),
            _ => Result.Fail<StepSuccess>(ErrorCode.Unknown, "Unknown operation.")
        };
    }

    public static StepResult Apply(LibraryState state, Operation operation) =>
        Apply(state, operation, FinePolicies.NoFine);

    public static StepResult Apply(LibraryState state, Operation operation, Func<int, int> finePolicy) =>
        Execute(state, operation, finePolicy).Match<StepSuccess, StepResult>(
            success => new StepResult(success.State, success.Message, true),
            error => new StepResult(state, $"REJECTED ({error.Code}): {error.Message}", false, error));

    public static IEnumerable<StepResult> Steps(LibraryState initial, IEnumerable<Operation> operations, Func<int, int> finePolicy)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(finePolicy);

        return Iterate(initial, operations, finePolicy);
    }

    public static WorkflowRun Run(LibraryState initial, IEnumerable<Operation> operations) =>
        Run(initial, operations, FinePolicies.NoFine);

    public static WorkflowRun Run(LibraryState initial, IEnumerable<Operation> operations, Func<int, int> finePolicy)
    {
        var steps = Steps(initial, operations, finePolicy).ToImmutableArray();
        return new WorkflowRun(steps, steps.IsEmpty ? initial : steps[steps.Length - 1].State);
    }

    private static IEnumerable<StepResult> Iterate(LibraryState initial, IEnumerable<Operation> operations, Func<int, int> finePolicy)
    {
        var current = initial;

        foreach (var operation in operations)
        {
            var step = Apply(current, operation, finePolicy);
            current = step.State;
            yield return step;
        }
    }

    private static Result<StepSuccess> CheckoutChain(LibraryState state, CheckoutRequest request) =>
        state.RequireMember(request.MemberId)
            .Bind(_ => state.RequireAvailableCopies(request.BookId))
            .Bind(_ => ValidatedCheckout.Create(request, state).ToResult())
            .Bind(checkout => state.IssueChecked(checkout)
                .Map(next => new StepSuccess(next, LibraryMessages.Issued(checkout))));
}
