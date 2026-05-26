namespace Rhodium.Connectivity.Tests;

public sealed class ReplayConnectorDeprecationTests
{
    [Fact]
    public void ProductionSource_DoesNotCreateReplayConnectorOutsideConnectivityOracle()
    {
        var rhodiumSourceRoot = GetRhodiumSourceRoot();
        var matches = Directory
            .EnumerateFiles(Path.Combine(rhodiumSourceRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactPath(path))
            .Where(path => !IsConnectivityPath(path))
            .Where(path => File.ReadAllText(path).Contains("new ReplayConnector", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(rhodiumSourceRoot, path))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void ReplayConnector_DoesNotUseSpotLikeMarginOrNotionalShortcuts()
    {
        var rhodiumSourceRoot = GetRhodiumSourceRoot();
        var replayConnector = Path.Combine(
            rhodiumSourceRoot,
            "src",
            "Rhodium.Connectivity",
            "ReplayConnector.cs");
        var source = File.ReadAllText(replayConnector);
        var stalePatterns = new[]
        {
            "price.Value * command.Quantity.Value",
            "notional * _config.Margin.InitialMarginFraction",
            "passiveNotional * _config.Margin.InitialMarginFraction",
            "mark.Value * multiplier * _config.Margin.MaintenanceMarginFraction",
            "mark.Value * _config.Margin.MaintenanceMarginFraction"
        };

        var matches = stalePatterns
            .Where(pattern => source.Contains(pattern, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(matches);
    }

    private static string GetRhodiumSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "HPD-AI-Framework",
                "dotnet",
                "shared",
                "src",
                "Rhodium");

            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Rhodium source root.");
    }

    private static bool IsConnectivityPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("Rhodium.Connectivity", StringComparer.Ordinal);

    private static bool IsBuildArtifactPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.Ordinal)
                || string.Equals(part, "obj", StringComparison.Ordinal));
}
