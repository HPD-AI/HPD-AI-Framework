namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Resolves PJRT plugin libraries from explicit paths, environment variables,
/// or prepared Helium runtime folders.
/// </summary>
public static class PjrtPluginResolver
{
    public const string HeliumPluginPathEnvironmentVariable = "HELIUM_PJRT_PLUGIN_PATH";
    public const string HeliumNamedPluginPathsEnvironmentVariable = "HELIUM_PJRT_NAMES_AND_LIBRARY_PATHS";
    public const string PjrtPluginPathEnvironmentVariable = "PJRT_PLUGIN_PATH";
    public const string PjrtNamedPluginPathsEnvironmentVariable = "PJRT_NAMES_AND_LIBRARY_PATHS";

    public static PjrtPluginResolution Resolve(PjrtPluginResolverOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var backend = NormalizeBackend(options.Backend);

        if (!string.IsNullOrWhiteSpace(options.ExplicitPath))
            return ResolveCandidate(options.ExplicitPath, "explicit --library");

        var heliumPath = Environment.GetEnvironmentVariable(HeliumPluginPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(heliumPath))
            return ResolveCandidate(heliumPath, HeliumPluginPathEnvironmentVariable);

        var pjrtPath = Environment.GetEnvironmentVariable(PjrtPluginPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(pjrtPath))
            return ResolveCandidate(pjrtPath, PjrtPluginPathEnvironmentVariable);

        var heliumNamedPaths = ResolveNamedPathEnvironmentVariable(
            Environment.GetEnvironmentVariable(HeliumNamedPluginPathsEnvironmentVariable),
            backend,
            HeliumNamedPluginPathsEnvironmentVariable);
        if (heliumNamedPaths.IsAvailable || heliumNamedPaths.ReasonUnavailable is not null)
            return heliumNamedPaths;

        var pjrtNamedPaths = ResolveNamedPathEnvironmentVariable(
            Environment.GetEnvironmentVariable(PjrtNamedPluginPathsEnvironmentVariable),
            backend,
            PjrtNamedPluginPathsEnvironmentVariable);
        if (pjrtNamedPaths.IsAvailable || pjrtNamedPaths.ReasonUnavailable is not null)
            return pjrtNamedPaths;

        foreach (var candidate in RuntimeCandidates(options))
        {
            if (File.Exists(candidate))
            {
                return new PjrtPluginResolution
                {
                    IsAvailable = true,
                    LibraryPath = Path.GetFullPath(candidate),
                    Source = "prepared runtime"
                };
            }
        }

        return new PjrtPluginResolution
        {
            IsAvailable = false,
            ReasonUnavailable =
                $"No PJRT {backend} plugin was found. Set {HeliumPluginPathEnvironmentVariable}, " +
                $"set {PjrtPluginPathEnvironmentVariable}, set {HeliumNamedPluginPathsEnvironmentVariable}, " +
                "pass --library, or run tools/prepare-pjrt-runtime.cs."
        };
    }

    private static PjrtPluginResolution ResolveCandidate(string path, string source)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return new PjrtPluginResolution
            {
                IsAvailable = false,
                Source = source,
                ReasonUnavailable = $"Configured PJRT plugin path does not exist: {fullPath}"
            };
        }

        return new PjrtPluginResolution
        {
            IsAvailable = true,
            LibraryPath = fullPath,
            Source = source
        };
    }

    private static IEnumerable<string> RuntimeCandidates(PjrtPluginResolverOptions options)
    {
        var root = string.IsNullOrWhiteSpace(options.SearchRoot)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(options.SearchRoot);

        var backend = NormalizeBackend(options.Backend);
        var fileNames = NativeFileNames(backend);
        var rid = CurrentRuntimeIdentifier();

        foreach (var fileName in fileNames)
        {
            yield return Path.Combine(root, "runtimes", rid, "native", fileName);
            yield return Path.Combine(root, "runtimes", "any", "native", fileName);
            yield return Path.Combine(root, "xla_plugins", $"xla_{backend}_pjrt", fileName);
            yield return Path.Combine(root, "xla_plugins", $"xla_{backend}", fileName);
        }
    }

    private static IReadOnlyList<string> NativeFileNames(string backend)
    {
        var names = backend switch
        {
            "cpu" => new[] { "pjrt_c_api_cpu_plugin.so", "xla_cpu_pjrt.so" },
            "cuda" => new[] { "pjrt_c_api_gpu_plugin.so", "xla_cuda_plugin.so" },
            "rocm" => new[] { "pjrt_c_api_gpu_plugin.so", "xla_rocm_plugin.so" },
            _ => new[] { $"pjrt_c_api_{backend}_plugin.so", $"xla_{backend}_pjrt.so" }
        };

        if (OperatingSystem.IsWindows())
            return names.SelectMany(name => new[] { name, Path.ChangeExtension(name, ".dll") }).Distinct(StringComparer.Ordinal).ToArray();
        if (OperatingSystem.IsMacOS())
            return names.SelectMany(name => new[] { name, Path.ChangeExtension(name, ".dylib") }).Distinct(StringComparer.Ordinal).ToArray();

        return names;
    }

    private static PjrtPluginResolution ResolveNamedPathEnvironmentVariable(string? value, string backend, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new PjrtPluginResolution { IsAvailable = false };

        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf(Path.PathSeparator);
            if (separator <= 0 || separator == entry.Length - 1)
            {
                return new PjrtPluginResolution
                {
                    IsAvailable = false,
                    Source = source,
                    ReasonUnavailable = $"Invalid {source} entry '{entry}'. Expected name{Path.PathSeparator}path."
                };
            }

            var name = NormalizeBackend(entry[..separator]);
            if (name != backend)
                continue;

            return ResolveCandidate(entry[(separator + 1)..], source);
        }

        return new PjrtPluginResolution { IsAvailable = false };
    }

    private static string NormalizeBackend(string backend)
    {
        var normalized = backend.Trim().ToLowerInvariant();
        return normalized == "gpu" ? "cuda" : normalized;
    }

    private static string CurrentRuntimeIdentifier()
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
