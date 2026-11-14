using System.Collections.Immutable;

namespace LibraryLoans.Core;

public sealed record AppError(ErrorCode Code, string Message)
{
    public ErrorKind Kind =>
        (Code is ErrorCode.FileNotFound or ErrorCode.FileUnreadable or ErrorCode.MalformedContent or ErrorCode.ExportFailed or ErrorCode.NativeLibraryUnavailable)
            ? ErrorKind.Operational
            : ErrorKind.Domain;

    public static AppError Combine(ImmutableArray<AppError> errors)
    {
        if (errors.IsDefaultOrEmpty)
            return new AppError(ErrorCode.Unknown, "An error occurred without details.");

        return new AppError(errors[0].Code, string.Join(" ", errors.Select(e => e.Message)));
    }
}
