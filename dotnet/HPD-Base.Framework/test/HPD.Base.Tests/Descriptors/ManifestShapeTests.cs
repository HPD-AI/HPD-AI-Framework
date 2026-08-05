using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Descriptors;

public sealed class ManifestShapeTests
{
    [Fact]
    public void ManifestIsCompactBootstrapShape()
    {
        var properties = typeof(BaseManifest).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Collections", properties);
        Assert.Contains("Capabilities", properties);
        Assert.Contains("Links", properties);
        Assert.DoesNotContain("Schema", properties);
        Assert.DoesNotContain("Health", properties);
        Assert.DoesNotContain("Diagnostics", properties);
    }

    [Fact]
    public void ExpansionTokensAreReservedByContract()
    {
        var tokens = new[] { "schema", "capabilities", "health", "diagnostics", "collections" };

        Assert.Equal(5, tokens.Distinct(StringComparer.Ordinal).Count());
    }
}
