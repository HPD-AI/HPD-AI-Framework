namespace HPD.Execution.AppleVirtualization.DevKit;

public sealed record AppleVirtualizationPreparedImageDiscoveryOptions
{
    public string EnvFileName { get; init; } = "hpd-applevz-real.env";
    public bool ValidateFileSystem { get; init; }
}

public sealed record AppleVirtualizationPreparedImage
{
    public required string RootPath { get; init; }
    public required string EnvFilePath { get; init; }
    public required AppleVirtualizationRealAcceptanceEnvironment Environment { get; init; }
    public required AppleVirtualizationDevKitValidationResult Validation { get; init; }
}

public sealed record AppleVirtualizationPreparedImageDiscoveryResult
{
    public IReadOnlyList<AppleVirtualizationPreparedImage> Images { get; init; } = Array.Empty<AppleVirtualizationPreparedImage>();
    public IReadOnlyList<AppleVirtualizationDevKitDiagnostic> Diagnostics { get; init; } = Array.Empty<AppleVirtualizationDevKitDiagnostic>();
}

public static class AppleVirtualizationPreparedImageDiscovery
{
    public static AppleVirtualizationPreparedImageDiscoveryResult Discover(
        string rootPath,
        AppleVirtualizationPreparedImageDiscoveryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        options ??= new();

        List<AppleVirtualizationPreparedImage> images = [];
        List<AppleVirtualizationDevKitDiagnostic> diagnostics = [];
        if (!Directory.Exists(rootPath))
        {
            diagnostics.Add(AppleVirtualizationRealAcceptanceEnvironment.Error(
                "AppleVirtualization.DevKit.PreparedImageRootMissing",
                "The prepared image root does not exist.",
                path: rootPath));
            return new() { Diagnostics = diagnostics };
        }

        foreach (string envFile in Directory.EnumerateFiles(rootPath, options.EnvFileName, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            AppleVirtualizationRealAcceptanceEnvironmentLoadResult loaded = AppleVirtualizationRealAcceptanceEnvironment.Load(envFile);
            if (loaded.Environment is null)
            {
                diagnostics.AddRange(loaded.Validation.Diagnostics);
                continue;
            }

            AppleVirtualizationDevKitValidationResult validation =
                AppleVirtualizationRealAcceptanceValidator.Validate(
                    loaded.Environment,
                    new AppleVirtualizationRealAcceptanceValidationOptions { CheckFileSystem = options.ValidateFileSystem });

            images.Add(new()
            {
                RootPath = Path.GetDirectoryName(envFile) ?? rootPath,
                EnvFilePath = envFile,
                Environment = loaded.Environment,
                Validation = validation
            });
        }

        return new() { Images = images, Diagnostics = diagnostics };
    }
}
