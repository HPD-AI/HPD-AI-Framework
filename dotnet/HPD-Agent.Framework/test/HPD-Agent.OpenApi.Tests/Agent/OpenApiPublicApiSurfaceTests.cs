using HPD.Agent.OpenApi;

namespace HPD.Agent.OpenApi.Tests.Agent;

public sealed class OpenApiPublicApiSurfaceTests
{
    [Fact]
    public void ExportedTypes_MatchTheDeliberateConsumerSurface()
    {
        var expected = new[]
        {
            "HPD.Agent.OpenApi.AgentBuilderOpenApiExtensions",
            "HPD.Agent.OpenApi.OpenApiConfig",
            "HPD.Agent.OpenApi.ResponseOptimizationConfig"
        };

        var actual = typeof(OpenApiConfig).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void ConsumerConfigurationTypes_AreClosedToInheritance()
    {
        Assert.True(typeof(OpenApiConfig).IsSealed);
        Assert.True(typeof(ResponseOptimizationConfig).IsSealed);
    }
}
