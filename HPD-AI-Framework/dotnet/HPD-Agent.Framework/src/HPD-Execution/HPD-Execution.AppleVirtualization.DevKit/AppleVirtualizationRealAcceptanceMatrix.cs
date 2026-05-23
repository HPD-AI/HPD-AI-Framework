namespace HPD.Execution.AppleVirtualization.DevKit;

public sealed record AppleVirtualizationRealAcceptanceMatrixEntry
{
    public required string Name { get; init; }
    public required string EnvFilePath { get; init; }
    public required AppleVirtualizationRealAcceptanceEnvironment Environment { get; init; }
    public required AppleVirtualizationDevKitValidationResult Validation { get; init; }
    public bool CanRun => Validation.IsValid;
}

public sealed record AppleVirtualizationRealAcceptanceMatrixPlan
{
    public IReadOnlyList<AppleVirtualizationRealAcceptanceMatrixEntry> Entries { get; init; } = Array.Empty<AppleVirtualizationRealAcceptanceMatrixEntry>();
    public IReadOnlyList<AppleVirtualizationDevKitDiagnostic> Diagnostics { get; init; } = Array.Empty<AppleVirtualizationDevKitDiagnostic>();
    public bool HasRunnableEntries => Entries.Any(static entry => entry.CanRun);
}

public static class AppleVirtualizationRealAcceptanceMatrix
{
    public static AppleVirtualizationRealAcceptanceMatrixPlan CreatePlan(
        string preparedImageRoot,
        AppleVirtualizationPreparedImageDiscoveryOptions? discoveryOptions = null)
    {
        AppleVirtualizationPreparedImageDiscoveryResult discovery =
            AppleVirtualizationPreparedImageDiscovery.Discover(preparedImageRoot, discoveryOptions);

        List<AppleVirtualizationRealAcceptanceMatrixEntry> entries = [];
        foreach (AppleVirtualizationPreparedImage image in discovery.Images)
        {
            entries.Add(new()
            {
                Name = Path.GetFileName(image.RootPath),
                EnvFilePath = image.EnvFilePath,
                Environment = image.Environment,
                Validation = image.Validation
            });
        }

        return new()
        {
            Entries = entries,
            Diagnostics = discovery.Diagnostics
        };
    }
}
