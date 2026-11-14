using System.Runtime.InteropServices;
using LibraryLoans.Core;

namespace LibraryLoans.Boundary;

/// <summary>
/// Adapter between the pure core and the native C library (native/libraryloans_native.c).
/// P/Invoke is a side effect (it loads a binary and runs foreign code), so it lives in the Boundary project.
/// The core only sees an ordinary Func of int to int, exactly like FinePolicies.Standard.
/// </summary>
public static class NativeFineCalculator
{
    public const string LibraryName = "libraryloans_native";
    public const int ExpectedAbiVersion = 1;

    // Parameters of the "native" fine policy (documented in README section 14).
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

    /// <summary>File name of the library for the current operating system.</summary>
    public static string FileName =>
        OperatingSystem.IsWindows() ? LibraryName + ".dll"
        : OperatingSystem.IsMacOS() ? LibraryName + ".dylib"
        : LibraryName + ".so";

    /// <summary>
    /// Path where the library is searched: the LIBRARYLOANS_NATIVE environment variable if set,
    /// otherwise runtimes/RID/native next to the program (copied by the project files).
    /// </summary>
    public static string DefaultPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LIBRARYLOANS_NATIVE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", FileName);
    }

    /// <summary>
    /// Loads the library and returns the native fine policy. Every expected failure is a Result:
    /// missing file, a file that is not a valid library, a missing function, a wrong ABI version.
    /// The loaded library handle belongs to the process and is released when the process ends
    /// (the P/Invoke binding is cached by the runtime, so unloading it earlier is unsafe).
    /// </summary>
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
                // Every call checks the given file itself, so a bad path fails even if another library was loaded earlier.
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

    /// <summary>The native fine in KZT for the number of days late. Calls the C function ll_fine.</summary>
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
