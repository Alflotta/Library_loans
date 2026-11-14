using LibraryLoans.Core;

namespace LibraryLoans.Boundary;

public static class ScriptStream
{
    public static IEnumerable<Result<Operation>> Read(string path) =>
        ReadFrom(() => OpenFile(path));

    public static IEnumerable<Result<Operation>> ReadFrom(Func<Result<TextReader>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return Iterate(open);
    }

    private static IEnumerable<Result<Operation>> Iterate(Func<Result<TextReader>> open)
    {
        var opened = open();
        if (opened is not Result<TextReader>.Ok ok)
        {
            yield return Result.Fail<Operation>(((Result<TextReader>.Failure)opened).Error);
            yield break;
        }

        using var reader = ok.Value;
        var lineNo = 0;

        while (true)
        {
            var failure = TryReadLine(reader, out var line);
            if (failure is not null)
            {
                yield return Result.Fail<Operation>(failure);
                yield break;
            }

            if (line is null)
                yield break;

            lineNo++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            yield return OperationParser.ParseLine(line, lineNo);
        }
    }

    private static AppError? TryReadLine(TextReader reader, out string? line)
    {
        try
        {
            line = reader.ReadLine();
            return null;
        }
        catch (IOException ex)
        {
            line = null;
            return new AppError(ErrorCode.FileUnreadable, $"Reading failed: {ex.Message}");
        }
    }

    private static Result<TextReader> OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Fail<TextReader>(ErrorCode.FileNotFound, "No file path was given.");

        try
        {
            return Result.Ok<TextReader>(new StreamReader(path));
        }
        catch (FileNotFoundException)
        {
            return Result.Fail<TextReader>(ErrorCode.FileNotFound, $"File '{path}' was not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Fail<TextReader>(ErrorCode.FileNotFound, $"File '{path}' was not found because its folder does not exist.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Fail<TextReader>(ErrorCode.FileUnreadable, $"File '{path}' cannot be read: {ex.Message}");
        }
    }
}
