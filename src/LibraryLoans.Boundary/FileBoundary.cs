using System.Collections.Immutable;
using LibraryLoans.Core;

namespace LibraryLoans.Boundary;

public static class FileBoundary
{
    public static Result<ImmutableArray<string>> ReadLines(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Fail<ImmutableArray<string>>(ErrorCode.FileNotFound, "No file path was given.");

        try
        {
            return Result.Ok(File.ReadAllLines(path).ToImmutableArray());
        }
        catch (FileNotFoundException)
        {
            return Result.Fail<ImmutableArray<string>>(
                ErrorCode.FileNotFound,
                $"File '{path}' was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Fail<ImmutableArray<string>>(
                ErrorCode.FileNotFound,
                $"File '{path}' was not found because its folder does not exist.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Fail<ImmutableArray<string>>(
                ErrorCode.FileUnreadable,
                $"File '{path}' cannot be read: {ex.Message}");
        }
    }

    public static Result<ImmutableArray<Operation>> LoadOperations(string path) =>
        ReadLines(path).Bind<ImmutableArray<string>, ImmutableArray<Operation>>(lines =>
        {
            var parsed = OperationParser.Parse(lines);
            return parsed.IsValid
                ? Result.Ok(parsed.Operations)
                : Result.Fail<ImmutableArray<Operation>>(ErrorCode.MalformedContent, Describe(path, parsed.Errors));
        });

    public static Result<ImmutableArray<Loan>> LoadLoans(string path) =>
        ReadLines(path).Bind<ImmutableArray<string>, ImmutableArray<Loan>>(lines =>
        {
            var parsed = LoanCsvParser.Parse(lines);
            return parsed.IsValid
                ? Result.Ok(parsed.Loans)
                : Result.Fail<ImmutableArray<Loan>>(ErrorCode.MalformedContent, Describe(path, parsed.Errors));
        });

    public static Result<ImmutableArray<Operation>> SnapshotOperations(string path) =>
        ScriptStream.Read(path)
            .CollectResults()
            .MapError(error => error.Code == ErrorCode.MalformedContent
                ? error with { Message = $"File '{path}' is malformed: {error.Message}" }
                : error);

    private static string Describe(string path, ImmutableArray<string> errors) =>
        $"File '{path}' is malformed: {string.Join(" ", errors)}";
}
