using LibraryLoans.Boundary;
using LibraryLoans.Core;

namespace LibraryLoans.Tests;

public sealed class NativeIntegrationTests : IDisposable
{
    private readonly string _folder;

    public NativeIntegrationTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "libraryloans-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private static Func<int, int> LoadNative() =>
        Outcome.Value(NativeFineCalculator.TryCreatePolicy(NativeFineCalculator.DefaultPath()));

    [Fact]
    public void RealNativeCall_ReturnsFeeWithGraceCapAndZero()
    {
        var native = LoadNative();

        Assert.Equal(0, native(0));
        Assert.Equal(0, native(3));            // inside the grace period
        Assert.Equal(60, native(4));           // one chargeable day
        Assert.Equal(480, native(11));         // (11 - 3) * 60
        Assert.Equal(1500, native(1000));      // capped
    }

    [Fact]
    public void NativePolicy_GivesDocumentedDifferenceFromStandardAndStrict_OnTheSameInput()
    {
        var native = LoadNative();

        Assert.Equal(550, FinePolicies.Standard(11));
        Assert.Equal(1100, FinePolicies.Strict(11));
        Assert.Equal(480, native(11));
    }

    [Fact]
    public void NativePolicy_DrivesTheRealFineWorkflow()
    {
        var native = LoadNative();
        var loans = ImmutableLoans(new Loan("L001", "Aigerim", "Clean Code", new DateOnly(2026, 11, 3), null));

        var bundle = FineWorkflow.Build(loans, new DateOnly(2026, 11, 14), native, Scoring.Reminder, 0);

        var report = bundle.Fines;
        Assert.Equal(480, report.TotalFine);
        Assert.Single(report.Lines);
    }

    [Fact]
    public void MissingNativeFile_IsControlledFailure_NotException()
    {
        var result = NativeFineCalculator.TryCreatePolicy(Path.Combine(_folder, "missing.so"));

        var error = Outcome.Error(result);
        Assert.Equal(ErrorCode.NativeLibraryUnavailable, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
    }

    [Fact]
    public void FileThatIsNotANativeLibrary_IsControlledFailure()
    {
        var fake = Path.Combine(_folder, NativeFineCalculator.FileName);
        File.WriteAllText(fake, "this is not a binary");

        var result = NativeFineCalculator.TryCreatePolicy(fake);

        Assert.Equal(ErrorCode.NativeLibraryUnavailable, Outcome.Error(result).Code);
    }

    [Fact]
    public void EmptyPath_IsControlledFailure()
    {
        Assert.Equal(ErrorCode.NativeLibraryUnavailable, Outcome.Error(NativeFineCalculator.TryCreatePolicy("  ")).Code);
    }

    [Fact]
    public void NegativeDays_IsRejectedAsProgrammingError()
    {
        LoadNative();

        Assert.Throws<ArgumentOutOfRangeException>(() => NativeFineCalculator.Policy(-1));
    }

    private static System.Collections.Immutable.ImmutableArray<Loan> ImmutableLoans(params Loan[] loans) =>
        System.Collections.Immutable.ImmutableArray.Create(loans);
}
