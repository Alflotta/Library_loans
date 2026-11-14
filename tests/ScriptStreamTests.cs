using LibraryLoans.Boundary;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public sealed class ScriptStreamTests : IDisposable
{
    private const string ThreeRows = "MEMBER,Aigerim\nBOOK,Dune,2\nCHECKOUT,L1,Aigerim,Dune,2026-10-01,7\n";

    private const string SecondRowBroken = "MEMBER,Aigerim\nBOOK,Dune,two\nCHECKOUT,L1,Aigerim,Dune,2026-10-01,7\n";

    private readonly string _folder;

    public ScriptStreamTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "libraryloans-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private sealed class TrackingReader : StringReader
    {
        public TrackingReader(string text) : base(text) { }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            base.Dispose(disposing);
        }
    }

    private sealed class Probe
    {
        private readonly string _text;

        public Probe(string text) => _text = text;

        public List<TrackingReader> Readers { get; } = new();

        public int Opens => Readers.Count;

        public Result<TextReader> Open()
        {
            var reader = new TrackingReader(_text);
            Readers.Add(reader);
            return Result.Ok<TextReader>(reader);
        }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void CreatingTheSequence_OpensNothingUntilItIsEnumerated()
    {
        var probe = new Probe(ThreeRows);

        var sequence = ScriptStream.ReadFrom(probe.Open);

        Assert.Equal(0, probe.Opens);

        var first = sequence.First();

        Assert.Equal(1, probe.Opens);
        Assert.Equal(new Operation.RegisterMember("Aigerim"), Outcome.Value(first));
    }

    [Fact]
    public void Iteration_ProducesOperationsOneByOneInFileOrder()
    {
        var probe = new Probe(ThreeRows);

        var operations = ScriptStream.ReadFrom(probe.Open).Select(Outcome.Value).ToArray();

        Assert.IsType<Operation.RegisterMember>(operations[0]);
        Assert.IsType<Operation.AddBook>(operations[1]);
        Assert.IsType<Operation.Checkout>(operations[2]);
    }

    [Fact]
    public void Materialising_ReadsOnceAndLaterUseDoesNotReadAgain()
    {
        var probe = new Probe(ThreeRows);

        var operations = Outcome.Value(ScriptStream.ReadFrom(probe.Open).CollectResults());
        var first = operations.Count();
        var second = operations.Count(o => o is Operation.Checkout);

        Assert.Equal(1, probe.Opens);
        Assert.Equal(3, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void EnumeratingTheDeferredSequenceTwice_ReadsTheSourceTwice()
    {
        var probe = new Probe(ThreeRows);
        var sequence = ScriptStream.ReadFrom(probe.Open);

        var firstPass = sequence.ToArray();
        var secondPass = sequence.ToArray();

        Assert.Equal(2, probe.Opens);
        Assert.Equal(firstPass.Length, secondPass.Length);
        Assert.All(probe.Readers, r => Assert.Equal(1, r.DisposeCalls));
    }

    [Fact]
    public void ReaderIsDisposedExactlyOnce_AfterFullSuccessfulIteration()
    {
        var probe = new Probe(ThreeRows);

        var operations = Outcome.Value(ScriptStream.ReadFrom(probe.Open).CollectResults());

        Assert.Equal(3, operations.Length);
        Assert.Equal(1, probe.Readers[0].DisposeCalls);
    }

    [Fact]
    public void ReaderIsDisposed_WhenTheConsumerStopsEarly()
    {
        var probe = new Probe(ThreeRows);

        var firstOnly = ScriptStream.ReadFrom(probe.Open).Take(1).ToArray();

        Assert.Single(firstOnly);
        Assert.Equal(1, probe.Readers[0].DisposeCalls);
    }

    [Fact]
    public void ReaderIsDisposed_WhenALineIsMalformedAndCollectingStops()
    {
        var probe = new Probe(SecondRowBroken);

        var error = Outcome.Error(ScriptStream.ReadFrom(probe.Open).CollectResults());

        Assert.Equal(ErrorCode.MalformedContent, error.Code);
        Assert.Contains("Line 2", error.Message);
        Assert.Equal(1, probe.Readers[0].DisposeCalls);
    }

    [Fact]
    public void ReaderIsDisposed_WhenTheConsumerThrowsInTheMiddleOfIteration()
    {
        var probe = new Probe(ThreeRows);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in ScriptStream.ReadFrom(probe.Open))
                throw new InvalidOperationException("consumer failed");
        });

        Assert.Equal(1, probe.Readers[0].DisposeCalls);
    }

    [Fact]
    public void OpenFailure_IsOneFailureResultAndNothingIsLeftToDispose()
    {
        var failure = Result.Fail<TextReader>(ErrorCode.FileNotFound, "cannot open");

        var results = ScriptStream.ReadFrom(() => failure).ToArray();

        Assert.Equal(ErrorCode.FileNotFound, Outcome.Error(Assert.Single(results)).Code);
    }

    [Fact]
    public void Snapshot_BlankLinesAreSkippedAndAnEmptyScriptIsAnEmptyArray()
    {
        var withBlank = WriteFile("blank.csv", "MEMBER,Aigerim\n\n   \nBOOK,Dune,1\n");
        var empty = WriteFile("empty.csv", "");

        var operations = Outcome.Value(FileBoundary.SnapshotOperations(withBlank));

        Assert.Equal(2, operations.Length);
        Assert.Empty(Outcome.Value(FileBoundary.SnapshotOperations(empty)));
    }

    [Fact]
    public void Snapshot_MissingFileAndEmptyPathAreSafeFailures()
    {
        var missing = Outcome.Error(FileBoundary.SnapshotOperations(Path.Combine(_folder, "nope.csv")));
        var noFolder = Outcome.Error(FileBoundary.SnapshotOperations(Path.Combine(_folder, "no-folder", "x.csv")));
        var blank = Outcome.Error(FileBoundary.SnapshotOperations("  "));

        Assert.Equal(ErrorCode.FileNotFound, missing.Code);
        Assert.Equal(ErrorCode.FileNotFound, noFolder.Code);
        Assert.Equal(ErrorCode.FileNotFound, blank.Code);
        Assert.Equal(ErrorKind.Operational, missing.Kind);
    }

    [Fact]
    public void Snapshot_MalformedContentIsASafeFailureThatNamesTheFileAndTheLine()
    {
        var path = WriteFile("bad.csv", SecondRowBroken);

        var error = Outcome.Error(FileBoundary.SnapshotOperations(path));

        Assert.Equal(ErrorCode.MalformedContent, error.Code);
        Assert.Contains("bad.csv", error.Message);
        Assert.Contains("Line 2", error.Message);
    }

    [Fact]
    public void Snapshot_FileHandleIsReleasedAfterSuccessAndAfterFailure()
    {
        var good = WriteFile("good.csv", ThreeRows);
        var bad = WriteFile("bad.csv", SecondRowBroken);

        Outcome.Value(FileBoundary.SnapshotOperations(good));
        Outcome.Error(FileBoundary.SnapshotOperations(bad));

        using (new FileStream(good, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        using (new FileStream(bad, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        File.Delete(good);
        File.Delete(bad);
        Assert.False(File.Exists(good));
        Assert.False(File.Exists(bad));
    }

    [Fact]
    public void SafeWriter_OverwritesAnExistingFileReleasesItAndLeavesNoTemporaryFile()
    {
        var path = WriteFile("report.txt", "old");

        var saved = Outcome.Value(SafeFileWriter.WriteLines(path, new[] { "new", "lines" }));

        Assert.Equal(path, saved);
        Assert.Equal(new[] { "new", "lines" }, File.ReadAllLines(path));
        Assert.False(File.Exists(path + ".tmp"));
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public void SafeWriter_UnwritableLocationAndEmptyPathBecomeExportFailed()
    {
        var blocker = WriteFile("blocker", "a file, not a folder");

        var underFile = Outcome.Error(SafeFileWriter.WriteLines(Path.Combine(blocker, "x.txt"), new[] { "x" }));
        var blank = Outcome.Error(SafeFileWriter.WriteLines(" ", new[] { "x" }));

        Assert.Equal(ErrorCode.ExportFailed, underFile.Code);
        Assert.Equal(ErrorCode.ExportFailed, blank.Code);
        Assert.Equal(ErrorKind.Operational, underFile.Kind);
        Assert.Equal(new[] { "a file, not a folder" }, File.ReadAllLines(blocker));
    }
}
