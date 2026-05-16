namespace HPD.Agent.Harness.Coding.Ripgrep;

internal static partial class RipgrepBundledBinaries
{
    public static IReadOnlyList<RipgrepBundledBinaryManifest> Manifest { get; } = CreateManifest();

    private static IReadOnlyList<RipgrepBundledBinaryManifest> CreateManifest()
    {
        var binaries = new List<RipgrepBundledBinaryManifest>();
        AddBundledBinaries(binaries);
        return binaries;
    }

    static partial void AddBundledBinaries(List<RipgrepBundledBinaryManifest> binaries);
}
