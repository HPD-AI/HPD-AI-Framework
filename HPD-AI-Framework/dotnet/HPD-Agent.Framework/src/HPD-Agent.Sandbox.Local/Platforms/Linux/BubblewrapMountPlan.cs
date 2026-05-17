namespace HPD.Sandbox.Local.Platforms.Linux;

internal enum BubblewrapMountKind
{
    Bind,
    ReadOnlyBind,
    Tmpfs,
}

internal sealed record BubblewrapMount(
    BubblewrapMountKind Kind,
    string? SourcePath,
    string DestinationPath);

internal sealed record BubblewrapMountPlan(
    IReadOnlyList<BubblewrapMount> Mounts,
    IReadOnlyList<string> CleanupPaths)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static BubblewrapMountPlan Empty { get; } = new([], []);
}
