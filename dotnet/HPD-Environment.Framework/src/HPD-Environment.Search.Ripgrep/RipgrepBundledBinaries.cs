namespace HPD.Environment.Search.Ripgrep;

/// <summary>One ripgrep bundle embedded in this package.</summary>
internal sealed record BundledRipgrepBinaryManifest
{
    /// <summary>Gets the Environment target identity.</summary>
    public required string Target { get; init; }

    /// <summary>Gets the package resource containing the tar archive.</summary>
    public required string ResourceName { get; init; }

    /// <summary>Gets the upstream ripgrep version.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the SHA-256 digest of the bundle.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Gets the executable path relative to the extracted bundle.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Gets the optional dynamic loader path in the bundle.</summary>
    public string? LoaderPath { get; init; }
}

/// <summary>Generated manifest of bundled ripgrep artifacts.</summary>
internal static partial class RipgrepBundledBinaries
{
    internal static IReadOnlyList<BundledRipgrepBinaryManifest>
        Manifest { get; } = CreateManifest();

    private static IReadOnlyList<BundledRipgrepBinaryManifest>
        CreateManifest()
    {
        var binaries = new List<BundledRipgrepBinaryManifest>();
        AddBundledBinaries(binaries);
        return binaries;
    }

    static partial void AddBundledBinaries(
        List<BundledRipgrepBinaryManifest> binaries);
}
