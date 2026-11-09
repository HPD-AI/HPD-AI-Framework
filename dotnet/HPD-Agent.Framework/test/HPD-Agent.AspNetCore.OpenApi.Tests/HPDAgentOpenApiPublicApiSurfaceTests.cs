using HPD.Agent.AspNetCore.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi.Tests;

public sealed class HPDAgentOpenApiPublicApiSurfaceTests
{
    [Fact]
    public void ExportedTypes_MatchTheDeliberateConsumerSurface()
    {
        var actual = typeof(HPDAgentOpenApiExtensions).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["HPD.Agent.AspNetCore.OpenApi.HPDAgentOpenApiExtensions"],
            actual);
    }
}
