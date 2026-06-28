namespace HPD.Agent.Packages;

public static class HpdPackageRegistry
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<string, IHpdPackage> s_packages = new(StringComparer.Ordinal);

    public static void Register(IHpdPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(package.Id);

        lock (s_gate)
        {
            s_packages[package.Id] = package;
        }
    }

    /// <summary>
    /// Registers a build-time referenced package using its parameterless constructor.
    /// This is intended for AOT-friendly module initializers in package assemblies.
    /// </summary>
    public static void Register<TPackage>()
        where TPackage : IHpdPackage, new()
        => Register(new TPackage());

    public static IReadOnlyList<IHpdPackage> Snapshot()
    {
        lock (s_gate)
        {
            return s_packages.Values
                .OrderBy(static package => package.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static IHpdPackage? Find(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        lock (s_gate)
        {
            return s_packages.TryGetValue(packageId, out var package)
                ? package
                : null;
        }
    }

    internal static void ClearForTesting()
    {
        lock (s_gate)
        {
            s_packages.Clear();
        }
    }
}
