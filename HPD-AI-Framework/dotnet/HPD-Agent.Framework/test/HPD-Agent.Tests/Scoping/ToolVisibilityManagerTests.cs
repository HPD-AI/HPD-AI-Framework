using Xunit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent.Collapsing;
using System.Collections.Immutable;

namespace HPD.Agent.Tests.Collapsing;

/// <summary>
/// Comprehensive tests for ToolVisibilityManager to validate all Collapsing scenarios.
/// These tests cover explicit/implicit ToolHarness registration, [Collapse] attribute behavior,
/// orphan function hiding, and skill parent Collapse detection.
/// </summary>
public class ToolVisibilityManagerTests
{
    #region Test Scenario 1: Both ToolHarness and Skills with [Collapse], Both Explicit

    [Fact]
    public void Scenario1_BothCollapsed_BothExplicit_ShowsOnlyContainers()
    {
        // Arrange: ToolHarness has [Collapse], Skills have [Collapse], both explicitly registered
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: true,
            skillsHaveCollapse: true,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            // No expanded ToolHarnesses
            ImmutableHashSet<string>.Empty); // No expanded skills

        // Assert
        visibleTools.Should().HaveCount(2); // Only containers
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisToolHarness"); // Collapse container
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills"); // Collapse container

        // Should NOT contain individual ToolHarness functions
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should NOT contain individual skills
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

    }

    #endregion

    #region Test Scenario 2: ToolHarness Explicit WITHOUT [Collapse], Skills With [Collapse]

    [Fact]
    public void Scenario2_ToolHarnessNotCollapsed_SkillsCollapsed_ShowsAllToolHarnessFunctions()
    {
        // Arrange: ToolHarness NO [Collapse] but explicit, Skills have [Collapse]
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: false,
            skillsHaveCollapse: true,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert - Should show all ToolHarness functions (explicit, no Collapse)
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should show skills Collapse container
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills");

        // Should NOT show individual skills (parent Collapse not expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

    }

    #endregion

    #region Test Scenario 3: ToolHarness With [Collapse], Skills WITHOUT [Collapse], Both Explicit

    [Fact]
    public void Scenario3_ToolHarnessCollapsed_SkillsNotCollapsed_ShowsIndividualSkills()
    {
        // Arrange: ToolHarness has [Collapse], Skills NO [Collapse], both explicit
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: true,
            skillsHaveCollapse: false,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisToolHarness"); // Collapse container
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis"); // Individual skill
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis"); // Individual skill

        // Should NOT show ToolHarness functions (Collapsed ToolHarness not expanded)
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Total: 1 ToolHarness container + 5 skills = 6
        visibleTools.Should().HaveCount(6);
    }

    #endregion

    #region Test Scenario 4: Only Skills Registered (No Explicit ToolHarness), Skills WITHOUT [Collapse]

    [Fact]
    public void Scenario4_OnlySkillsExplicit_NoCollapse_HidesOrphanFunctions()
    {
        // Arrange: Only skills registered (ToolHarness auto-registered), skills NO [Collapse]
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: false,
            skillsHaveCollapse: false,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisSkills"); // Only skills explicit
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert - Skills visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis");

        // Orphan functions should be hidden (ToolHarness auto-registered, not explicit)
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Referenced functions hidden until skill expanded
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
    }

    #endregion

    #region Test Scenario 5: Only Skills Registered, Skills WITH [Collapse]

    [Fact]
    public void Scenario5_OnlySkillsExplicit_WithCollapse_ShowsOnlyCollapseContainer()
    {
        // Arrange: Only skills registered, skills HAVE [Collapse]
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: false,
            skillsHaveCollapse: true,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills"); // Collapse container

        // Individual skills hidden (parent Collapse not expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // ToolHarness functions hidden (orphans)
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");

        visibleTools.Should().HaveCount(1); // Only Collapse container
    }

    #endregion

    #region Test Scenario 6: Collapsed ToolHarness Explicit, No Skills

    [Fact]
    public void Scenario6_CollapsedToolHarnessExplicit_NoSkills_HidesFunctions()
    {
        // Arrange: ToolHarness has [Collapse], explicit, no skills
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: true,
            skillsHaveCollapse: false,
            includeToolHarnessFunctions: true,
            includeSkills: false);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolHarness");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act - Not expanded
        var visibleToolsBeforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert - Before expansion
        visibleToolsBeforeExpansion.Should().Contain(t => t.Name == "FinancialAnalysisToolHarness");
        visibleToolsBeforeExpansion.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleToolsBeforeExpansion.Should().HaveCount(1); // Only ToolHarness container

        // Act - After expansion
        var visibleToolsAfterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisToolHarness"));

        // Assert - After expansion, all functions visible
        visibleToolsAfterExpansion.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleToolsAfterExpansion.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
    }

    #endregion

    #region Test Expansion Behavior

    [Fact]
    public void ExpandSkillCollapse_ShowsIndividualSkills()
    {
        // Arrange
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: true,
            skillsHaveCollapse: true,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act - Expand FinancialAnalysisSkills Collapse
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisSkills"));

        // Assert - Individual skills now visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis");
        visibleTools.Should().Contain(t => t.Name == "PeriodChangeAnalysis");
        visibleTools.Should().Contain(t => t.Name == "CommonSizeBalanceSheet");
        visibleTools.Should().Contain(t => t.Name == "FinancialHealthDashboard");
    }

    [Fact]
    public void ExpandSkill_ShowsReferencedFunctions()
    {
        // Arrange
        var tools = CreateTestTools(
            ToolHarnessHasCollapse: true,
            skillsHaveCollapse: false,
            includeToolHarnessFunctions: true,
            includeSkills: true);
        
        var explicitToolHarnesses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act - Expand both the ToolHarness (so functions are available) AND the skill (so it references them)
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisToolHarness", "QuickLiquidityAnalysis"));

        // Assert - Functions referenced by QuickLiquidityAnalysis now visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateWorkingCapital");
    }

    #endregion

    #region Helper Methods

    private IEnumerable<AIFunction> CreateTestTools(
        bool ToolHarnessHasCollapse,
        bool skillsHaveCollapse,
        bool includeToolHarnessFunctions,
        bool includeSkills)
    {
        var tools = new List<AIFunction>();

        // Add ToolHarness container if Collapsed
        if (ToolHarnessHasCollapse)
        {
            tools.Add(CreateToolHarnessContainer("FinancialAnalysisToolHarness"));
        }

        // Add skills Collapse container if Collapsed
        if (skillsHaveCollapse)
        {
            tools.Add(CreateCollapseContainer("FinancialAnalysisSkills"));
        }

        // Add ToolHarness functions
        if (includeToolHarnessFunctions)
        {
            tools.AddRange(CreateToolHarnessFunctions("FinancialAnalysisToolHarness"));
        }

        // Add skills
        if (includeSkills)
        {
            tools.AddRange(CreateSkills(skillsHaveCollapse ? "FinancialAnalysisSkills" : null));
        }

        return tools;
    }

    private AIFunction CreateToolHarnessContainer(string toolName)
    {
        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{toolName} expanded"),
            new AIFunctionFactoryOptions
            {
                Name = toolName,
                Description = $"{toolName} Collapse container",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["ToolHarnessName"] = toolName,
                    ["ReferencedFunctions"] = new[] { "CalculateCurrentRatio", "CalculateQuickRatio", "CalculateWorkingCapital", "CalculateDebtToEquityRatio", "CalculateDebtToAssetsRatio", "ComprehensiveBalanceSheetAnalysis" },
                    ["FunctionCount"] = 6
                }
            });
    }

    private AIFunction CreateCollapseContainer(string CollapseName)
    {
        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{CollapseName} expanded"),
            new AIFunctionFactoryOptions
            {
                Name = CollapseName,
                Description = $"{CollapseName} Collapse container",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["IsToolHarnessContainer"] = true
                }
            });
    }

    private IEnumerable<AIFunction> CreateToolHarnessFunctions(string parentToolHarness)
    {
        var functionNames = new[]
        {
            "CalculateCurrentRatio",
            "CalculateQuickRatio",
            "CalculateWorkingCapital",
            "CalculateDebtToEquityRatio",
            "CalculateDebtToAssetsRatio",
            "ComprehensiveBalanceSheetAnalysis"
        };

        return functionNames.Select(name => AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{name} result"),
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = $"{name} function",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["ParentToolHarness"] = parentToolHarness
                }
            }));
    }

    private IEnumerable<AIFunction> CreateSkills(string? parentCollapse)
    {
        var skillNames = new[]
        {
            "QuickLiquidityAnalysis",
            "CapitalStructureAnalysis",
            "PeriodChangeAnalysis",
            "CommonSizeBalanceSheet",
            "FinancialHealthDashboard"
        };

        return skillNames.Select(name =>
        {
            var props = new Dictionary<string, object>
            {
                ["IsContainer"] = true,
                ["IsSkill"] = true,
                ["ReferencedFunctions"] = GetReferencedFunctionsForSkill(name),
                ["ReferencedToolHarnesses"] = new[] { "FinancialAnalysisToolHarness" }
            };

            if (parentCollapse != null)
            {
                props["ParentContainer"] = parentCollapse;
            }

            return AIFunctionFactory.Create(
                (object? args, CancellationToken ct) => Task.FromResult<object?>($"{name} executed"),
                new AIFunctionFactoryOptions
                {
                    Name = name,
                    Description = $"{name} skill",
                    AdditionalProperties = props
                });
        });
    }

    private string[] GetReferencedFunctionsForSkill(string skillName)
    {
        return skillName switch
        {
            "QuickLiquidityAnalysis" => new[]
            {
                "FinancialAnalysisToolHarness.CalculateCurrentRatio",
                "FinancialAnalysisToolHarness.CalculateQuickRatio",
                "FinancialAnalysisToolHarness.CalculateWorkingCapital"
            },
            "CapitalStructureAnalysis" => new[]
            {
                "FinancialAnalysisToolHarness.CalculateDebtToEquityRatio",
                "FinancialAnalysisToolHarness.CalculateDebtToAssetsRatio"
            },
            _ => Array.Empty<string>()
        };
    }

    private AIFunction CreateNonCollapsedFunction(string name)
    {
        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{name} result"),
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = $"{name} function"
            });
    }

    #endregion

    #region Collapsed ToolHarness/Skill Expansion Tests

    [Fact]
    public void CollapsedToolHarness_HidesAfterExpansion()
    {
        // Arrange: Create MathTools with [Collapse], containing functions and skills
        var tools = CreateMathToolsTools();
        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Initially, MathTools container should be visible
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible initially
        initialTools.Should().Contain(t => t.Name == "MathTools");
        initialTools.Should().NotContain(t => t.Name == "Add");
        initialTools.Should().NotContain(t => t.Name == "SolveQuadratic");

        // Act: After expansion, MathTools should hide and contents should show
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers);

        // Assert: Container hidden, contents visible
        expandedTools.Should().NotContain(t => t.Name == "MathTools");
        expandedTools.Should().Contain(t => t.Name == "Add");
        expandedTools.Should().Contain(t => t.Name == "Multiply");
        expandedTools.Should().Contain(t => t.Name == "SolveQuadratic");
    }

    [Fact]
    public void CollapsedToolHarness_ShowsFunctionsAfterExpansion()
    {
        // Arrange
        var tools = CreateMathToolsTools();
        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand MathTools
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers);

        // Assert: All AI functions from MathTools should be visible
        visibleTools.Should().Contain(t => t.Name == "Add");
        visibleTools.Should().Contain(t => t.Name == "Multiply");
        visibleTools.Should().Contain(t => t.Name == "Abs");
        visibleTools.Should().Contain(t => t.Name == "Square");
        visibleTools.Should().Contain(t => t.Name == "Subtract");
        visibleTools.Should().Contain(t => t.Name == "Min");
    }

    [Fact]
    public void CollapsedToolHarness_ShowsSkillsAfterExpansion_ExpandedSkillContainers()
    {
        // Arrange
        var tools = CreateMathToolsTools();
        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand MathTools (goes into ExpandedSkillContainers)
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers);

        // Assert: SolveQuadratic skill should be visible when parent is in ExpandedSkillContainers
        visibleTools.Should().Contain(t => t.Name == "SolveQuadratic");
    }

    [Fact]
    public void CollapsedToolHarness_ShowsSkillsAfterExpansion_ExpandedSkillsParameter()
    {
        // Arrange
        var tools = CreateMathToolsTools();
        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand MathTools via expandedSkills parameter (second parameter)
        var expandedSkills = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            expandedSkills);

        // Assert: SolveQuadratic skill should be visible when parent is in expandedSkills
        visibleTools.Should().Contain(t => t.Name == "SolveQuadratic");
    }

    [Fact]
    public void CollapsedToolHarness_OnlyHidesItself_NotOtherContainers()
    {
        // Arrange: Two separate Collapse containers
        var tools = new List<AIFunction>();
        tools.AddRange(CreateMathToolsTools());
        tools.Add(CreateCollapseContainer("OtherToolHarness", "Other ToolHarness for testing"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand only MathTools
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers);

        // Assert: MathTools hidden, but OtherToolHarness still visible
        visibleTools.Should().NotContain(t => t.Name == "MathTools");
        visibleTools.Should().Contain(t => t.Name == "OtherToolHarness");
    }

    [Fact]
    public void SkillContainer_VisibleWhenParentCollapseExpandedInToolHarnesses()
    {
        // Arrange: Skill with parent Collapse
        var tools = CreateMathToolsTools();
        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand parent Collapse in ExpandedSkillContainers
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers);

        // Assert: Skill should be visible
        var solveQuadratic = visibleTools.FirstOrDefault(t => t.Name == "SolveQuadratic");
        solveQuadratic.Should().NotBeNull();
        solveQuadratic!.AdditionalProperties?["ParentContainer"].Should().Be("MathTools");
    }

    #endregion

    #region Helper Methods for New Tests

    private List<AIFunction> CreateMathToolsTools()
    {
        var tools = new List<AIFunction>();

        // 1. Collapse container for MathTools
        tools.Add(CreateCollapseContainer(
            "MathTools",
            "Math Operations. Contains 7 functions: Add, Multiply, Abs, Square, Subtract, Min, SolveQuadratic"));

        // 2. AI Functions in MathTools
        tools.Add(CreateToolHarnessFunction("Add", "MathTools", "Adds two numbers"));
        tools.Add(CreateToolHarnessFunction("Multiply", "MathTools", "Multiplies two numbers"));
        tools.Add(CreateToolHarnessFunction("Abs", "MathTools", "Returns absolute value"));
        tools.Add(CreateToolHarnessFunction("Square", "MathTools", "Squares a number"));
        tools.Add(CreateToolHarnessFunction("Subtract", "MathTools", "Subtracts b from a"));
        tools.Add(CreateToolHarnessFunction("Min", "MathTools", "Returns minimum of two numbers"));

        // 3. Skill container in MathTools
        tools.Add(CreateSkillContainer(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathTools",
            new[] { "MathTools.Multiply", "MathTools.Add", "MathTools.Subtract" },
            new[] { "MathTools" }));

        return tools;
    }

    private AIFunction CreateCollapseContainer(string name, string description)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => name + " expanded",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["IsToolHarnessContainer"] = true,
                    ["ReferencedFunctions"] = new string[] { },
                    ["FunctionCount"] = 0
                }
            });
    }

    private AIFunction CreateToolHarnessFunction(string name, string parentToolHarness, string description)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => "Result",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["ParentToolHarness"] = parentToolHarness,
                    ["IsContainer"] = false
                }
            });
    }

    private AIFunction CreateSkillContainer(
        string name,
        string description,
        string parentSkillContainer,
        string[] referencedFunctions,
        string[] referencedToolHarnesses)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => name + " activated",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["IsSkill"] = true,
                    ["ParentContainer"] = parentSkillContainer,
                    ["ReferencedFunctions"] = referencedFunctions,
                    ["ReferencedToolHarnesses"] = referencedToolHarnesses
                }
            });
    }

    private AIFunction CreateSkillWithReferences(
        string name,
        string description,
        string? parentCollapse,
        string[] referencedFunctions,
        string[] referencedToolHarnesses)
    {
        var additionalProps = new Dictionary<string, object>
        {
            ["IsContainer"] = true,
            ["IsSkill"] = true,
            ["ReferencedFunctions"] = referencedFunctions,
            ["ReferencedToolHarnesses"] = referencedToolHarnesses
        };

        if (parentCollapse != null)
        {
            additionalProps["ParentContainer"] = parentCollapse;
        }

        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => name + " activated",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = additionalProps
            });
    }

    #endregion

    #region Collapsed ToolHarness Referenced by Skill Tests

    [Fact]
    public void CollapsedToolHarnessReferencedBySkill_HidesToolHarnessContainer_ShowsOnlySkill()
    {
        // Arrange: Collapsed ToolHarness referenced by a skill (NOT explicitly registered)
        var tools = new List<AIFunction>();

        // Add Collapsed ToolHarness container
        tools.Add(CreateToolHarnessContainer("FinancialAnalysisToolHarness"));

        // Add ToolHarness functions
        tools.AddRange(CreateToolHarnessFunctions("FinancialAnalysisToolHarness"));

        // Add skill that references the Collapsed ToolHarness
        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolHarness.CalculateCurrentRatio",
                "FinancialAnalysisToolHarness.CalculateQuickRatio"
            },
            referencedToolHarnesses: new[] { "FinancialAnalysisToolHarness" }));

        // ToolHarness is NOT explicitly registered - only implicitly via skill reference
        var explicitToolHarnesses = ImmutableHashSet<string>.Empty;

        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act: No expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Should show ONLY the skill container, NOT the ToolHarness Collapse container
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisToolHarness");

        // Functions should be hidden (skill not expanded yet)
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "CalculateQuickRatio");
    }

    [Fact]
    public void CollapsedToolHarnessReferencedBySkill_ExpandSkill_ShowsReferencedFunctions()
    {
        // Arrange: Collapsed ToolHarness referenced by a skill
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainer("FinancialAnalysisToolHarness"));
        tools.AddRange(CreateToolHarnessFunctions("FinancialAnalysisToolHarness"));

        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolHarness.CalculateCurrentRatio",
                "FinancialAnalysisToolHarness.CalculateQuickRatio"
            },
            referencedToolHarnesses: new[] { "FinancialAnalysisToolHarness" }));

        var explicitToolHarnesses = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act: Expand the skill (NOT the ToolHarness)
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            // ToolHarness Collapse NOT expanded
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "QuickLiquidityAnalysis")); // Skill expanded

        // Assert: Skill bypass should make referenced functions visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");

        // Skill container should be hidden (it's expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // ToolHarness Collapse container should still be hidden (implicitly registered)
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisToolHarness");
    }

    [Fact]
    public void CollapsedToolHarnessReferencedBySkill_OrphanFunctions_StayHidden()
    {
        // Arrange: Collapsed ToolHarness with some functions referenced by skill, others are orphans
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainer("FinancialAnalysisToolHarness"));
        tools.AddRange(CreateToolHarnessFunctions("FinancialAnalysisToolHarness"));

        // Skill only references 2 functions out of 6
        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolHarness.CalculateCurrentRatio",
                "FinancialAnalysisToolHarness.CalculateQuickRatio"
            },
            referencedToolHarnesses: new[] { "FinancialAnalysisToolHarness" }));

        var explicitToolHarnesses = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act: Expand the skill
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "QuickLiquidityAnalysis"));

        // Assert: Referenced functions visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");

        // Orphan functions (not referenced by any skill) should remain HIDDEN
        visibleTools.Should().NotContain(t => t.Name == "CalculateWorkingCapital");
        visibleTools.Should().NotContain(t => t.Name == "CalculateDebtToEquityRatio");
        visibleTools.Should().NotContain(t => t.Name == "CalculateDebtToAssetsRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
    }

    [Fact]
    public void CollapsedToolHarnessReferencedBySkill_ExpandToolHarness_ShowsAllFunctions()
    {
        // Arrange: Collapsed ToolHarness referenced by skill
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainer("FinancialAnalysisToolHarness"));
        tools.AddRange(CreateToolHarnessFunctions("FinancialAnalysisToolHarness"));

        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolHarness.CalculateCurrentRatio",
                "FinancialAnalysisToolHarness.CalculateQuickRatio"
            },
            referencedToolHarnesses: new[] { "FinancialAnalysisToolHarness" }));

        var explicitToolHarnesses = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitToolHarnesses);

        // Act: Expand the ToolHarness Collapse (not the skill)
        // This is an edge case - user manually expands the ToolHarness even though it was implicitly registered
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisToolHarness"));

        // Assert: ALL ToolHarness functions should be visible (ToolHarness Collapse expanded)
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateWorkingCapital");
        visibleTools.Should().Contain(t => t.Name == "CalculateDebtToEquityRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateDebtToAssetsRatio");
        visibleTools.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // ToolHarness container should be hidden (expanded)
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisToolHarness");

        // Skill container should still be visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
    }

    #endregion

    #region Regression Tests: IsToolHarnessContainer Flag (ToolHarness Attribute Migration)

    /// <summary>
    /// Regression test for the ToolHarness attribute migration.
    /// When a toolharness is marked with [Collapse(Collapsed=true)], the source generator sets
    /// IsToolHarnessContainer=true.
    /// ToolVisibilityManager must recognize the generated marker to properly hide skills inside collapsed toolharnesses.
    /// </summary>
    [Fact]
    public void CollapsedToolHarness_WithIsToolHarnessContainerFlag_HidesSkillsUntilExpanded()
    {
        // Arrange: Create a collapsed toolharness using the NEW IsToolHarnessContainer flag
        // This simulates what the source generator produces for [Collapse("...", Collapsed = true)]
        var tools = new List<AIFunction>();

        // ToolHarness container with IsToolHarnessContainer=true (new flag from [Collapse] attribute)
        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "MathToolHarness",
            "Math Operations. Contains 3 functions: Add, Multiply, SolveQuadratic"));

        // Functions in the toolharness
        tools.Add(CreateToolHarnessFunction("Add", "MathToolHarness", "Adds two numbers"));
        tools.Add(CreateToolHarnessFunction("Multiply", "MathToolHarness", "Multiplies two numbers"));

        // Skill inside the collapsed toolharness (should be hidden initially)
        tools.Add(CreateSkillInsideCollapsedToolHarness(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathToolHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: No expansions - initial state
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Only the toolharness container should be visible initially
        initialTools.Should().Contain(t => t.Name == "MathToolHarness",
            "collapsed toolharness container should be visible");
        initialTools.Should().NotContain(t => t.Name == "Add",
            "functions inside collapsed toolharness should be hidden");
        initialTools.Should().NotContain(t => t.Name == "Multiply",
            "functions inside collapsed toolharness should be hidden");
        initialTools.Should().NotContain(t => t.Name == "SolveQuadratic",
            "REGRESSION: skill inside collapsed toolharness should be hidden until parent is expanded");

        // Verify we only have the container
        initialTools.Should().HaveCount(1, "only the toolharness container should be visible");
    }

    [Fact]
    public void CollapsedToolHarness_WithIsToolHarnessContainerFlag_ShowsSkillsAfterExpansion()
    {
        // Arrange: Create a collapsed toolharness using the NEW IsToolHarnessContainer flag
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "MathToolHarness",
            "Math Operations. Contains 3 functions: Add, Multiply, SolveQuadratic"));

        tools.Add(CreateToolHarnessFunction("Add", "MathToolHarness", "Adds two numbers"));
        tools.Add(CreateToolHarnessFunction("Multiply", "MathToolHarness", "Multiplies two numbers"));

        tools.Add(CreateSkillInsideCollapsedToolHarness(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathToolHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand the toolharness
        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "MathToolHarness"));

        // Assert: Container hidden, contents visible
        expandedTools.Should().NotContain(t => t.Name == "MathToolHarness",
            "expanded toolharness container should be hidden");
        expandedTools.Should().Contain(t => t.Name == "Add",
            "functions should be visible after toolharness expansion");
        expandedTools.Should().Contain(t => t.Name == "Multiply",
            "functions should be visible after toolharness expansion");
        expandedTools.Should().Contain(t => t.Name == "SolveQuadratic",
            "skill should be visible after parent toolharness is expanded");
    }

    [Fact]
    public void CollapsedToolHarness_IsToolHarnessContainerFlag_CollapsesMultipleToolHarnesses()
    {
        var tools = new List<AIFunction>();

        tools.Add(CreateCollapseContainer("SearchToolHarness", "Search toolharness using IsToolHarnessContainer flag"));
        tools.Add(CreateToolHarnessFunction("SearchFunc", "SearchToolHarness", "A search function"));
        tools.Add(CreateToolHarnessContainerWithNewFlag("NewToolHarness", "New toolharness using IsToolHarnessContainer flag"));
        tools.Add(CreateToolHarnessFunction("NewFunc", "NewToolHarness", "A new function"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: No expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Both containers should be visible, both functions hidden
        visibleTools.Should().Contain(t => t.Name == "SearchToolHarness",
            "collapsed toolharness should be visible");
        visibleTools.Should().Contain(t => t.Name == "NewToolHarness",
            "second collapsed toolharness should be visible");
        visibleTools.Should().NotContain(t => t.Name == "SearchFunc",
            "function in collapsed toolharness should be hidden");
        visibleTools.Should().NotContain(t => t.Name == "NewFunc",
            "function in second collapsed toolharness should be hidden");
        visibleTools.Should().HaveCount(2, "only the two toolharness containers should be visible");
    }

    /// <summary>
    /// Creates a toolharness container using the NEW IsToolHarnessContainer flag.
    /// This simulates what the source generator produces for [Collapse("...", Collapsed = true)]
    /// </summary>
    private AIFunction CreateToolHarnessContainerWithNewFlag(string name, string description)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => name + " expanded",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["IsToolHarnessContainer"] = true, // NEW flag from [Collapse] attribute
                    ["ReferencedFunctions"] = new string[] { },
                    ["FunctionCount"] = 0
                }
            });
    }

    /// <summary>
    /// Creates a skill that is inside a collapsed toolharness.
    /// The ParentContainer property indicates the skill belongs to the parent toolharness.
    /// </summary>
    private AIFunction CreateSkillInsideCollapsedToolHarness(string name, string description, string parentToolHarness)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => name + " activated",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsContainer"] = true,
                    ["IsSkill"] = true,
                    ["ParentContainer"] = parentToolHarness, // Links skill to parent toolharness
                    ["ReferencedFunctions"] = Array.Empty<string>(),
                    ["ReferencedToolHarnesses"] = new[] { parentToolHarness }
                }
            });
    }

    #endregion

    #region NeverCollapse Runtime Config Tests

    /// <summary>
    /// Tests the NeverCollapse runtime config feature.
    /// When a toolharness is in the NeverCollapse list, its functions should be visible directly
    /// even if the toolharness has a container (description provided).
    /// </summary>
    [Fact]
    public void NeverCollapse_ToolHarnessInList_ShowsFunctionsDirectly()
    {
        // Arrange: Create a collapsed toolharness that would normally hide its functions
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "FileToolHarness",
            "File operations for reading and writing files"));

        tools.Add(CreateToolHarnessFunction("ReadFile", "FileToolHarness", "Reads a file"));
        tools.Add(CreateToolHarnessFunction("WriteFile", "FileToolHarness", "Writes a file"));

        // Create manager with FileToolHarness in NeverCollapse list
        var neverCollapse = new HashSet<string> { "FileToolHarness" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act: Get visible tools without any expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Functions should be visible directly, container should be hidden
        visibleTools.Should().NotContain(t => t.Name == "FileToolHarness",
            "container should be hidden when toolharness is in NeverCollapse");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "functions should be visible directly");
        visibleTools.Should().Contain(t => t.Name == "WriteFile",
            "functions should be visible directly");
        visibleTools.Should().HaveCount(2, "only the functions should be visible");
    }

    [Fact]
    public void NeverCollapse_ToolHarnessNotInList_CollapsesNormally()
    {
        // Arrange: Create a collapsed toolharness
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "DatabaseToolHarness",
            "Database operations"));

        tools.Add(CreateToolHarnessFunction("Query", "DatabaseToolHarness", "Executes a query"));
        tools.Add(CreateToolHarnessFunction("Insert", "DatabaseToolHarness", "Inserts a record"));

        // Create manager with a DIFFERENT toolharness in NeverCollapse (not DatabaseToolHarness)
        var neverCollapse = new HashSet<string> { "FileToolHarness" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act: Get visible tools without any expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally - only container visible
        visibleTools.Should().Contain(t => t.Name == "DatabaseToolHarness",
            "container should be visible when toolharness is NOT in NeverCollapse");
        visibleTools.Should().NotContain(t => t.Name == "Query",
            "functions should be hidden behind container");
        visibleTools.Should().NotContain(t => t.Name == "Insert",
            "functions should be hidden behind container");
        visibleTools.Should().HaveCount(1, "only the container should be visible");
    }

    [Fact]
    public void NeverCollapse_MixedToolHarnesses_OnlyAffectsListedToolHarnesses()
    {
        // Arrange: Create two collapsed toolharnesses
        var tools = new List<AIFunction>();

        // FileToolHarness - will be in NeverCollapse
        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "FileToolHarness",
            "File operations"));
        tools.Add(CreateToolHarnessFunction("ReadFile", "FileToolHarness", "Reads a file"));

        // DatabaseToolHarness - will NOT be in NeverCollapse
        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "DatabaseToolHarness",
            "Database operations"));
        tools.Add(CreateToolHarnessFunction("Query", "DatabaseToolHarness", "Executes a query"));

        // Only FileToolHarness in NeverCollapse
        var neverCollapse = new HashSet<string> { "FileToolHarness" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: FileToolHarness functions visible, DatabaseToolHarness collapsed
        visibleTools.Should().NotContain(t => t.Name == "FileToolHarness",
            "FileToolHarness container should be hidden (in NeverCollapse)");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "FileToolHarness functions should be visible directly");

        visibleTools.Should().Contain(t => t.Name == "DatabaseToolHarness",
            "DatabaseToolHarness container should be visible (not in NeverCollapse)");
        visibleTools.Should().NotContain(t => t.Name == "Query",
            "DatabaseToolHarness functions should be hidden behind container");

        visibleTools.Should().HaveCount(2, "ReadFile + DatabaseToolHarness container");
    }

    [Fact]
    public void NeverCollapse_CaseInsensitive_MatchesRegardlessOfCase()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "FileToolHarness",  // PascalCase
            "File operations"));
        tools.Add(CreateToolHarnessFunction("ReadFile", "FileToolHarness", "Reads a file"));

        // NeverCollapse with different casing
        var neverCollapse = new HashSet<string> { "filetoolharness" };  // lowercase
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Should match case-insensitively
        visibleTools.Should().NotContain(t => t.Name == "FileToolHarness",
            "container should be hidden (case-insensitive match)");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "functions should be visible directly");
    }

    [Fact]
    public void NeverCollapse_EmptyList_AllToolHarnessesCollapseNormally()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "FileToolHarness",
            "File operations"));
        tools.Add(CreateToolHarnessFunction("ReadFile", "FileToolHarness", "Reads a file"));

        // Empty NeverCollapse list
        var neverCollapse = new HashSet<string>();
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally
        visibleTools.Should().Contain(t => t.Name == "FileToolHarness",
            "container should be visible");
        visibleTools.Should().NotContain(t => t.Name == "ReadFile",
            "functions should be hidden");
    }

    [Fact]
    public void NeverCollapse_NullList_AllToolHarnessesCollapseNormally()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "FileToolHarness",
            "File operations"));
        tools.Add(CreateToolHarnessFunction("ReadFile", "FileToolHarness", "Reads a file"));

        // Null NeverCollapse list (uses constructor overload)
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapseToolHarnesses: null);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally
        visibleTools.Should().Contain(t => t.Name == "FileToolHarness",
            "container should be visible");
        visibleTools.Should().NotContain(t => t.Name == "ReadFile",
            "functions should be hidden");
    }

    [Fact]
    public void NeverCollapse_WithSkillsInsideToolHarness_ShowsSkillsDirectly()
    {
        // Arrange: ToolHarness with both functions and skills
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "MathToolHarness",
            "Math operations"));
        tools.Add(CreateToolHarnessFunction("Add", "MathToolHarness", "Adds two numbers"));
        tools.Add(CreateSkillInsideCollapsedToolHarness(
            "SolveEquation",
            "Solves equations",
            "MathToolHarness"));

        // MathToolHarness in NeverCollapse
        var neverCollapse = new HashSet<string> { "MathToolHarness" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Both functions and skills should be visible directly
        visibleTools.Should().NotContain(t => t.Name == "MathToolHarness",
            "container should be hidden");
        visibleTools.Should().Contain(t => t.Name == "Add",
            "functions should be visible directly");
        visibleTools.Should().Contain(t => t.Name == "SolveEquation",
            "skills should be visible directly");
    }

    #endregion

    #region SubAgent Visibility Tests

    /// <summary>
    /// Regression test: SubAgents should use ParentToolHarness metadata, not ToolHarnessName.
    /// This ensures SubAgents follow the same collapsing rules as Functions and Skills.
    /// </summary>
    [Fact]
    public void SubAgent_UsesParentToolHarness_NotToolHarnessName()
    {
        // Arrange: Create a SubAgent with ParentToolHarness metadata (correct)
        var subAgent = CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent",
            "MathToolHarness");

        // Assert: Should have ParentToolHarness, not ToolHarnessName
        subAgent.AdditionalProperties.Should().ContainKey("ParentToolHarness");
        subAgent.AdditionalProperties.Should().NotContainKey("ToolHarnessName");
        subAgent.AdditionalProperties?["ParentToolHarness"].Should().Be("MathToolHarness");
    }

    [Fact]
    public void SubAgent_InsideCollapsedToolHarness_HiddenUntilToolHarnessExpanded()
    {
        // Arrange: Collapsed ToolHarness with functions and a SubAgent
        var tools = new List<AIFunction>();

        // ToolHarness container (collapsed)
        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "MathToolHarness",
            "Math operations with research capabilities"));

        // Regular functions
        tools.Add(CreateToolHarnessFunction("Add", "MathToolHarness", "Adds two numbers"));
        tools.Add(CreateToolHarnessFunction("Multiply", "MathToolHarness", "Multiplies two numbers"));

        // SubAgent inside the toolharness
        tools.Add(CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent for math problems",
            "MathToolHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Initial state (no expansions)
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible, SubAgent hidden
        initialTools.Should().Contain(t => t.Name == "MathToolHarness");
        initialTools.Should().NotContain(t => t.Name == "Add");
        initialTools.Should().NotContain(t => t.Name == "ResearchAgent",
            "SubAgent should be hidden when parent toolharness is collapsed");
        initialTools.Should().HaveCount(1);
    }

    [Fact]
    public void SubAgent_InsideCollapsedToolHarness_VisibleAfterToolHarnessExpanded()
    {
        // Arrange: Collapsed ToolHarness with SubAgent
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "MathToolHarness",
            "Math operations with research capabilities"));

        tools.Add(CreateToolHarnessFunction("Add", "MathToolHarness", "Adds two numbers"));
        tools.Add(CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent",
            "MathToolHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand the toolharness
        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "MathToolHarness"));

        // Assert: Container hidden, contents visible (including SubAgent)
        expandedTools.Should().NotContain(t => t.Name == "MathToolHarness");
        expandedTools.Should().Contain(t => t.Name == "Add");
        expandedTools.Should().Contain(t => t.Name == "ResearchAgent",
            "SubAgent should be visible when parent toolharness is expanded");
    }

    [Fact]
    public void SubAgent_WithoutParentToolHarness_AlwaysVisible()
    {
        // Arrange: SubAgent without ParentToolHarness (standalone)
        var tools = new List<AIFunction>();

        // SubAgent without ParentToolHarness
        var subAgent = AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => "Result",
            new AIFunctionFactoryOptions
            {
                Name = "StandaloneAgent",
                Description = "Standalone agent not in a toolharness",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "ThreadNative"
                    // No ParentToolHarness!
                }
            });

        tools.Add(subAgent);

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: SubAgent should be visible (no parent to collapse it)
        visibleTools.Should().Contain(t => t.Name == "StandaloneAgent");
    }

    [Fact]
    public void SubAgent_MultipleInSameToolHarness_AllHiddenAndShownTogether()
    {
        // Arrange: Multiple SubAgents in the same collapsed toolharness
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "ResearchToolHarness",
            "Research toolharness with multiple specialized agents"));

        tools.Add(CreateSubAgentFunction("WebSearchAgent", "Web search specialist", "ResearchToolHarness"));
        tools.Add(CreateSubAgentFunction("DataAnalysisAgent", "Data analysis specialist", "ResearchToolHarness"));
        tools.Add(CreateSubAgentFunction("SummaryAgent", "Summary specialist", "ResearchToolHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Before expansion
        var beforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: All SubAgents hidden
        beforeExpansion.Should().Contain(t => t.Name == "ResearchToolHarness");
        beforeExpansion.Should().NotContain(t => t.Name == "WebSearchAgent");
        beforeExpansion.Should().NotContain(t => t.Name == "DataAnalysisAgent");
        beforeExpansion.Should().NotContain(t => t.Name == "SummaryAgent");

        // Act: After expansion
        var afterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ResearchToolHarness"));

        // Assert: All SubAgents visible
        afterExpansion.Should().NotContain(t => t.Name == "ResearchToolHarness");
        afterExpansion.Should().Contain(t => t.Name == "WebSearchAgent");
        afterExpansion.Should().Contain(t => t.Name == "DataAnalysisAgent");
        afterExpansion.Should().Contain(t => t.Name == "SummaryAgent");
    }

    [Fact]
    public void SubAgent_MixedWithFunctionsAndSkills_AllFollowSameCollapsingRules()
    {
        // Arrange: ToolHarness with functions, skills, AND SubAgents
        var tools = new List<AIFunction>();

        tools.Add(CreateToolHarnessContainerWithNewFlag(
            "ComprehensiveToolHarness",
            "ToolHarness with functions, skills, and sub-agents"));

        // Regular function
        tools.Add(CreateToolHarnessFunction("Calculate", "ComprehensiveToolHarness", "Calculation function"));

        // Skill inside toolharness
        tools.Add(CreateSkillInsideCollapsedToolHarness(
            "AnalysisSkill",
            "Analysis skill",
            "ComprehensiveToolHarness"));

        // SubAgent inside toolharness
        tools.Add(CreateSubAgentFunction(
            "ExpertAgent",
            "Expert agent",
            "ComprehensiveToolHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Before expansion
        var beforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible
        beforeExpansion.Should().Contain(t => t.Name == "ComprehensiveToolHarness");
        beforeExpansion.Should().NotContain(t => t.Name == "Calculate");
        beforeExpansion.Should().NotContain(t => t.Name == "AnalysisSkill");
        beforeExpansion.Should().NotContain(t => t.Name == "ExpertAgent");
        beforeExpansion.Should().HaveCount(1);

        // Act: After expansion
        var afterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ComprehensiveToolHarness"));

        // Assert: All contents visible (function, skill, SubAgent)
        afterExpansion.Should().NotContain(t => t.Name == "ComprehensiveToolHarness");
        afterExpansion.Should().Contain(t => t.Name == "Calculate");
        afterExpansion.Should().Contain(t => t.Name == "AnalysisSkill");
        afterExpansion.Should().Contain(t => t.Name == "ExpertAgent");
    }

    /// <summary>
    /// Creates a SubAgent AIFunction with correct ParentToolHarness metadata.
    /// This simulates what the source generator produces after the fix.
    /// </summary>
    private AIFunction CreateSubAgentFunction(
        string name,
        string description,
        string parentToolHarness)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => $"{name} result",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "ThreadNative",
                    ["ParentToolHarness"] = parentToolHarness  //  Correct key (not ToolHarnessName)
                }
            });
    }

    #endregion
}
