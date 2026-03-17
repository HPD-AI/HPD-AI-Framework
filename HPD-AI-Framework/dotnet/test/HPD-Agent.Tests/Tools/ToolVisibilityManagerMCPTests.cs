using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent.Collapsing;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Tools;

/// <summary>
/// Tests for ToolVisibilityManager MCP-related changes:
/// - ParentContainer key migration (from ParentSkillContainer)
/// - Nested MCP container visibility (IsCollapseContainerVisible)
/// - Flat MCP tool visibility (GetFunctionVisibility)
/// </summary>
public class ToolVisibilityManagerMCPTests
{
    #region Helper Methods

    /// <summary>
    /// Creates a collapse/toolkit container function with the given properties.
    /// </summary>
    private static AIFunction CreateCollapseContainer(
        string name,
        string description,
        string[] functionNames,
        string? parentContainer = null,
        string? functionResult = null,
        string? systemPrompt = null)
    {
        return HPDAIFunctionFactory.Create(
            async (args, ct) => $"{name} expanded",
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsToolkitContainer"] = true,
                    ["ToolkitName"] = name,
                    ["FunctionNames"] = functionNames,
                    ["FunctionCount"] = functionNames.Length,
                    ["SourceType"] = "CSharp",
                    ["ParentContainer"] = parentContainer,
                    ["FunctionResult"] = functionResult,
                    ["SystemPrompt"] = systemPrompt,
                }
            });
    }

    /// <summary>
    /// Creates an MCP container function nested under a parent toolkit.
    /// </summary>
    private static AIFunction CreateMCPContainer(
        string serverName,
        string[] functionNames,
        string? parentContainer = null)
    {
        var containerName = $"MCP_{serverName}";
        return HPDAIFunctionFactory.Create(
            async (args, ct) => $"{serverName} expanded",
            new HPDAIFunctionFactoryOptions
            {
                Name = containerName,
                Description = $"MCP Server '{serverName}'",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["ToolkitName"] = containerName,
                    ["FunctionNames"] = functionNames,
                    ["FunctionCount"] = functionNames.Length,
                    ["SourceType"] = "MCP",
                    ["MCPServerName"] = serverName,
                    ["ParentContainer"] = parentContainer,
                }
            });
    }

    /// <summary>
    /// Creates a toolkit member function with ParentToolkit and optional ParentContainer.
    /// </summary>
    private static AIFunction CreateToolkitFunction(
        string name,
        string parentToolkit,
        string? parentContainer = null,
        string sourceType = "CSharp")
    {
        return HPDAIFunctionFactory.Create(
            async (args, ct) => $"{name} result",
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = $"Function {name}",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ParentToolkit"] = parentToolkit,
                    ["ToolkitName"] = parentToolkit,
                    ["ParentContainer"] = parentContainer,
                    ["IsContainer"] = false,
                    ["SourceType"] = sourceType,
                }
            });
    }

    /// <summary>
    /// Creates a standalone non-collapsed function.
    /// </summary>
    private static AIFunction CreateStandaloneFunction(string name)
    {
        return CollapsedToolkitTestHelper.CreateSimpleFunction(name, $"Standalone {name}", () => $"{name} result");
    }

    private static List<string> GetVisibleNames(List<AIFunction> tools) =>
        tools.Select(t => t.Name ?? "").ToList();

    #endregion

    #region Nested MCP Container Visibility

    [Fact]
    public void NestedMCPContainer_ParentNotExpanded_Hidden()
    {
        // Arrange
        var searchToolkitContainer = CreateCollapseContainer(
            "SearchToolkit", "Search tools",
            new[] { "WebSearch", "ImageSearch" });

        var mcpContainer = CreateMCPContainer(
            "wolfram",
            new[] { "calculate", "plot" },
            parentContainer: "SearchToolkit");

        var allTools = new List<AIFunction> { searchToolkitContainer, mcpContainer };
        var manager = new ToolVisibilityManager(allTools);

        // Act — nothing expanded
        var visible = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);

        // Assert — only SearchToolkit container visible, MCP container hidden
        var names = GetVisibleNames(visible);
        names.Should().Contain("SearchToolkit");
        names.Should().NotContain("MCP_wolfram", "MCP container is hidden because parent SearchToolkit is not expanded");
    }

    [Fact]
    public void NestedMCPContainer_ParentExpanded_Visible()
    {
        // Arrange
        var searchToolkitContainer = CreateCollapseContainer(
            "SearchToolkit", "Search tools",
            new[] { "WebSearch", "ImageSearch" });

        var webSearch = CreateToolkitFunction("WebSearch", "SearchToolkit");
        var imageSearch = CreateToolkitFunction("ImageSearch", "SearchToolkit");

        var mcpContainer = CreateMCPContainer(
            "wolfram",
            new[] { "calculate", "plot" },
            parentContainer: "SearchToolkit");

        var allTools = new List<AIFunction>
        {
            searchToolkitContainer, webSearch, imageSearch, mcpContainer
        };
        var manager = new ToolVisibilityManager(allTools);

        // Act — expand SearchToolkit
        var expanded = ImmutableHashSet.Create("SearchToolkit");
        var visible = manager.GetToolsForAgentTurn(allTools, expanded);

        // Assert — MCP container visible alongside native functions
        var names = GetVisibleNames(visible);
        names.Should().Contain("MCP_wolfram", "MCP container visible when parent expanded");
        names.Should().Contain("WebSearch");
        names.Should().Contain("ImageSearch");
        names.Should().NotContain("SearchToolkit", "SearchToolkit container is hidden when expanded");
    }

    [Fact]
    public void NestedMCPContainer_MCPExpanded_MCPToolsVisible()
    {
        // Arrange
        var searchToolkitContainer = CreateCollapseContainer(
            "SearchToolkit", "Search tools",
            new[] { "WebSearch" });

        var webSearch = CreateToolkitFunction("WebSearch", "SearchToolkit");

        var mcpContainer = CreateMCPContainer(
            "wolfram",
            new[] { "calculate", "plot" },
            parentContainer: "SearchToolkit");

        var calculate = CreateToolkitFunction("calculate", "MCP_wolfram", sourceType: "MCP");
        var plot = CreateToolkitFunction("plot", "MCP_wolfram", sourceType: "MCP");

        var allTools = new List<AIFunction>
        {
            searchToolkitContainer, webSearch, mcpContainer, calculate, plot
        };
        var manager = new ToolVisibilityManager(allTools);

        // Act — expand both SearchToolkit and MCP_wolfram
        var expanded = ImmutableHashSet.Create("SearchToolkit", "MCP_wolfram");
        var visible = manager.GetToolsForAgentTurn(allTools, expanded);

        // Assert — MCP tools visible
        var names = GetVisibleNames(visible);
        names.Should().Contain("calculate");
        names.Should().Contain("plot");
        names.Should().Contain("WebSearch");
        names.Should().NotContain("MCP_wolfram", "MCP container hidden when itself expanded");
        names.Should().NotContain("SearchToolkit", "SearchToolkit container hidden when expanded");
    }

    [Fact]
    public void StandaloneMCPContainer_NoParent_VisibleByDefault()
    {
        // Arrange — standalone MCP container (from WithMCP(), no parent)
        var mcpContainer = CreateMCPContainer(
            "filesystem",
            new[] { "read_file", "write_file" },
            parentContainer: null);

        var allTools = new List<AIFunction> { mcpContainer };
        var manager = new ToolVisibilityManager(allTools);

        // Act — nothing expanded
        var visible = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);

        // Assert — visible (no parent to block it)
        var names = GetVisibleNames(visible);
        names.Should().Contain("MCP_filesystem");
    }

    #endregion

    #region Flat MCP Tool Visibility

    [Fact]
    public void FlatMCPTool_ParentCollapsedToolkit_NotExpanded_Hidden()
    {
        // Arrange — flat MCP tools under a collapsed toolkit
        var devToolkitContainer = CreateCollapseContainer(
            "DevToolkit", "Dev tools",
            new[] { "ReadFile", "git_status", "git_diff" });

        var readFile = CreateToolkitFunction("ReadFile", "DevToolkit");
        var gitStatus = CreateToolkitFunction("git_status", "DevToolkit",
            parentContainer: "DevToolkit", sourceType: "MCP");
        var gitDiff = CreateToolkitFunction("git_diff", "DevToolkit",
            parentContainer: "DevToolkit", sourceType: "MCP");

        var allTools = new List<AIFunction>
        {
            devToolkitContainer, readFile, gitStatus, gitDiff
        };
        var manager = new ToolVisibilityManager(allTools);

        // Act — nothing expanded
        var visible = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);

        // Assert — only container visible
        var names = GetVisibleNames(visible);
        names.Should().Contain("DevToolkit");
        names.Should().NotContain("git_status");
        names.Should().NotContain("git_diff");
        names.Should().NotContain("ReadFile");
    }

    [Fact]
    public void FlatMCPTool_ParentCollapsedToolkit_Expanded_Visible()
    {
        // Arrange — flat MCP tools under a collapsed toolkit
        var devToolkitContainer = CreateCollapseContainer(
            "DevToolkit", "Dev tools",
            new[] { "ReadFile", "git_status", "git_diff" });

        var readFile = CreateToolkitFunction("ReadFile", "DevToolkit");
        var gitStatus = CreateToolkitFunction("git_status", "DevToolkit",
            parentContainer: "DevToolkit", sourceType: "MCP");
        var gitDiff = CreateToolkitFunction("git_diff", "DevToolkit",
            parentContainer: "DevToolkit", sourceType: "MCP");

        var allTools = new List<AIFunction>
        {
            devToolkitContainer, readFile, gitStatus, gitDiff
        };
        var manager = new ToolVisibilityManager(allTools);

        // Act — expand DevToolkit
        var expanded = ImmutableHashSet.Create("DevToolkit");
        var visible = manager.GetToolsForAgentTurn(allTools, expanded);

        // Assert — all functions visible
        var names = GetVisibleNames(visible);
        names.Should().Contain("ReadFile");
        names.Should().Contain("git_status");
        names.Should().Contain("git_diff");
        names.Should().NotContain("DevToolkit", "container hidden when expanded");
    }

    [Fact]
    public void FlatMCPTool_NoParentContainer_AlwaysVisible()
    {
        // Arrange — standalone function without parent container
        var standalone = CreateStandaloneFunction("standalone_func");

        var allTools = new List<AIFunction> { standalone };
        var manager = new ToolVisibilityManager(allTools);

        // Act
        var visible = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);

        // Assert
        var names = GetVisibleNames(visible);
        names.Should().Contain("standalone_func");
    }

    #endregion

    #region Regression - Existing Behavior Unchanged

    [Fact]
    public void ExistingCollapseToolkit_BehaviorUnchanged()
    {
        // Arrange — standard collapsed toolkit without MCP
        var container = CreateCollapseContainer(
            "MathToolkit", "Math functions",
            new[] { "Add", "Multiply" });

        var add = CreateToolkitFunction("Add", "MathToolkit");
        var multiply = CreateToolkitFunction("Multiply", "MathToolkit");

        var allTools = new List<AIFunction> { container, add, multiply };
        var manager = new ToolVisibilityManager(allTools);

        // Act — not expanded
        var visibleCollapsed = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);
        var namesCollapsed = GetVisibleNames(visibleCollapsed);
        namesCollapsed.Should().Contain("MathToolkit");
        namesCollapsed.Should().NotContain("Add");
        namesCollapsed.Should().NotContain("Multiply");

        // Act — expanded
        var expanded = ImmutableHashSet.Create("MathToolkit");
        var visibleExpanded = manager.GetToolsForAgentTurn(allTools, expanded);
        var namesExpanded = GetVisibleNames(visibleExpanded);
        namesExpanded.Should().NotContain("MathToolkit", "container hidden when expanded");
        namesExpanded.Should().Contain("Add");
        namesExpanded.Should().Contain("Multiply");
    }

    [Fact]
    public void StandaloneWithMCP_BehaviorUnchanged()
    {
        // Arrange — standalone MCP container from WithMCP() (no ParentContainer)
        var mcpContainer = CreateMCPContainer(
            "github",
            new[] { "create_pr", "list_issues" },
            parentContainer: null);

        var createPr = CreateToolkitFunction("create_pr", "MCP_github", sourceType: "MCP");
        var listIssues = CreateToolkitFunction("list_issues", "MCP_github", sourceType: "MCP");

        var allTools = new List<AIFunction> { mcpContainer, createPr, listIssues };
        var manager = new ToolVisibilityManager(allTools);

        // Act — not expanded
        var visibleCollapsed = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);
        var namesCollapsed = GetVisibleNames(visibleCollapsed);
        namesCollapsed.Should().Contain("MCP_github");
        namesCollapsed.Should().NotContain("create_pr");
        namesCollapsed.Should().NotContain("list_issues");

        // Act — expanded
        var expanded = ImmutableHashSet.Create("MCP_github");
        var visibleExpanded = manager.GetToolsForAgentTurn(allTools, expanded);
        var namesExpanded = GetVisibleNames(visibleExpanded);
        namesExpanded.Should().NotContain("MCP_github");
        namesExpanded.Should().Contain("create_pr");
        namesExpanded.Should().Contain("list_issues");
    }

    #endregion

    #region Integration: Two-Level Expand Scenario

    [Fact]
    public void TwoLevelExpand_ToolkitThenMCP_FullExpansionChain()
    {
        // Arrange: SearchToolkit with native functions + nested MCP_wolfram
        var searchContainer = CreateCollapseContainer(
            "SearchToolkit", "Search capabilities",
            new[] { "WebSearch" });

        var webSearch = CreateToolkitFunction("WebSearch", "SearchToolkit");

        var wolframContainer = CreateMCPContainer(
            "wolfram",
            new[] { "calculate", "plot" },
            parentContainer: "SearchToolkit");

        var calculate = CreateToolkitFunction("calculate", "MCP_wolfram", sourceType: "MCP");
        var plot = CreateToolkitFunction("plot", "MCP_wolfram", sourceType: "MCP");

        var allTools = new List<AIFunction>
        {
            searchContainer, webSearch, wolframContainer, calculate, plot
        };
        var manager = new ToolVisibilityManager(allTools);

        // Step 1: Nothing expanded — only SearchToolkit visible
        var step1 = manager.GetToolsForAgentTurn(allTools, ImmutableHashSet<string>.Empty);
        var names1 = GetVisibleNames(step1);
        names1.Should().Contain("SearchToolkit");
        names1.Should().HaveCount(1, "only the top-level container is visible initially");

        // Step 2: Expand SearchToolkit — WebSearch + MCP_wolfram visible
        var step2Expanded = ImmutableHashSet.Create("SearchToolkit");
        var step2 = manager.GetToolsForAgentTurn(allTools, step2Expanded);
        var names2 = GetVisibleNames(step2);
        names2.Should().Contain("WebSearch");
        names2.Should().Contain("MCP_wolfram");
        names2.Should().NotContain("SearchToolkit");
        names2.Should().NotContain("calculate");
        names2.Should().NotContain("plot");

        // Step 3: Expand both SearchToolkit + MCP_wolfram — all tools visible
        var step3Expanded = ImmutableHashSet.Create("SearchToolkit", "MCP_wolfram");
        var step3 = manager.GetToolsForAgentTurn(allTools, step3Expanded);
        var names3 = GetVisibleNames(step3);
        names3.Should().Contain("WebSearch");
        names3.Should().Contain("calculate");
        names3.Should().Contain("plot");
        names3.Should().NotContain("SearchToolkit");
        names3.Should().NotContain("MCP_wolfram");
    }

    #endregion
}
