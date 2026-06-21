using System.Runtime.InteropServices;

namespace HPD.ML.Backends.Pjrt;

internal static class PjrtGpuRuntimeLoader
{
    public const string CudaLibraryPathsEnvironmentVariable = "HELIUM_CUDA_LIBRARY_PATHS";
    public const string RocmLibraryPathsEnvironmentVariable = "HELIUM_ROCM_LIBRARY_PATHS";

    private static readonly string[] CudaLibraries =
    [
        "libcudart.so.12",
        "libcudart.so.13",
        "libnvrtc.so.12",
        "libnvrtc.so.13",
        "libcublas.so.12",
        "libcublasLt.so.12",
        "libcublas.so.13",
        "libcublasLt.so.13",
        "libcudnn.so.9",
        "libcufft.so.11",
        "libcufft.so.12",
        "libcusolver.so.11",
        "libcusolver.so.12",
        "libcusparse.so.12",
        "libnccl.so.2",
        "libcupti.so.12",
        "libcupti.so.13",
        "libnvshmem_host.so.3"
    ];

    private static readonly string[] RocmLibraries =
    [
        "libamdhip64.so",
        "libhipblas.so",
        "libhipsparse.so",
        "libhipsolver.so",
        "libhipfft.so",
        "libMIOpen.so",
        "librccl.so"
    ];

    public static void PreloadForBackend(string backend)
    {
        var normalized = NormalizeBackend(backend);
        if (normalized == "cuda")
        {
            Preload(CudaLibraryPathsEnvironmentVariable, CudaLibraries, CandidateCudaRoots());
        }
        else if (normalized == "rocm")
        {
            Preload(RocmLibraryPathsEnvironmentVariable, RocmLibraries, CandidateRocmRoots());
        }
    }

    private static void Preload(string environmentVariable, IReadOnlyList<string> libraryNames, IEnumerable<string> roots)
    {
        var explicitPaths = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPaths))
        {
            foreach (var entry in explicitPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                LoadPath(entry, libraryNames, throwOnFailure: true);
        }

        foreach (var root in roots)
            LoadPath(root, libraryNames, throwOnFailure: false);
    }

    private static void LoadPath(string path, IReadOnlyList<string> libraryNames, bool throwOnFailure)
    {
        if (File.Exists(path))
        {
            LoadLibrary(path, throwOnFailure);
            return;
        }

        if (!Directory.Exists(path))
        {
            if (throwOnFailure)
                throw new DirectoryNotFoundException($"Configured GPU library path does not exist: {path}");
            return;
        }

        foreach (var libraryName in libraryNames)
        {
            var candidate = Path.Combine(path, libraryName);
            if (File.Exists(candidate))
                LoadLibrary(candidate, throwOnFailure: false);
        }
    }

    private static void LoadLibrary(string path, bool throwOnFailure)
    {
        try
        {
            NativeLibrary.Load(path);
        }
        catch when (!throwOnFailure)
        {
        }
    }

    private static IEnumerable<string> CandidateCudaRoots()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH")
            ?? Environment.GetEnvironmentVariable("CUDA_HOME")
            ?? Environment.GetEnvironmentVariable("CUDA_ROOT");
        if (!string.IsNullOrWhiteSpace(cudaPath))
        {
            yield return Path.Combine(cudaPath, "lib64");
            yield return Path.Combine(cudaPath, "lib");
        }
    }

    private static IEnumerable<string> CandidateRocmRoots()
    {
        var rocmPath = Environment.GetEnvironmentVariable("ROCM_PATH") ?? "/opt/rocm";
        if (!string.IsNullOrWhiteSpace(rocmPath))
        {
            yield return Path.Combine(rocmPath, "lib");
            yield return Path.Combine(rocmPath, "lib64");
        }
    }

    private static string NormalizeBackend(string backend)
    {
        var normalized = backend.Trim().ToLowerInvariant();
        return normalized == "gpu" ? "cuda" : normalized;
    }
}
