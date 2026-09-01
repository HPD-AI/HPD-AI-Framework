namespace HPD.Payments.Tools.Conformance;

/// <summary>Defines the complete source closure consumed by a Payments release proof.</summary>
internal static class SourceInventoryPolicy
{
    internal static readonly string[] IncludedPaths =
    [
        "HPD-Payments.Framework/src",
        "HPD-Payments.Framework/test",
        "HPD-Payments.Framework/perf",
        "HPD-Payments.Framework/eng/registry",
        "HPD-Payments.Framework/eng/commands",
        "HPD-Payments.Framework/eng/build",
        "HPD-Payments.Framework/Directory.Build.props",
        "HPD-Payments.Framework/Directory.Build.targets",
        "HPD-Payments.Framework/Directory.Packages.props",
        "HPD-Payments.Framework/HPD-Payments.slnx",
        "HPD-Base.Framework",
        "shared/src/HPD-Events",
    ];

    internal static string DotnetRoot(string productRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productRoot);
        var product = Path.GetFullPath(productRoot);
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(product), "HPD-Payments.Framework"))
            throw new InvalidDataException("Payments proof root has an unexpected identity.");
        return Directory.GetParent(product)?.FullName ?? throw new InvalidDataException("Payments dotnet root is absent.");
    }

    internal static SourceTreeSnapshot Capture(string productRoot) =>
        SourceTreeSnapshotter.Capture(DotnetRoot(productRoot), IncludedPaths);

    internal static string RequireCleanStatus(string porcelainOutput)
    {
        ArgumentNullException.ThrowIfNull(porcelainOutput);
        if (porcelainOutput.Length != 0)
            throw new InvalidDataException(
                $"Release source closure is dirty ({porcelainOutput.Count(static character => character == '\n')} entries).");
        return "clean:sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855;entries=0";
    }
}
