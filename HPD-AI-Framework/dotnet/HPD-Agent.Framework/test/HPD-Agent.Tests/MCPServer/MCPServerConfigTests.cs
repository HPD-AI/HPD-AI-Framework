using System.Text.Json;
using Xunit;
using FluentAssertions;
using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

/// <summary>
/// Unit tests for MCPServerConfig toolkit-awareness fields:
/// - ParentToolkit and CollapseWithinToolkit have [JsonIgnore]
/// - Existing JSON deserialization is unchanged
/// </summary>
public class MCPServerConfigTests
{
    [Fact]
    public void ParentToolkit_HasJsonIgnore_NotSerialized()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node",
            Arguments = new List<string> { "test.js" },
            ParentToolkit = "MyToolkit"
        };

        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);

        json.Should().NotContain("ParentToolkit");
        json.Should().NotContain("MyToolkit");
    }

    [Fact]
    public void CollapseWithinToolkit_HasJsonIgnore_NotSerialized()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node",
            Arguments = new List<string> { "test.js" },
            CollapseWithinToolkit = true
        };

        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);

        json.Should().NotContain("CollapseWithinToolkit");
    }

    [Fact]
    public void ExistingJsonDeserialization_Unchanged()
    {
        var json = @"{
            ""name"": ""filesystem"",
            ""command"": ""npx"",
            ""arguments"": [""@modelcontextprotocol/server-filesystem"", ""/tmp""],
            ""timeout"": 60000,
            ""retryAttempts"": 5
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.Name.Should().Be("filesystem");
        config.Command.Should().Be("npx");
        config.Arguments.Should().Contain("@modelcontextprotocol/server-filesystem");
        config.TimeoutMs.Should().Be(60000);
        config.RetryAttempts.Should().Be(5);

        // Toolkit-awareness fields should have defaults
        config.ParentToolkit.Should().BeNull();
        config.CollapseWithinToolkit.Should().BeFalse();
    }

    [Fact]
    public void ParentToolkit_DefaultNull()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node"
        };

        config.ParentToolkit.Should().BeNull();
    }

    [Fact]
    public void CollapseWithinToolkit_DefaultFalse()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node"
        };

        config.CollapseWithinToolkit.Should().BeFalse();
    }

    [Fact]
    public void RequiresPermission_DefaultFalse()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node"
        };

        config.RequiresPermission.Should().BeFalse();
    }

    [Fact]
    public void RequiresPermission_JsonWithTrue_DeserializesCorrectly()
    {
        var json = @"{
            ""name"": ""dangerous-server"",
            ""command"": ""node"",
            ""requiresPermission"": true
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.RequiresPermission.Should().BeTrue();
    }

    [Fact]
    public void RequiresPermission_JsonWithout_DefaultsFalse()
    {
        var json = @"{
            ""name"": ""safe-server"",
            ""command"": ""node""
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.RequiresPermission.Should().BeFalse();
    }

    [Fact]
    public void RuntimeFieldsCanBeSet_WithoutAffectingSerialization()
    {
        var config = new MCPServerConfig
        {
            Name = "wolfram",
            Command = "npx",
            Arguments = new List<string> { "wolfram-mcp" },
            ParentToolkit = "SearchToolkit",
            CollapseWithinToolkit = true
        };

        // Verify the fields are set
        config.ParentToolkit.Should().Be("SearchToolkit");
        config.CollapseWithinToolkit.Should().BeTrue();

        // Serialize and verify they're excluded
        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);
        json.Should().NotContain("ParentToolkit");
        json.Should().NotContain("CollapseWithinToolkit");

        // Deserialize back — fields have defaults
        var deserialized = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);
        deserialized!.ParentToolkit.Should().BeNull();
        deserialized.CollapseWithinToolkit.Should().BeFalse();
    }
}
