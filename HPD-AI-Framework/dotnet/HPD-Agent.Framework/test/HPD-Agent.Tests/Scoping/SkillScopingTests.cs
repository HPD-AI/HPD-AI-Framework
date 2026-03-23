using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using HPD.Agent.Collapsing;
using Xunit;

namespace HPD.Agent.Tests.Collapsing;

/// <summary>
/// Tests for skill Collapsing functionality in ToolVisibilityManager.
/// Ensures skills work like Toolkit containers - hiding referenced functions until expanded.
/// </summary>
public class SkillCollapsingTests
{
    private static AIFunction CreateSkillContainer(
        string name, 
        string description, 
        params string[] referencedFunctions)
    {
        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{name} activated"),
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["IsSkill"] = true,
                    ["ReferencedFunctions"] = referencedFunctions
                }
            });
    }

    private static AIFunction CreateFunction(string name, string? parentToolkit = null)
    {
        var options = new AIFunctionFactoryOptions
        {
            Name = name,
            Description = $"{name} function"
        };

        if (parentToolkit != null)
        {
            options.AdditionalProperties = new Dictionary<string, object>
            {
                ["ParentToolkit"] = parentToolkit
            };
        }

        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("result"),
            options);
    }

    [Fact]
    public void SkillContainer_NotExpanded_HidesReferencedFunctions()
    {
        // Arrange
        var skill = CreateSkillContainer("TestSkill", "Test skill", "Function1", "Function2");
        var func1 = CreateFunction("Function1");
        var func2 = CreateFunction("Function2");
        var nonCollapsed = CreateFunction("AlwaysVisible");

        var allTools = new List<AIFunction> { skill, func1, func2, nonCollapsed };
        var manager = new ToolVisibilityManager(allTools);

        // Act
        var visible = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert
        Assert.Contains(visible, f => f.Name == "TestSkill"); // Skill container visible
        Assert.DoesNotContain(visible, f => f.Name == "Function1"); // Referenced function hidden
        Assert.DoesNotContain(visible, f => f.Name == "Function2"); // Referenced function hidden
        Assert.Contains(visible, f => f.Name == "AlwaysVisible"); // Non-Collapsed function visible
    }

    [Fact]
    public void SkillContainer_Expanded_ShowsReferencedFunctions()
    {
        // Arrange
        var skill = CreateSkillContainer("TestSkill", "Test skill", "Function1", "Function2");
        var func1 = CreateFunction("Function1");
        var func2 = CreateFunction("Function2");
        var nonCollapsed = CreateFunction("AlwaysVisible");

        var allTools = new List<AIFunction> { skill, func1, func2, nonCollapsed };
        var manager = new ToolVisibilityManager(allTools);

        // Act - expand the skill
        var visible = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("TestSkill"));

        // Assert
        Assert.DoesNotContain(visible, f => f.Name == "TestSkill"); // Skill container hidden when expanded
        Assert.Contains(visible, f => f.Name == "Function1"); // Referenced function now visible
        Assert.Contains(visible, f => f.Name == "Function2"); // Referenced function now visible
        Assert.Contains(visible, f => f.Name == "AlwaysVisible"); // Non-Collapsed function still visible
    }

    [Fact]
    public void MultipleSkills_ReferenceSameFunction_DeduplicationWorks()
    {
        // Arrange
        var skill1 = CreateSkillContainer("Skill1", "First skill", "SharedFunction");
        var skill2 = CreateSkillContainer("Skill2", "Second skill", "SharedFunction");
        var sharedFunc = CreateFunction("SharedFunction");

        var allTools = new List<AIFunction> { skill1, skill2, sharedFunc };
        var manager = new ToolVisibilityManager(allTools);

        // Act - expand only Skill1
        var visible = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("Skill1"));

        // Assert
        Assert.DoesNotContain(visible, f => f.Name == "Skill1"); // Skill1 hidden (expanded)
        Assert.Contains(visible, f => f.Name == "Skill2"); // Skill2 visible (not expanded)
        Assert.Contains(visible, f => f.Name == "SharedFunction"); // Shared function visible (Skill1 expanded)
        Assert.Single(visible.Where(f => f.Name == "SharedFunction")); // Only one instance
    }

    [Fact]
    public void MultipleSkills_BothExpanded_FunctionVisibleOnce()
    {
        // Arrange
        var skill1 = CreateSkillContainer("Skill1", "First skill", "SharedFunction");
        var skill2 = CreateSkillContainer("Skill2", "Second skill", "SharedFunction");
        var sharedFunc = CreateFunction("SharedFunction");

        var allTools = new List<AIFunction> { skill1, skill2, sharedFunc };
        var manager = new ToolVisibilityManager(allTools);

        // Act - expand both skills
        var visible = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("Skill1", "Skill2"));

        // Assert
        Assert.DoesNotContain(visible, f => f.Name == "Skill1"); // Skill1 hidden (expanded)
        Assert.DoesNotContain(visible, f => f.Name == "Skill2"); // Skill2 hidden (expanded)
        Assert.Contains(visible, f => f.Name == "SharedFunction"); // Shared function visible
        Assert.Single(visible.Where(f => f.Name == "SharedFunction")); // Still only one instance
    }

    [Fact]
    public void SkillReferences_QualifiedNames_ExtractsCorrectly()
    {
        // Arrange - skill references qualified names like "ToolkitName.FunctionName"
        var skill = CreateSkillContainer(
            "TestSkill", 
            "Test skill", 
            "MyToolkit.Function1", 
            "MyToolkit.Function2");
        var func1 = CreateFunction("Function1", "MyToolkit");
        var func2 = CreateFunction("Function2", "MyToolkit");

        var allTools = new List<AIFunction> { skill, func1, func2 };
        var manager = new ToolVisibilityManager(allTools);

        // Act - skill not expanded
        var visibleCollapse = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - functions hidden when skill Collapse
        Assert.Contains(visibleCollapse, f => f.Name == "TestSkill");
        Assert.DoesNotContain(visibleCollapse, f => f.Name == "Function1");
        Assert.DoesNotContain(visibleCollapse, f => f.Name == "Function2");

        // Act - skill expanded
        var visibleExpanded = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("TestSkill"));

        // Assert - functions visible when skill expanded
        Assert.DoesNotContain(visibleExpanded, f => f.Name == "TestSkill");
        Assert.Contains(visibleExpanded, f => f.Name == "Function1");
        Assert.Contains(visibleExpanded, f => f.Name == "Function2");
    }

    [Fact]
    public void MixedSkillsAndToolkits_BothWorkCorrectly()
    {
        // Arrange
        var skill = CreateSkillContainer("TestSkill", "Test skill", "SkillFunc");
        var skillFunc = CreateFunction("SkillFunc");
        
        var ToolkitContainer = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("Toolkit activated"),
            new AIFunctionFactoryOptions
            {
                Name = "ToolkitContainer",
                Description = "Toolkit container",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["ToolkitName"] = "TestToolkit"
                }
            });
        var ToolkitFunc = CreateFunction("ToolkitFunc", "TestToolkit");

        var allTools = new List<AIFunction> { skill, skillFunc, ToolkitContainer, ToolkitFunc };
        var manager = new ToolVisibilityManager(allTools);

        // Act - nothing expanded
        var visibleInitial = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - containers visible, functions hidden
        Assert.Contains(visibleInitial, f => f.Name == "TestSkill");
        Assert.Contains(visibleInitial, f => f.Name == "ToolkitContainer");
        Assert.DoesNotContain(visibleInitial, f => f.Name == "SkillFunc");
        Assert.DoesNotContain(visibleInitial, f => f.Name == "ToolkitFunc");

        // Act - expand both
        var visibleExpanded = manager.GetToolsForAgentTurn(
            allTools,
            ImmutableHashSet.Create("TestToolkit"),
            ImmutableHashSet.Create("TestSkill"));

        // Assert - containers hidden, functions visible
        Assert.DoesNotContain(visibleExpanded, f => f.Name == "TestSkill");
        Assert.DoesNotContain(visibleExpanded, f => f.Name == "ToolkitContainer");
        Assert.Contains(visibleExpanded, f => f.Name == "SkillFunc");
        Assert.Contains(visibleExpanded, f => f.Name == "ToolkitFunc");
    }
}
