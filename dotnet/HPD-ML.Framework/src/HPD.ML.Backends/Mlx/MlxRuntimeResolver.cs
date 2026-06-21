namespace HPD.ML.Backends.Mlx;

public static class MlxRuntimeResolver
{
    public const string HeliumMlxLibraryPathEnvironmentVariable = "HELIUM_MLX_LIBRARY_PATH";

    public static MlxRuntimeResolution Resolve(MlxRuntimeOptions? options = null)
    {
        options ??= new MlxRuntimeOptions();
        var searched = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.NativeLibraryPath))
            return ResolveCandidate(options.NativeLibraryPath, "explicit native library path", searched);

        var environmentPath = Environment.GetEnvironmentVariable(HeliumMlxLibraryPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
            return ResolveCandidate(environmentPath, HeliumMlxLibraryPathEnvironmentVariable, searched);

        foreach (var candidate in RuntimeCandidates(options))
        {
            searched.Add(candidate);
            if (File.Exists(candidate))
            {
                return new MlxRuntimeResolution
                {
                    IsAvailable = true,
                    LibraryPath = Path.GetFullPath(candidate),
                    Source = "prepared runtime",
                    SearchedPaths = searched
                };
            }
        }

        return new MlxRuntimeResolution
        {
            IsAvailable = false,
            ReasonUnavailable =
                $"No MLX C runtime was found. Set {HeliumMlxLibraryPathEnvironmentVariable}, " +
                "pass NativeLibraryPath, or run tools/prepare-mlx-runtime.cs.",
            SearchedPaths = searched
        };
    }

    private static MlxRuntimeResolution ResolveCandidate(string path, string source, List<string> searched)
    {
        var fullPath = Path.GetFullPath(path);
        searched.Add(fullPath);

        if (!File.Exists(fullPath))
        {
            return new MlxRuntimeResolution
            {
                IsAvailable = false,
                Source = source,
                ReasonUnavailable = $"Configured MLX runtime path does not exist: {fullPath}",
                SearchedPaths = searched
            };
        }

        return new MlxRuntimeResolution
        {
            IsAvailable = true,
            LibraryPath = fullPath,
            Source = source,
            SearchedPaths = searched
        };
    }

    private static IEnumerable<string> RuntimeCandidates(MlxRuntimeOptions options)
    {
        var root = string.IsNullOrWhiteSpace(options.SearchRoot)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(options.SearchRoot);

        var rid = CurrentRuntimeIdentifier();
        foreach (var fileName in NativeFileNames())
        {
            yield return Path.Combine(root, "artifacts", "mlx", rid, "native", fileName);
            yield return Path.Combine(root, "runtimes", rid, "native", fileName);
            yield return Path.Combine(root, "runtimes", "any", "native", fileName);
            yield return Path.Combine(root, fileName);
        }
    }

    private static IReadOnlyList<string> NativeFileNames()
    {
        if (OperatingSystem.IsWindows())
            return new[] { "mlxc.dll", "libmlxc.dll", "MLX.NativeAOT.dll" };

        if (OperatingSystem.IsMacOS())
            return new[] { "libmlxc.dylib", "MLX.NativeAOT.dylib" };

        return new[] { "libmlxc.so", "MLX.NativeAOT.so" };
    }

    internal static string CurrentRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{arch}";
    }
}

