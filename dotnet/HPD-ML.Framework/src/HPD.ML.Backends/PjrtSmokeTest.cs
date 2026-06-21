namespace HPD.ML.Backends.Pjrt;

public static class PjrtSmokeTest
{
    private static readonly float[] ExpectedMatMul2x2 = [19.0f, 22.0f, 43.0f, 50.0f];

    public static bool TryRunLocalMatMulMilestone(
        PjrtPluginResolverOptions options,
        out PjrtMatMulSmokeResult? result,
        out string? reasonUnavailable)
    {
        var resolution = PjrtPluginResolver.Resolve(options);
        if (!resolution.IsAvailable || resolution.LibraryPath is null)
        {
            result = null;
            reasonUnavailable = resolution.ReasonUnavailable ?? "PJRT plugin is unavailable.";
            return false;
        }

        var disposed = false;
        PjrtFloatBackend? backend = null;
        try
        {
            backend = PjrtFloatBackend.Create(new PjrtPluginResolverOptions
            {
                ExplicitPath = resolution.LibraryPath,
                Backend = options.Backend,
                ClientOptions = options.ClientOptions
            });

            var pluginInfo = backend.PluginInfo;
            var clientInfo = backend.ClientInfo;

            using var a = backend.CreateMatrix(2, 2, [1.0f, 2.0f, 3.0f, 4.0f]);
            using var b = backend.CreateMatrix(2, 2, [5.0f, 6.0f, 7.0f, 8.0f]);
            using var product = backend.MatMul(a, b);

            var output = product.ToArray();
            var executableCount = backend.CachedExecutableCount;

            backend.Dispose();
            disposed = true;

            result = new PjrtMatMulSmokeResult
            {
                Resolution = resolution,
                PluginInfo = pluginInfo,
                ClientInfo = clientInfo,
                Output = output,
                Expected = ExpectedMatMul2x2.ToArray(),
                OutputMatchesExpected = output.SequenceEqual(ExpectedMatMul2x2),
                CachedExecutableCount = executableCount,
                BackendDisposed = disposed
            };
            reasonUnavailable = null;
            return true;
        }
        finally
        {
            if (!disposed)
                backend?.Dispose();
        }
    }

}

public sealed record PjrtMatMulSmokeResult
{
    public required PjrtPluginResolution Resolution { get; init; }
    public required PjrtPluginInfo PluginInfo { get; init; }
    public required PjrtClientInfo ClientInfo { get; init; }
    public required float[] Output { get; init; }
    public required float[] Expected { get; init; }
    public required bool OutputMatchesExpected { get; init; }
    public required int CachedExecutableCount { get; init; }
    public required bool BackendDisposed { get; init; }
}
