namespace HPD.Execution.AppleVirtualization.DevKit;

public enum AppleVirtualizationCleanupTargetKind
{
    SerialLog,
    ScratchDiskDirectory
}

public sealed record AppleVirtualizationCleanupTarget
{
    public required AppleVirtualizationCleanupTargetKind Kind { get; init; }
    public required string Path { get; init; }
    public bool Exists { get; init; }
}

public sealed record AppleVirtualizationCleanupPlan
{
    public IReadOnlyList<AppleVirtualizationCleanupTarget> Targets { get; init; } = Array.Empty<AppleVirtualizationCleanupTarget>();
}

public static class AppleVirtualizationCleanupPlanner
{
    public static AppleVirtualizationCleanupPlan CreatePlan(AppleVirtualizationRealAcceptanceEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        List<AppleVirtualizationCleanupTarget> targets = [];
        if (!string.IsNullOrWhiteSpace(environment.GuestSerialLogPath))
        {
            targets.Add(new()
            {
                Kind = AppleVirtualizationCleanupTargetKind.SerialLog,
                Path = environment.GuestSerialLogPath,
                Exists = File.Exists(environment.GuestSerialLogPath)
            });
        }

        string? diskDirectory = Path.GetDirectoryName(environment.GuestDiskPath);
        if (!string.IsNullOrWhiteSpace(diskDirectory))
        {
            string scratchDirectory = Path.Combine(diskDirectory, ".hpd-real-acceptance-scratch");
            targets.Add(new()
            {
                Kind = AppleVirtualizationCleanupTargetKind.ScratchDiskDirectory,
                Path = scratchDirectory,
                Exists = Directory.Exists(scratchDirectory)
            });
        }

        return new() { Targets = targets };
    }
}

public sealed record AppleVirtualizationCleanupResult
{
    public required AppleVirtualizationCleanupPlan Plan { get; init; }
    public IReadOnlyList<AppleVirtualizationDevKitDiagnostic> Diagnostics { get; init; } =
        Array.Empty<AppleVirtualizationDevKitDiagnostic>();
    public bool Succeeded => Diagnostics.All(static diagnostic => diagnostic.Severity != AppleVirtualizationDevKitDiagnosticSeverity.Error);
}

public static class AppleVirtualizationCleanupExecutor
{
    public static AppleVirtualizationCleanupResult Execute(AppleVirtualizationCleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        List<AppleVirtualizationDevKitDiagnostic> diagnostics = [];
        foreach (AppleVirtualizationCleanupTarget target in plan.Targets)
        {
            try
            {
                if (target.Kind == AppleVirtualizationCleanupTargetKind.SerialLog)
                {
                    if (File.Exists(target.Path))
                    {
                        File.Delete(target.Path);
                    }

                    continue;
                }

                if (target.Kind == AppleVirtualizationCleanupTargetKind.ScratchDiskDirectory &&
                    Directory.Exists(target.Path))
                {
                    Directory.Delete(target.Path, recursive: true);
                }
            }
            catch (IOException ex)
            {
                diagnostics.Add(AppleVirtualizationRealAcceptanceEnvironment.Error(
                    "AppleVirtualization.DevKit.CleanupFailed",
                    ex.Message,
                    path: target.Path));
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics.Add(AppleVirtualizationRealAcceptanceEnvironment.Error(
                    "AppleVirtualization.DevKit.CleanupFailed",
                    ex.Message,
                    path: target.Path));
            }
        }

        return new() { Plan = plan, Diagnostics = diagnostics };
    }
}
