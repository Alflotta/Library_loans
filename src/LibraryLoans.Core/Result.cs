namespace LibraryLoans.Core;

public abstract record Result<T>
{
    private protected Result() { }

    public sealed record Ok : Result<T>
    {
        internal Ok(T value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value), "A success must carry a value.");
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record Failure : Result<T>
    {
        internal Failure(AppError error)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error), "A failure must carry an error.");
        }

        public AppError Error { get; }
    }
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new Result<T>.Ok(value);

    public static Result<T> Fail<T>(AppError error) => new Result<T>.Failure(error);

    public static Result<T> Fail<T>(ErrorCode code, string message) =>
        new Result<T>.Failure(new AppError(code, message));
}
