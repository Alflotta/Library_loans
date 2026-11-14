namespace LibraryLoans.Core;

public abstract record Maybe<T>
{
    private protected Maybe() { }

    public sealed record Some : Maybe<T>
    {
        internal Some(T value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value), "Some must carry a value.");
            Value = value;
        }

        public T Value { get; }
    }

    public sealed record None : Maybe<T>
    {
        internal None() { }
    }
}

public static class Maybe
{
    public static Maybe<T> Some<T>(T value) => new Maybe<T>.Some(value);

    public static Maybe<T> None<T>() => new Maybe<T>.None();
}
