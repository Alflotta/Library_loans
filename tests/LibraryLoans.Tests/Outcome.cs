using LibraryLoans.Core;

namespace LibraryLoans.Tests;

internal static class Outcome
{
    public static T Value<T>(Result<T> result) => Assert.IsType<Result<T>.Ok>(result).Value;

    public static AppError Error<T>(Result<T> result) => Assert.IsType<Result<T>.Failure>(result).Error;
}
