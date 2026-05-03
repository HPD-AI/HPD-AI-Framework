using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Tools;

/// <summary>
/// Tests for ExternalToolCollapsingWrapper MCP-related changes:
/// - WrapMCPServerTools with parentContainer parameter
/// - AddParentToolMetadata with parentContainer parameter
/// </summary>
public class ExternalToolCollapsingWrapperMCPTests
{
    #region Helper Methods

    private static List<AIFunction> CreateMockTools(params string[] names)
    {
        return names.Select(name =>
            CollapsedHarnessTestHelper.CreateSimpleFunction(name, $"Description for {name}", () => $"{name} result")
        ).ToList();
    }

    private static string? GetAdditionalProperty(AIFunction func, string key)
    {
        if (func.AdditionalProperties?.TryGetValue(key, out var val) == true)
            return val as string;
        return null;
    }

    private static object? GetAdditionalPropertyRaw(AIFunction func, string key)
    {
        if (func.AdditionalProperties?.TryGetValue(key, out var val) == true)
            return val;
        return null;
    }

    #endregion

    #region WrapMCPServerTools with parentContainer

    [Fact]
    public void WrapMCPServerTools_ParentContainerNull_StandaloneContainer()
    {
        // Arrange
        var tools = CreateMockTools("tool1", "tool2");

        // Act
        var (container, collapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
            serverName: "testServer",
            tools: tools,
            parentContainer: null);

        // Assert
        var parentContainer = GetAdditionalProperty(container, "ParentContainer");
        parentContainer.Should().BeNull("standalone WithMCP() has no parent");
    }

    [Fact]
    public void WrapMCPServerTools_ParentContainerSet_NestedContainer()
    {
        // Arrange
        var tools = CreateMockTools("tool1", "tool2");

        // Act
        var (container, collapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
            serverName: "wolfram",
            tools: tools,
            parentContainer: "SearchHarness");

        // Assert
        var parentContainer = GetAdditionalProperty(container, "ParentContainer");
        parentContainer.Should().Be("SearchHarness", "container should be nested under SearchHarness");
    }

    [Fact]
    public void WrapMCPServerTools_ContainerName_IsMCP_Prefix()
    {
        // Arrange
        var tools = CreateMockTools("tool1");

        // Act
        var (container, _) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
            serverName: "filesystem",
            tools: tools);

        // Assert
        container.Name.Should().Be("MCP_filesystem");
    }

    [Fact]
    public void WrapMCPServerTools_CollapsedTools_HaveParentHARNESSetToContainerName()
    {
        // Arrange
        var tools = CreateMockTools("read", "write");

        // Act
        var (container, collapsedTools) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
            serverName: "fs",
            tools: tools,
            parentContainer: "DevHarness");

        // Assert — collapsed tools have ParentHarness = "MCP_fs" (the container name), not "DevHarness"
        foreach (var tool in collapsedTools)
        {
            var parentHarness = GetAdditionalProperty(tool, "ParentHarness");
            parentHarness.Should().Be("MCP_fs", "collapsed tools are children of the MCP container");
        }
    }

    [Fact]
    public void WrapMCPServerTools_ContainerMetadata_IsComplete()
    {
        // Arrange
        var tools = CreateMockTools("search", "fetch");

        // Act
        var (container, _) = ExternalToolCollapsingWrapper.WrapMCPServerTools(
            serverName: "web",
            tools: tools,
            FunctionResult: "Web tools activated",
            SystemPrompt: "Use web tools carefully",
            customDescription: "Web server tools",
            parentContainer: "SearchHarness");

        // Assert
        var props = container.AdditionalProperties!;
        props["IsContainer"].Should().Be(true);
        props["HarnessName"].Should().Be("MCP_web");
        props["MCPServerName"].Should().Be("web");
        props["SourceType"].Should().Be("MCP");
        props["FunctionResult"].Should().Be("Web tools activated");
        props["SystemPrompt"].Should().Be("Use web tools carefully");
        props["ParentContainer"].Should().Be("SearchHarness");
        (props["FunctionNames"] as string[]).Should().Contain("search", "fetch");
        props["FunctionCount"].Should().Be(2);
    }

    #endregion

    #region AddParentToolMetadata with parentContainer

    [Fact]
    public void AddParentToolMetadata_ParentContainerNull_NoParentContainer()
    {
        // Arrange
        var tool = CollapsedHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "result");

        // Act
        var wrapped = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            tool, "MCP_server", "MCP", parentContainer: null);

        // Assert
        var parentContainer = GetAdditionalProperty(wrapped, "ParentContainer");
        parentContainer.Should().BeNull();
    }

    [Fact]
    public void AddParentToolMetadata_ParentContainerSet_StampsKey()
    {
        // Arrange
        var tool = CollapsedHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "result");

        // Act
        var wrapped = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            tool, "MCP_server", "MCP", parentContainer: "DevHarness");

        // Assert
        var parentContainer = GetAdditionalProperty(wrapped, "ParentContainer");
        parentContainer.Should().Be("DevHarness");
    }

    [Fact]
    public void AddParentToolMetadata_DoubleWrapPrevention_ExistingParentHarness()
    {
        // Arrange — tool already has ParentHarness metadata
        var opts = new HPDAIFunctionFactoryOptions
        {
            Name = "existing",
            Description = "already wrapped",
            AdditionalProperties = new Dictionary<string, object?>
            {
                ["ParentHarness"] = "OldHarness"
            }
        };
        var existingTool = HPDAIFunctionFactory.Create(
            async (args, ct) => "result", opts);

        // Act
        var result = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            existingTool, "NewHarness", "MCP", parentContainer: "NewParent");

        // Assert — should return unchanged (double-wrap prevention)
        var parentHarness = GetAdditionalProperty(result, "ParentHarness");
        parentHarness.Should().Be("OldHarness", "double-wrap prevention should keep original");
    }

    [Fact]
    public void AddParentToolMetadata_SetsSourceType()
    {
        // Arrange
        var tool = CollapsedHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "result");

        // Act
        var wrapped = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            tool, "MCP_server", "MCP", parentContainer: "TestHarness");

        // Assert
        var sourceType = GetAdditionalProperty(wrapped, "SourceType");
        sourceType.Should().Be("MCP");
    }

    #endregion
}
