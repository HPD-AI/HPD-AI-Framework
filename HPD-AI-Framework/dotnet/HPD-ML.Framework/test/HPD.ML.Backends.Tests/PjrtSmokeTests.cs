using HPD.ML.Backends.Pjrt;

namespace HPD.ML.Backends.Tests;

public sealed class PjrtSmokeTests
{
    [Fact]
    public void RawPjrtWrappers_AreNotPublicApi()
    {
        var exportedTypeNames = typeof(PjrtFloatBackend).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("PjrtPlugin", exportedTypeNames);
        Assert.DoesNotContain("PjrtClient", exportedTypeNames);
        Assert.DoesNotContain("PjrtBuffer", exportedTypeNames);
        Assert.DoesNotContain("PjrtLoadedExecutable", exportedTypeNames);
    }

    [Fact]
    public void LocalMatMulMilestone_ReturnsUnavailableForMissingRuntime()
    {
        var ran = PjrtSmokeTest.TryRunLocalMatMulMilestone(
            new PjrtPluginResolverOptions
            {
                SearchRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                Backend = "cpu"
            },
            out var result,
            out var reasonUnavailable);

        Assert.False(ran);
        Assert.Null(result);
        Assert.False(string.IsNullOrWhiteSpace(reasonUnavailable));
    }

    [Theory]
    [InlineData("cuda", "pjrt_c_api_gpu_plugin.so")]
    [InlineData("cuda", "xla_cuda_plugin.so")]
    [InlineData("rocm", "pjrt_c_api_gpu_plugin.so")]
    [InlineData("rocm", "xla_rocm_plugin.so")]
    public void Resolver_FindsGpuPreparedRuntimeLayouts(string backend, string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var rid = CurrentRuntimeIdentifier();
            var nativeDirectory = Path.Combine(root, "runtimes", rid, "native");
            Directory.CreateDirectory(nativeDirectory);
            var pluginPath = Path.Combine(nativeDirectory, fileName);
            File.WriteAllBytes(pluginPath, [0]);

            var resolution = PjrtPluginResolver.Resolve(new PjrtPluginResolverOptions
            {
                SearchRoot = root,
                Backend = backend
            });

            Assert.True(resolution.IsAvailable, resolution.ReasonUnavailable);
            Assert.Equal(Path.GetFullPath(pluginPath), resolution.LibraryPath);
            Assert.Equal("prepared runtime", resolution.Source);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolver_GpuAliasResolvesCudaPlugin()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var nativeDirectory = Path.Combine(root, "runtimes", CurrentRuntimeIdentifier(), "native");
            Directory.CreateDirectory(nativeDirectory);
            var pluginPath = Path.Combine(nativeDirectory, "xla_cuda_plugin.so");
            File.WriteAllBytes(pluginPath, [0]);

            var resolution = PjrtPluginResolver.Resolve(new PjrtPluginResolverOptions
            {
                SearchRoot = root,
                Backend = "gpu"
            });

            Assert.True(resolution.IsAvailable, resolution.ReasonUnavailable);
            Assert.Equal(Path.GetFullPath(pluginPath), resolution.LibraryPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocalMatMulMilestone_WhenPreparedRuntimeExists()
    {
        var runtimeRoot = FindPreparedRuntimeRoot();
        if (runtimeRoot is null)
            return;

        var ran = PjrtSmokeTest.TryRunLocalMatMulMilestone(
            new PjrtPluginResolverOptions
            {
                SearchRoot = runtimeRoot,
                Backend = "cpu"
            },
            out var result,
            out var reasonUnavailable);

        Assert.True(ran, reasonUnavailable);
        Assert.NotNull(result);
        Assert.True(result.OutputMatchesExpected);
        Assert.Equal([19.0f, 22.0f, 43.0f, 50.0f], result.Output);
        Assert.Equal(result.Expected, result.Output);
        Assert.True(result.ClientInfo.DeviceCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.ClientInfo.PlatformName));
        Assert.False(string.IsNullOrWhiteSpace(result.PluginInfo.LibraryPath));
        Assert.Equal(1, result.CachedExecutableCount);
        Assert.True(result.BackendDisposed);
    }

    private static string? FindPreparedRuntimeRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "artifacts", "pjrt");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
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
