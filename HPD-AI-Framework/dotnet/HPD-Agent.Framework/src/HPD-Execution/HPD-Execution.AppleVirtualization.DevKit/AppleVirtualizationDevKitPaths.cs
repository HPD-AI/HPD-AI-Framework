namespace HPD.Execution.AppleVirtualization.DevKit;

public sealed record AppleVirtualizationDevKitPaths
{
    public required string FrameworkRoot { get; init; }
    public required string GuestImagePreparationScript { get; init; }
    public required string PrerequisiteCheckScript { get; init; }
    public required string RealAcceptanceTestProject { get; init; }
    public required string PreparedImageRoot { get; init; }

    public static AppleVirtualizationDevKitPaths FromFrameworkRoot(
        string frameworkRoot,
        string? preparedImageRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameworkRoot);
        string docs = Path.Combine(frameworkRoot, "docs", "apple-virtualization");
        return new()
        {
            FrameworkRoot = frameworkRoot,
            GuestImagePreparationScript = Path.Combine(docs, "guest-image", "prepare-ubuntu-qemu-image.sh"),
            PrerequisiteCheckScript = Path.Combine(docs, "scripts", "check-real-acceptance-prereqs.sh"),
            RealAcceptanceTestProject = Path.Combine(
                frameworkRoot,
                "test",
                "HPD-Execution",
                "HPD-Execution.AppleVirtualization.Tests",
                "HPD-Execution.AppleVirtualization.Tests.csproj"),
            PreparedImageRoot = preparedImageRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".hpd",
                "applevz",
                "images"),
        };
    }
}
