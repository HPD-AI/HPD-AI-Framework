using Xunit;
using FluentAssertions;
using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

/// <summary>
/// Unit tests for MCPServerRegistration — the runtime bridge between
/// source-generated code and AgentBuilder's MCP loading pipeline.
/// </summary>
public class MCPServerRegistrationTests
{
    [Fact]
    public void StaticConfigProvider_Set_InstanceConfigProvider_Null_IsValid()
    {
        var reg = new MCPServerRegistration
        {
            Name = "wolfram",
            ParentToolkit = "SearchToolkit",
            StaticConfigProvider = () => new MCPServerConfig
            {
                Name = "wolfram",
                Command = "npx",
                Arguments = new List<string> { "wolfram-mcp" }
            }
        };

        reg.StaticConfigProvider.Should().NotBeNull();
        reg.InstanceConfigProvider.Should().BeNull();

        var config = reg.StaticConfigProvider!();
        config.Should().NotBeNull();
        config!.Name.Should().Be("wolfram");
    }

    [Fact]
    public void InstanceConfigProvider_Set_StaticConfigProvider_Null_IsValid()
    {
        var reg = new MCPServerRegistration
        {
            Name = "custom",
            ParentToolkit = "DevToolkit",
            InstanceConfigProvider = (instance) => new MCPServerConfig
            {
                Name = "custom",
                Command = "node",
                Arguments = new List<string> { "custom.js" }
            }
        };

        reg.InstanceConfigProvider.Should().NotBeNull();
        reg.StaticConfigProvider.Should().BeNull();

        var config = reg.InstanceConfigProvider!(new object());
        config.Should().NotBeNull();
        config!.Name.Should().Be("custom");
    }

    [Fact]
    public void FromManifest_Set_ManifestServerName_ShouldBeNonNull()
    {
        var reg = new MCPServerRegistration
        {
            Name = "filesystem",
            ParentToolkit = "FileToolkit",
            FromManifest = "mcp.json",
            ManifestServerName = "filesystem"
        };

        reg.FromManifest.Should().Be("mcp.json");
        reg.ManifestServerName.Should().Be("filesystem");
    }

    [Fact]
    public void RequiresPermissionOverride_Null_MeansUseConfigDefault()
    {
        var reg = new MCPServerRegistration
        {
            Name = "test",
            ParentToolkit = "TestToolkit"
        };

        reg.RequiresPermissionOverride.Should().BeNull();
    }

    [Fact]
    public void RequiresPermissionOverride_True_FromRequiresPermissionAttribute()
    {
        // Simulates what source gen emits when [RequiresPermission] is on the method
        var reg = new MCPServerRegistration
        {
            Name = "test",
            ParentToolkit = "TestToolkit",
            RequiresPermissionOverride = true
        };

        reg.RequiresPermissionOverride.Should().BeTrue();
    }

    [Fact]
    public void CollapseWithinToolkit_Default_IsFalse()
    {
        var reg = new MCPServerRegistration
        {
            Name = "test",
            ParentToolkit = "TestToolkit"
        };

        reg.CollapseWithinToolkit.Should().BeFalse();
    }

    [Fact]
    public void CollapseWithinToolkit_True_NestedMode()
    {
        var reg = new MCPServerRegistration
        {
            Name = "wolfram",
            ParentToolkit = "SearchToolkit",
            CollapseWithinToolkit = true
        };

        reg.CollapseWithinToolkit.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_EmptyStrings()
    {
        var reg = new MCPServerRegistration();

        reg.Name.Should().Be(string.Empty);
        reg.Description.Should().Be(string.Empty);
        reg.ParentToolkit.Should().Be(string.Empty);
        reg.FromManifest.Should().BeNull();
        reg.ManifestServerName.Should().BeNull();
    }
}
