using System.Text.Json;
using Xunit;
using FluentAssertions;
using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

/// <summary>
/// Unit tests for MCPServerConfig toolharness-awareness fields:
/// - ParentToolHarness and CollapseWithinToolHarness have [JsonIgnore]
/// - Existing JSON deserialization is unchanged
/// </summary>
public class MCPServerConfigTests
{
    [Fact]
    public void ParentToolHarness_HasJsonIgnore_NotSerialized()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node",
            Arguments = new List<string> { "test.js" },
            ParentToolHarness = "MyToolHarness"
        };

        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);

        json.Should().NotContain("ParentToolHarness");
        json.Should().NotContain("MyToolHarness");
    }

    [Fact]
    public void CollapseWithinToolHarness_HasJsonIgnore_NotSerialized()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node",
            Arguments = new List<string> { "test.js" },
            CollapseWithinToolHarness = true
        };

        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);

        json.Should().NotContain("CollapseWithinToolHarness");
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

        // ToolHarness-awareness fields should have defaults
        config.ParentToolHarness.Should().BeNull();
        config.CollapseWithinToolHarness.Should().BeFalse();
    }

    [Fact]
    public void ParentToolHarness_DefaultNull()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node"
        };

        config.ParentToolHarness.Should().BeNull();
    }

    [Fact]
    public void CollapseWithinToolHarness_DefaultFalse()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node"
        };

        config.CollapseWithinToolHarness.Should().BeFalse();
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
            ParentToolHarness = "SearchToolHarness",
            CollapseWithinToolHarness = true
        };

        // Verify the fields are set
        config.ParentToolHarness.Should().Be("SearchToolHarness");
        config.CollapseWithinToolHarness.Should().BeTrue();

        // Serialize and verify they're excluded
        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);
        json.Should().NotContain("ParentToolHarness");
        json.Should().NotContain("CollapseWithinToolHarness");

        // Deserialize back — fields have defaults
        var deserialized = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);
        deserialized!.ParentToolHarness.Should().BeNull();
        deserialized.CollapseWithinToolHarness.Should().BeFalse();
    }
}
