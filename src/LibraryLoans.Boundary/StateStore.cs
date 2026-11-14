using System.Collections.Immutable;
using System.Text.Json;
using LibraryLoans.Core;

namespace LibraryLoans.Boundary;

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static Result<LibraryState> Load(string path) =>
        FileBoundary.ReadLines(path)
            .Bind<ImmutableArray<string>, LibraryState>(lines => Parse(path, string.Join("\n", lines)));

    public static Result<string> Save(string path, LibraryState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var json = JsonSerializer.Serialize(state.ToSnapshot(), Options);
        return SafeFileWriter.WriteLines(path, json.Split('\n'));
    }

    private static Result<LibraryState> Parse(string path, string json)
    {
        StateSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<StateSnapshot>(json, Options);
        }
        catch (JsonException ex)
        {
            return Result.Fail<LibraryState>(ErrorCode.MalformedContent, $"File '{path}' is not valid state JSON: {ex.Message}");
        }

        if (snapshot is null)
            return Result.Fail<LibraryState>(ErrorCode.MalformedContent, $"File '{path}' is empty.");

        return LibraryState.Restore(snapshot)
            .MapError(error => error with { Message = $"File '{path}': {error.Message}" });
    }
}
