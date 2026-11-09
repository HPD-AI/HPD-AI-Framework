using System.ComponentModel;
using System.Reflection;
using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpPublicApiSurfaceTests
{
    [Fact]
    public void ExportedTypes_MatchTheDeliberateConsumerAndCompositionSurface()
    {
        var expected = new[]
        {
            "HPD.Agent.MCP.AgentBuilderMcpExtensions",
            "HPD.Agent.MCP.IMcpAuthorizationStore",
            "HPD.Agent.MCP.IMcpInputAuthorizer",
            "HPD.Agent.MCP.IMcpInputResolver",
            "HPD.Agent.MCP.IMcpRecoveryReferenceProtector",
            "HPD.Agent.MCP.McpAuthorizationRecord",
            "HPD.Agent.MCP.McpCapabilitySourceFactory",
            "HPD.Agent.MCP.McpCatalogLoadMode",
            "HPD.Agent.MCP.McpCatalogOptions",
            "HPD.Agent.MCP.McpConfigurationPackage",
            "HPD.Agent.MCP.McpInputResolution",
            "HPD.Agent.MCP.McpInputResolutionContext",
            "HPD.Agent.MCP.McpInvocationOptions",
            "HPD.Agent.MCP.McpOptions",
            "HPD.Agent.MCP.McpProtocolOptions",
            "HPD.Agent.MCP.McpResourceRegistrationId",
            "HPD.Agent.MCP.McpSubscriptionFailurePolicy",
            "HPD.Agent.MCP.McpSubscriptionOptions",
            "HPD.Agent.MCP.McpTasksExtensions"
        };

        var actual = typeof(McpOptions).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void CompositionBridges_AreHiddenFromConsumerIntelliSense()
    {
        AssertHidden(typeof(McpCapabilitySourceFactory));
        AssertHidden(typeof(McpConfigurationPackage));
        AssertHidden(typeof(HPD.Agent.McpServerSource));
    }

    [Fact]
    public void GeneratedOnlyFactoryMethod_IsNotPartOfTheConsumerApi()
    {
        Assert.Null(typeof(McpCapabilitySourceFactory).GetMethod(
            "FromServers",
            BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void ResourceRegistrationId_CanOnlyBeCreatedThroughValidatedFactory()
    {
        Assert.Empty(typeof(McpResourceRegistrationId).GetConstructors());
        Assert.NotNull(typeof(McpResourceRegistrationId).GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.Static));
        Assert.Throws<ArgumentException>(() => McpResourceRegistrationId.Create(" "));
    }

    [Fact]
    public void CoreMcpActivationPlumbing_IsNotExported()
    {
        var exported = typeof(AgentMcpConfig).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("HPD.Agent.MCP.ResolvedMcpManifestReference", exported);
        Assert.DoesNotContain("HPD.Agent.MCP.ResolvedAgentMcpConfig", exported);
        Assert.DoesNotContain("HPD.Agent.MCP.McpConfigurationActivationContext", exported);
        Assert.DoesNotContain("HPD.Agent.MCP.IMcpConfigurationActivator", exported);
        Assert.DoesNotContain("HPD.Agent.MCP.McpConfigurationActivation", exported);
    }

    private static void AssertHidden(Type type)
    {
        var attribute = type.GetCustomAttribute<EditorBrowsableAttribute>();
        Assert.Equal(EditorBrowsableState.Never, attribute?.State);
    }
}
