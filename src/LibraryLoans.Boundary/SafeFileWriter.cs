using System.Text;
using LibraryLoans.Core;

namespace LibraryLoans.Boundary;

public static class SafeFileWriter
{
    public static Result<string> WriteLines(string path, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (string.IsNullOrWhiteSpace(path))
            return Result.Fail<string>(ErrorCode.ExportFailed, "No export path was given.");

        var temp = path + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                foreach (var line in lines)
                    writer.WriteLine(line);
            }

            File.Move(temp, path, true);
            return Result.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DeleteQuietly(temp);
            return Result.Fail<string>(ErrorCode.ExportFailed, $"File cannot be written to '{path}': {ex.Message}");
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }
}
