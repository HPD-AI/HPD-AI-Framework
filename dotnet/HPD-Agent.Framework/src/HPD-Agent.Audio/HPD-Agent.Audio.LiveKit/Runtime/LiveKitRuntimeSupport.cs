using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HPD.Agent.Audio.LiveKit;

public enum LiveKitRuntimeSupportDisposition { Qualified = 0, Unsupported = 1 }

public sealed record LiveKitRuntimeSupportCell(
    string RuntimeIdentifier,
    LiveKitRuntimeSupportDisposition Disposition,
    string SafeCode,
    string? NativeFileName,
    string? NativeSha256);

public enum LiveKitNativeArtifactDisposition { Verified = 0, Unsupported = 1, Missing = 2, HashMismatch = 3 }

public sealed record LiveKitNativeArtifactVerification(
    LiveKitNativeArtifactDisposition Disposition,
    string SafeCode,
    string? Path = null);

public static class LiveKitRuntimeSupport
{
    public const string QualifiedRuntimeIdentifier = "osx-arm64";
    public const string QualifiedNativeSha256 = "cf034115fb3b94b5682151d2d36cb5ea351e97b881cd5ac0b97d0873b2a2b1da";

    private static readonly LiveKitRuntimeSupportCell[] Matrix =
    [
        new("osx-arm64", LiveKitRuntimeSupportDisposition.Qualified, "livekit-runtime-qualified", "liblivekit_ffi.dylib", QualifiedNativeSha256),
        new("osx-x64", LiveKitRuntimeSupportDisposition.Unsupported, "livekit-runtime-unsupported", null, null),
        new("linux-arm64", LiveKitRuntimeSupportDisposition.Unsupported, "livekit-runtime-unsupported", null, null),
        new("linux-x64", LiveKitRuntimeSupportDisposition.Unsupported, "livekit-runtime-unsupported", null, null),
        new("win-arm64", LiveKitRuntimeSupportDisposition.Unsupported, "livekit-runtime-unsupported", null, null),
        new("win-x64", LiveKitRuntimeSupportDisposition.Unsupported, "livekit-runtime-unsupported", null, null)
    ];

    public static IReadOnlyList<LiveKitRuntimeSupportCell> Cells => Matrix;
    public static LiveKitRuntimeSupportCell Current => ForRuntimeIdentifier(CurrentRuntimeIdentifier());

    public static LiveKitRuntimeSupportCell ForRuntimeIdentifier(string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        return Matrix.SingleOrDefault(cell => string.Equals(cell.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal))
            ?? new(runtimeIdentifier, LiveKitRuntimeSupportDisposition.Unsupported, "livekit-runtime-unsupported", null, null);
    }

    public static LiveKitNativeArtifactVerification VerifyCurrentArtifact()
    {
        var cell = Current;
        if (cell.Disposition != LiveKitRuntimeSupportDisposition.Qualified || cell.NativeFileName is null || cell.NativeSha256 is null)
            return new(LiveKitNativeArtifactDisposition.Unsupported, cell.SafeCode);
        foreach (var candidate in Candidates(cell))
        {
            if (!File.Exists(candidate)) continue;
            using var stream = File.OpenRead(candidate);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(actual),
                    System.Text.Encoding.ASCII.GetBytes(cell.NativeSha256)))
                return new(LiveKitNativeArtifactDisposition.HashMismatch, "livekit-native-artifact-hash-mismatch", candidate);
            return new(LiveKitNativeArtifactDisposition.Verified, "livekit-native-artifact-verified", candidate);
        }
        return new(LiveKitNativeArtifactDisposition.Missing, "livekit-native-artifact-missing");
    }

    private static IEnumerable<string> Candidates(LiveKitRuntimeSupportCell cell)
    {
        yield return Path.Combine(AppContext.BaseDirectory, cell.NativeFileName!);
        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", cell.RuntimeIdentifier, "native", cell.NativeFileName!);
        var search = System.Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(search))
            foreach (var directory in search.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return Path.Combine(directory, cell.NativeFileName!);
    }

    private static string CurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsWindows() ? "win"
            : "unknown";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => "unknown"
        };
        return $"{os}-{architecture}";
    }
}

