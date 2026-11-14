using System.Collections.Immutable;

namespace LibraryLoans.Core;

public static class ResultCollections
{
    public static Result<ImmutableArray<T>> CollectResults<T>(this IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var result in results)
        {
            switch (result)
            {
                case Result<T>.Ok ok:
                    builder.Add(ok.Value);
                    break;
                case Result<T>.Failure failure:
                    return Result.Fail<ImmutableArray<T>>(failure.Error);
                default:
                    return Result.Fail<ImmutableArray<T>>(ErrorCode.Unknown, "Unknown result case.");
            }
        }

        return Result.Ok(builder.ToImmutable());
    }

    public static Result<T> MapError<T>(this Result<T> result, Func<AppError, AppError> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        return result switch
        {
            Result<T>.Failure failure => Result.Fail<T>(map(failure.Error)),
            _ => result
        };
    }
}
