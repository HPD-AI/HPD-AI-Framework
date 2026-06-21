namespace HPD.ML.Backends.Pjrt;

internal static class PjrtClientCreateOptionDefaults
{
    public static PjrtPluginResolverOptions WithBackendDefaults(PjrtPluginResolverOptions options)
    {
        var backend = options.Backend.Trim().ToLowerInvariant();
        if (backend is not ("cuda" or "rocm" or "gpu"))
            return options;

        var clientOptions = options.ClientOptions ?? new PjrtClientCreateOptions();
        if (backend == "rocm" && string.IsNullOrWhiteSpace(clientOptions.PlatformName))
            clientOptions = clientOptions with { PlatformName = "ROCM" };

        return ReferenceEquals(clientOptions, options.ClientOptions)
            ? options
            : options with { ClientOptions = clientOptions };
    }
}
