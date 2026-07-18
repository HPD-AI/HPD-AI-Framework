using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using HPD.Events.Core;

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
            CollapsedToolHarnessTestHelper.CreateSimpleFunction(name, $"Description for {name}", () => $"{name} result")
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

    private static FunctionExecutionContext CreateContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new global::HPD.Agent.Session("session-1");
        var thread = new global::HPD.Agent.Thread("session-1", "test-agent") { Id = "thread-1" };
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            new EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);

        return new FunctionExecutionContext(
            beforeContext,
            new FunctionRequest
            {
                Function = function,
                CallId = "call-1",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            });
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
            parentContainer: "SearchToolHarness");

        // Assert
        var parentContainer = GetAdditionalProperty(container, "ParentContainer");
        parentContainer.Should().Be("SearchToolHarness", "container should be nested under SearchToolHarness");
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
            parentContainer: "DevToolHarness");

        // Assert — collapsed tools have ParentToolHarness = "MCP_fs" (the container name), not "DevToolHarness"
        foreach (var tool in collapsedTools)
        {
            var parentToolHarness = GetAdditionalProperty(tool, "ParentToolHarness");
            parentToolHarness.Should().Be("MCP_fs", "collapsed tools are children of the MCP container");
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
            parentContainer: "SearchToolHarness");

        // Assert
        var props = container.AdditionalProperties!;
        props["IsContainer"].Should().Be(true);
        props["ToolHarnessName"].Should().Be("MCP_web");
        props["MCPServerName"].Should().Be("web");
        props["SourceType"].Should().Be("MCP");
        props["FunctionResult"].Should().Be("Web tools activated");
        props["SystemPrompt"].Should().Be("Use web tools carefully");
        props["ParentContainer"].Should().Be("SearchToolHarness");
        (props["ReferencedFunctions"] as string[]).Should().Contain("search", "fetch");
        props["FunctionCount"].Should().Be(2);
    }

    #endregion

    #region AddParentToolMetadata with parentContainer

    [Fact]
    public void AddParentToolMetadata_ParentContainerNull_NoParentContainer()
    {
        // Arrange
        var tool = CollapsedToolHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "result");

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
        var tool = CollapsedToolHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "result");

        // Act
        var wrapped = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            tool, "MCP_server", "MCP", parentContainer: "DevToolHarness");

        // Assert
        var parentContainer = GetAdditionalProperty(wrapped, "ParentContainer");
        parentContainer.Should().Be("DevToolHarness");
    }

    [Fact]
    public async Task AddParentToolMetadata_MCPWrappedHPDFunction_InvokesWithFunctionExecutionContext()
    {
        // Arrange
        var tool = CollapsedToolHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "mcp result");

        // Act
        var wrapped = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            tool, "MCP_server", "MCP", parentContainer: "DevToolHarness");
        var hpdFunction = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(wrapped);
        var result = await hpdFunction.InvokeAsync(new AIFunctionArguments(), CreateContext(wrapped), CancellationToken.None);

        // Assert
        result.Should().Be("mcp result");
    }

    [Fact]
    public void AddParentToolMetadata_DoubleWrapPrevention_ExistingParentToolHarness()
    {
        // Arrange — tool already has ParentToolHarness metadata
        var opts = new HPDAIFunctionFactoryOptions
        {
            Name = "existing",
            Description = "already wrapped",
            AdditionalProperties = new Dictionary<string, object?>
            {
                ["ParentToolHarness"] = "OldToolHarness"
            }
        };
        var existingTool = HPDAIFunctionFactory.Create(
            async (args, _, ct) => "result", opts);

        // Act
        var result = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            existingTool, "NewToolHarness", "MCP", parentContainer: "NewParent");

        // Assert — should return unchanged (double-wrap prevention)
        var parentToolHarness = GetAdditionalProperty(result, "ParentToolHarness");
        parentToolHarness.Should().Be("OldToolHarness", "double-wrap prevention should keep original");
    }

    [Fact]
    public void AddParentToolMetadata_SetsSourceType()
    {
        // Arrange
        var tool = CollapsedToolHarnessTestHelper.CreateSimpleFunction("myTool", "desc", () => "result");

        // Act
        var wrapped = ExternalToolCollapsingWrapper.AddParentToolMetadata(
            tool, "MCP_server", "MCP", parentContainer: "TestToolHarness");

        // Assert
        var sourceType = GetAdditionalProperty(wrapped, "SourceType");
        sourceType.Should().Be("MCP");
    }

    #endregion
}
