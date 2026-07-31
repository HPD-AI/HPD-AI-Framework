using FluentAssertions;
using Xunit;

namespace HPD.Base.Tests.LiveQuery;

public sealed class LiveQueryArchitectureTests
{
    [Fact]
    public void ProductionPackageDoesNotOwnClientTransportOrTelemetry()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/HPD.Base/LiveQuery"));
        var text = string.Join(
            '\n',
            Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal)
                    || path.EndsWith(".csproj", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        text.Should().NotContain("HPD.Base.Realtime");
        text.Should().NotContain("AspNetCore");
        text.Should().NotContain("WebSocket");
        text.Should().NotContain("ILogger");
        text.Should().NotContain("ActivitySource");
        text.Should().NotContain("System.Diagnostics.Metrics");
        text.Should().NotContain("TypeScript");
    }
}
