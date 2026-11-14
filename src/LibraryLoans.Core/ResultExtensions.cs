namespace LibraryLoans.Core;

public static class ResultExtensions
{
    public static Result<U> Map<T, U>(this Result<T> result, Func<T, U> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        return result switch
        {
            Result<T>.Ok ok => Result.Ok(map(ok.Value)),
            Result<T>.Failure failure => Result.Fail<U>(failure.Error),
            _ => Result.Fail<U>(ErrorCode.Unknown, "Unknown result case.")
        };
    }

    public static Result<U> Bind<T, U>(this Result<T> result, Func<T, Result<U>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);

        return result switch
        {
            Result<T>.Ok ok => bind(ok.Value),
            Result<T>.Failure failure => Result.Fail<U>(failure.Error),
            _ => Result.Fail<U>(ErrorCode.Unknown, "Unknown result case.")
        };
    }

    public static R Match<T, R>(this Result<T> result, Func<T, R> onOk, Func<AppError, R> onFailure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onOk);
        ArgumentNullException.ThrowIfNull(onFailure);

        return result switch
        {
            Result<T>.Ok ok => onOk(ok.Value),
            Result<T>.Failure failure => onFailure(failure.Error),
            _ => onFailure(new AppError(ErrorCode.Unknown, "Unknown result case."))
        };
    }

    public static Result<ValidatedCheckout> ToResult(this CheckoutOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome switch
        {
            CheckoutOutcome.Valid valid => Result.Ok(valid.Value),
            CheckoutOutcome.Invalid invalid => Result.Fail<ValidatedCheckout>(AppError.Combine(invalid.Errors)),
            _ => Result.Fail<ValidatedCheckout>(ErrorCode.Unknown, "Unknown checkout outcome.")
        };
    }
}

public static class MaybeExtensions
{
    public static Result<T> ToResult<T>(this Maybe<T> maybe, Func<AppError> onMissing)
    {
        ArgumentNullException.ThrowIfNull(maybe);
        ArgumentNullException.ThrowIfNull(onMissing);

        return maybe switch
        {
            Maybe<T>.Some some => Result.Ok(some.Value),
            Maybe<T>.None => Result.Fail<T>(onMissing()),
            _ => Result.Fail<T>(ErrorCode.Unknown, "Unknown maybe case.")
        };
    }

    public static R Match<T, R>(this Maybe<T> maybe, Func<T, R> onSome, Func<R> onNone)
    {
        ArgumentNullException.ThrowIfNull(maybe);
        ArgumentNullException.ThrowIfNull(onSome);
        ArgumentNullException.ThrowIfNull(onNone);

        return maybe switch
        {
            Maybe<T>.Some some => onSome(some.Value),
            Maybe<T>.None => onNone(),
            _ => onNone()
        };
    }
}
