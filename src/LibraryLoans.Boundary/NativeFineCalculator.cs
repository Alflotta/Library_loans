using System.Runtime.InteropServices;
using LibraryLoans.Core;

namespace LibraryLoans.Boundary;
public static class NativeFineCalculator
{
    public const string LibraryName = "libraryloans_native";
    public const int ExpectedAbiVersion = 1;

    public const int PerDay = 60;
    public const int Cap = 1500;
    public const int GraceDays = 3;

    private static readonly object Gate = new();
    private static IntPtr _handle = IntPtr.Zero;
    private static bool _resolverInstalled;

    private static class Native
    {
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ll_abi_version();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ll_fine(int daysLate, int perDay, int cap, int graceDays);
    }


    public static string FileName =>
        OperatingSystem.IsWindows() ? LibraryName + ".dll"
        : OperatingSystem.IsMacOS() ? LibraryName + ".dylib"
        : LibraryName + ".so";

    public static string DefaultPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LIBRARYLOANS_NATIVE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName);
    }


    public static Result<Func<int, int>> TryCreatePolicy(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Fail("No path to the native library was given.");
        if (!File.Exists(path))
            return Fail($"Native library '{path}' was not found. Build it with native/build.sh or native/build.ps1.");

        try
        {
            lock (Gate)
            {
                if (!NativeLibrary.TryLoad(path, out var handle))
                    return Fail($"File '{path}' could not be loaded as a native library for this platform.");
                if (_handle == IntPtr.Zero)
                {
                    InstallResolver();
                    _handle = handle;
                }
            }

            var version = Native.ll_abi_version();
            if (version != ExpectedAbiVersion)
                return Fail($"Native library ABI version is {version}, expected {ExpectedAbiVersion}.");

            return Result.Ok<Func<int, int>>(Policy);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Fail($"Native library '{path}' is not usable: {ex.Message}");
        }
    }

   
    public static int Policy(int daysLate)
    {
        var fine = Native.ll_fine(daysLate, PerDay, Cap, GraceDays);
        if (fine < 0)
            throw new ArgumentOutOfRangeException(nameof(daysLate), "Days late must not be negative.");
        return fine;
    }

    private static void InstallResolver()
    {
        if (_resolverInstalled) return;
        NativeLibrary.SetDllImportResolver(typeof(NativeFineCalculator).Assembly, (name, _, _) =>
            name == LibraryName ? _handle : IntPtr.Zero);
        _resolverInstalled = true;
    }

    private static Result<Func<int, int>> Fail(string message) =>
        Result.Fail<Func<int, int>>(ErrorCode.NativeLibraryUnavailable, message);
}
