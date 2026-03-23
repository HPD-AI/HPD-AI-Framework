using Xunit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent.Collapsing;
using System.Collections.Immutable;

namespace HPD.Agent.Tests.Collapsing;

/// <summary>
/// Comprehensive tests for ToolVisibilityManager to validate all Collapsing scenarios.
/// These tests cover explicit/implicit Toolkit registration, [Collapse] attribute behavior,
/// orphan function hiding, and skill parent Collapse detection.
/// </summary>
public class ToolVisibilityManagerTests
{
    #region Test Scenario 1: Both Toolkit and Skills with [Collapse], Both Explicit

    [Fact]
    public void Scenario1_BothCollapsed_BothExplicit_ShowsOnlyContainers()
    {
        // Arrange: Toolkit has [Collapse], Skills have [Collapse], both explicitly registered
        var tools = CreateTestTools(
            ToolkitHasCollapse: true,
            skillsHaveCollapse: true,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolkit",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty, // No expanded Toolkits
            ImmutableHashSet<string>.Empty); // No expanded skills

        // Assert
        visibleTools.Should().HaveCount(2); // Only containers, no read_skill_document (no skills expanded)
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisToolkit"); // Collapse container
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills"); // Collapse container

        // Should NOT contain individual Toolkit functions
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should NOT contain individual skills
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // Should NOT contain read_skill_document (no skills with documents expanded)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");
    }

    #endregion

    #region Test Scenario 2: Toolkit Explicit WITHOUT [Collapse], Skills With [Collapse]

    [Fact]
    public void Scenario2_ToolkitNotCollapsed_SkillsCollapsed_ShowsAllToolkitFunctions()
    {
        // Arrange: Toolkit NO [Collapse] but explicit, Skills have [Collapse]
        var tools = CreateTestTools(
            ToolkitHasCollapse: false,
            skillsHaveCollapse: true,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolkit",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - Should show all Toolkit functions (explicit, no Collapse)
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should show skills Collapse container
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills");

        // Should NOT show individual skills (parent Collapse not expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // Should NOT contain read_skill_document (no skills with documents expanded)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");
    }

    #endregion

    #region Test Scenario 3: Toolkit With [Collapse], Skills WITHOUT [Collapse], Both Explicit

    [Fact]
    public void Scenario3_ToolkitCollapsed_SkillsNotCollapsed_ShowsIndividualSkills()
    {
        // Arrange: Toolkit has [Collapse], Skills NO [Collapse], both explicit
        var tools = CreateTestTools(
            ToolkitHasCollapse: true,
            skillsHaveCollapse: false,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolkit",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisToolkit"); // Collapse container
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis"); // Individual skill
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis"); // Individual skill

        // Should NOT show Toolkit functions (Collapsed Toolkit not expanded)
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should NOT contain read_skill_document (no skills expanded - only containers visible)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");

        // Total: 1 Toolkit container + 5 skills = 6
        visibleTools.Should().HaveCount(6);
    }

    #endregion

    #region Test Scenario 4: Only Skills Registered (No Explicit Toolkit), Skills WITHOUT [Collapse]

    [Fact]
    public void Scenario4_OnlySkillsExplicit_NoCollapse_HidesOrphanFunctions()
    {
        // Arrange: Only skills registered (Toolkit auto-registered), skills NO [Collapse]
        var tools = CreateTestTools(
            ToolkitHasCollapse: false,
            skillsHaveCollapse: false,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisSkills"); // Only skills explicit
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - Skills visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis");

        // Orphan functions should be hidden (Toolkit auto-registered, not explicit)
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should NOT contain read_skill_document (no skills expanded - only skill containers visible)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");
        
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
            ToolkitHasCollapse: false,
            skillsHaveCollapse: true,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills"); // Collapse container

        // Individual skills hidden (parent Collapse not expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // Should NOT contain read_skill_document (no skills expanded)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");

        // Toolkit functions hidden (orphans)
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");

        visibleTools.Should().HaveCount(1); // Only Collapse container, no read_skill_document
    }

    #endregion

    #region Test Scenario 6: Collapsed Toolkit Explicit, No Skills

    [Fact]
    public void Scenario6_CollapsedToolkitExplicit_NoSkills_HidesFunctions()
    {
        // Arrange: Toolkit has [Collapse], explicit, no skills
        var tools = CreateTestTools(
            ToolkitHasCollapse: true,
            skillsHaveCollapse: false,
            includeToolkitFunctions: true,
            includeSkills: false);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolkit");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act - Not expanded
        var visibleToolsBeforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - Before expansion
        visibleToolsBeforeExpansion.Should().Contain(t => t.Name == "FinancialAnalysisToolkit");
        visibleToolsBeforeExpansion.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleToolsBeforeExpansion.Should().NotContain(t => t.Name == "ReadSkillDocument"); // No skills expanded
        visibleToolsBeforeExpansion.Should().HaveCount(1); // Only Toolkit container

        // Act - After expansion
        var visibleToolsAfterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisToolkit"), // Expanded
            ImmutableHashSet<string>.Empty);

        // Assert - After expansion, all functions visible
        visibleToolsAfterExpansion.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleToolsAfterExpansion.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
        // Still no read_skill_document (no skills expanded, only Toolkit expanded)
        visibleToolsAfterExpansion.Should().NotContain(t => t.Name == "ReadSkillDocument");
    }

    #endregion

    #region Test Expansion Behavior

    [Fact]
    public void ExpandSkillCollapse_ShowsIndividualSkills()
    {
        // Arrange
        var tools = CreateTestTools(
            ToolkitHasCollapse: true,
            skillsHaveCollapse: true,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolkit",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act - Expand FinancialAnalysisSkills Collapse
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
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
            ToolkitHasCollapse: true,
            skillsHaveCollapse: false,
            includeToolkitFunctions: true,
            includeSkills: true);
        
        var explicitToolkits = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisToolkit",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act - Expand both the Toolkit (so functions are available) AND the skill (so it references them)
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisToolkit"), // Expand Toolkit
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "QuickLiquidityAnalysis")); // Expand skill

        // Assert - Functions referenced by QuickLiquidityAnalysis now visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateWorkingCapital");
    }

    #endregion

    #region Helper Methods

    private IEnumerable<AIFunction> CreateTestTools(
        bool ToolkitHasCollapse,
        bool skillsHaveCollapse,
        bool includeToolkitFunctions,
        bool includeSkills)
    {
        var tools = new List<AIFunction>();

        // Add Toolkit container if Collapsed
        if (ToolkitHasCollapse)
        {
            tools.Add(CreateToolkitContainer("FinancialAnalysisToolkit"));
        }

        // Add skills Collapse container if Collapsed
        if (skillsHaveCollapse)
        {
            tools.Add(CreateCollapseContainer("FinancialAnalysisSkills"));
        }

        // Add Toolkit functions
        if (includeToolkitFunctions)
        {
            tools.AddRange(CreateToolkitFunctions("FinancialAnalysisToolkit"));
        }

        // Add skills
        if (includeSkills)
        {
            tools.AddRange(CreateSkills(skillsHaveCollapse ? "FinancialAnalysisSkills" : null));
        }

        return tools;
    }

    private AIFunction CreateToolkitContainer(string toolName)
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
                    ["ToolkitName"] = toolName,
                    ["FunctionNames"] = new[] { "CalculateCurrentRatio", "CalculateQuickRatio", "CalculateWorkingCapital", "CalculateDebtToEquityRatio", "CalculateDebtToAssetsRatio", "ComprehensiveBalanceSheetAnalysis" },
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
                    ["IsCollapse"] = true
                }
            });
    }

    private IEnumerable<AIFunction> CreateToolkitFunctions(string parentToolkit)
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
                    ["ParentToolkit"] = parentToolkit
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
                ["ReferencedToolkits"] = new[] { "FinancialAnalysisToolkit" }
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
                "FinancialAnalysisToolkit.CalculateCurrentRatio",
                "FinancialAnalysisToolkit.CalculateQuickRatio",
                "FinancialAnalysisToolkit.CalculateWorkingCapital"
            },
            "CapitalStructureAnalysis" => new[]
            {
                "FinancialAnalysisToolkit.CalculateDebtToEquityRatio",
                "FinancialAnalysisToolkit.CalculateDebtToAssetsRatio"
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

    private IEnumerable<AIFunction> CreateSkillsWithDocuments(string? parentCollapse, bool withDocuments)
    {
        var skillNames = new[]
        {
            "QuickLiquidityAnalysis",
            "CapitalStructureAnalysis"
        };

        return skillNames.Select(name =>
        {
            var props = new Dictionary<string, object>
            {
                ["IsContainer"] = true,
                ["IsSkill"] = true,
                ["ReferencedFunctions"] = GetReferencedFunctionsForSkill(name),
                ["ReferencedToolkits"] = new[] { "FinancialAnalysisToolkit" }
            };

            if (parentCollapse != null)
            {
                props["ParentContainer"] = parentCollapse;
            }

            // Add documents metadata if requested
            if (withDocuments)
            {
                props["DocumentUploads"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["FilePath"] = $"./Skills/SOPs/{name}-SOP.md",
                        ["DocumentId"] = $"{name.ToLower()}-sop",
                        ["Description"] = $"SOP for {name}"
                    }
                };
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

    #endregion

    #region Collapsed Toolkit/Skill Expansion Tests

    [Fact]
    public void CollapsedToolkit_HidesAfterExpansion()
    {
        // Arrange: Create MathTools with [Collapse], containing functions and skills
        var tools = CreateMathToolsTools();
        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Initially, MathTools container should be visible
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
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
            ExpandedSkillContainers,
            ImmutableHashSet<string>.Empty);

        // Assert: Container hidden, contents visible
        expandedTools.Should().NotContain(t => t.Name == "MathTools");
        expandedTools.Should().Contain(t => t.Name == "Add");
        expandedTools.Should().Contain(t => t.Name == "Multiply");
        expandedTools.Should().Contain(t => t.Name == "SolveQuadratic");
    }

    [Fact]
    public void CollapsedToolkit_ShowsFunctionsAfterExpansion()
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
            ExpandedSkillContainers,
            ImmutableHashSet<string>.Empty);

        // Assert: All AI functions from MathTools should be visible
        visibleTools.Should().Contain(t => t.Name == "Add");
        visibleTools.Should().Contain(t => t.Name == "Multiply");
        visibleTools.Should().Contain(t => t.Name == "Abs");
        visibleTools.Should().Contain(t => t.Name == "Square");
        visibleTools.Should().Contain(t => t.Name == "Subtract");
        visibleTools.Should().Contain(t => t.Name == "Min");
    }

    [Fact]
    public void CollapsedToolkit_ShowsSkillsAfterExpansion_ExpandedSkillContainers()
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
            ExpandedSkillContainers,
            ImmutableHashSet<string>.Empty);

        // Assert: SolveQuadratic skill should be visible when parent is in ExpandedSkillContainers
        visibleTools.Should().Contain(t => t.Name == "SolveQuadratic");
    }

    [Fact]
    public void CollapsedToolkit_ShowsSkillsAfterExpansion_ExpandedSkillsParameter()
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
            ImmutableHashSet<string>.Empty,
            expandedSkills);

        // Assert: SolveQuadratic skill should be visible when parent is in expandedSkills
        visibleTools.Should().Contain(t => t.Name == "SolveQuadratic");
    }

    [Fact]
    public void CollapsedToolkit_OnlyHidesItself_NotOtherContainers()
    {
        // Arrange: Two separate Collapse containers
        var tools = new List<AIFunction>();
        tools.AddRange(CreateMathToolsTools());
        tools.Add(CreateCollapseContainer("OtherToolkit", "Other Toolkit for testing"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand only MathTools
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers,
            ImmutableHashSet<string>.Empty);

        // Assert: MathTools hidden, but OtherToolkit still visible
        visibleTools.Should().NotContain(t => t.Name == "MathTools");
        visibleTools.Should().Contain(t => t.Name == "OtherToolkit");
    }

    [Fact]
    public void SkillContainer_VisibleWhenParentCollapseExpandedInToolkits()
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
            ExpandedSkillContainers,
            ImmutableHashSet<string>.Empty);

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
        tools.Add(CreateToolkitFunction("Add", "MathTools", "Adds two numbers"));
        tools.Add(CreateToolkitFunction("Multiply", "MathTools", "Multiplies two numbers"));
        tools.Add(CreateToolkitFunction("Abs", "MathTools", "Returns absolute value"));
        tools.Add(CreateToolkitFunction("Square", "MathTools", "Squares a number"));
        tools.Add(CreateToolkitFunction("Subtract", "MathTools", "Subtracts b from a"));
        tools.Add(CreateToolkitFunction("Min", "MathTools", "Returns minimum of two numbers"));

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
                    ["IsCollapse"] = true,
                    ["FunctionNames"] = new string[] { },
                    ["FunctionCount"] = 0
                }
            });
    }

    private AIFunction CreateToolkitFunction(string name, string parentToolkit, string description)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => "Result",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["ParentToolkit"] = parentToolkit,
                    ["IsContainer"] = false
                }
            });
    }

    private AIFunction CreateSkillContainer(
        string name,
        string description,
        string parentSkillContainer,
        string[] referencedFunctions,
        string[] referencedToolkits)
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
                    ["ReferencedToolkits"] = referencedToolkits
                }
            });
    }

    private AIFunction CreateSkillWithReferences(
        string name,
        string description,
        string? parentCollapse,
        string[] referencedFunctions,
        string[] referencedToolkits)
    {
        var additionalProps = new Dictionary<string, object>
        {
            ["IsContainer"] = true,
            ["IsSkill"] = true,
            ["ReferencedFunctions"] = referencedFunctions,
            ["ReferencedToolkits"] = referencedToolkits
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

    #region Collapsed Toolkit Referenced by Skill Tests

    [Fact]
    public void CollapsedToolkitReferencedBySkill_HidesToolkitContainer_ShowsOnlySkill()
    {
        // Arrange: Collapsed Toolkit referenced by a skill (NOT explicitly registered)
        var tools = new List<AIFunction>();

        // Add Collapsed Toolkit container
        tools.Add(CreateToolkitContainer("FinancialAnalysisToolkit"));

        // Add Toolkit functions
        tools.AddRange(CreateToolkitFunctions("FinancialAnalysisToolkit"));

        // Add skill that references the Collapsed Toolkit
        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolkit.CalculateCurrentRatio",
                "FinancialAnalysisToolkit.CalculateQuickRatio"
            },
            referencedToolkits: new[] { "FinancialAnalysisToolkit" }));

        // Toolkit is NOT explicitly registered - only implicitly via skill reference
        var explicitToolkits = ImmutableHashSet<string>.Empty;

        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act: No expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should show ONLY the skill container, NOT the Toolkit Collapse container
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisToolkit");

        // Functions should be hidden (skill not expanded yet)
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "CalculateQuickRatio");
    }

    [Fact]
    public void CollapsedToolkitReferencedBySkill_ExpandSkill_ShowsReferencedFunctions()
    {
        // Arrange: Collapsed Toolkit referenced by a skill
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainer("FinancialAnalysisToolkit"));
        tools.AddRange(CreateToolkitFunctions("FinancialAnalysisToolkit"));

        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolkit.CalculateCurrentRatio",
                "FinancialAnalysisToolkit.CalculateQuickRatio"
            },
            referencedToolkits: new[] { "FinancialAnalysisToolkit" }));

        var explicitToolkits = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act: Expand the skill (NOT the Toolkit)
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty, // Toolkit Collapse NOT expanded
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "QuickLiquidityAnalysis")); // Skill expanded

        // Assert: Skill bypass should make referenced functions visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");

        // Skill container should be hidden (it's expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // Toolkit Collapse container should still be hidden (implicitly registered)
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisToolkit");
    }

    [Fact]
    public void CollapsedToolkitReferencedBySkill_OrphanFunctions_StayHidden()
    {
        // Arrange: Collapsed Toolkit with some functions referenced by skill, others are orphans
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainer("FinancialAnalysisToolkit"));
        tools.AddRange(CreateToolkitFunctions("FinancialAnalysisToolkit"));

        // Skill only references 2 functions out of 6
        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolkit.CalculateCurrentRatio",
                "FinancialAnalysisToolkit.CalculateQuickRatio"
            },
            referencedToolkits: new[] { "FinancialAnalysisToolkit" }));

        var explicitToolkits = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act: Expand the skill
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
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
    public void CollapsedToolkitReferencedBySkill_ExpandToolkit_ShowsAllFunctions()
    {
        // Arrange: Collapsed Toolkit referenced by skill
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainer("FinancialAnalysisToolkit"));
        tools.AddRange(CreateToolkitFunctions("FinancialAnalysisToolkit"));

        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisToolkit.CalculateCurrentRatio",
                "FinancialAnalysisToolkit.CalculateQuickRatio"
            },
            referencedToolkits: new[] { "FinancialAnalysisToolkit" }));

        var explicitToolkits = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitToolkits);

        // Act: Expand the Toolkit Collapse (not the skill)
        // This is an edge case - user manually expands the Toolkit even though it was implicitly registered
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisToolkit"), // Toolkit expanded
            ImmutableHashSet<string>.Empty);

        // Assert: ALL Toolkit functions should be visible (Toolkit Collapse expanded)
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateWorkingCapital");
        visibleTools.Should().Contain(t => t.Name == "CalculateDebtToEquityRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateDebtToAssetsRatio");
        visibleTools.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Toolkit container should be hidden (expanded)
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisToolkit");

        // Skill container should still be visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
    }

    #endregion

    #region Regression Tests: IsToolkitContainer Flag (Toolkit Attribute Migration)

    /// <summary>
    /// Regression test for the Toolkit attribute migration.
    /// When a toolkit is marked with [Collapse(Collapsed=true)], the source generator sets
    /// IsToolkitContainer=true (not the legacy IsCollapse flag).
    /// ToolVisibilityManager must recognize both flags to properly hide skills inside collapsed toolkits.
    ///
    /// Bug fix: ToolVisibilityManager.GetContainerType() was only checking IsCollapse flag,
    /// but the new [Collapse] attribute sets IsToolkitContainer flag.
    /// </summary>
    [Fact]
    public void CollapsedToolkit_WithIsToolkitContainerFlag_HidesSkillsUntilExpanded()
    {
        // Arrange: Create a collapsed toolkit using the NEW IsToolkitContainer flag
        // This simulates what the source generator produces for [Collapse("...", Collapsed = true)]
        var tools = new List<AIFunction>();

        // Toolkit container with IsToolkitContainer=true (new flag from [Collapse] attribute)
        tools.Add(CreateToolkitContainerWithNewFlag(
            "MathToolkit",
            "Math Operations. Contains 3 functions: Add, Multiply, SolveQuadratic"));

        // Functions in the toolkit
        tools.Add(CreateToolkitFunction("Add", "MathToolkit", "Adds two numbers"));
        tools.Add(CreateToolkitFunction("Multiply", "MathToolkit", "Multiplies two numbers"));

        // Skill inside the collapsed toolkit (should be hidden initially)
        tools.Add(CreateSkillInsideCollapsedToolkit(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathToolkit"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: No expansions - initial state
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Only the toolkit container should be visible initially
        initialTools.Should().Contain(t => t.Name == "MathToolkit",
            "collapsed toolkit container should be visible");
        initialTools.Should().NotContain(t => t.Name == "Add",
            "functions inside collapsed toolkit should be hidden");
        initialTools.Should().NotContain(t => t.Name == "Multiply",
            "functions inside collapsed toolkit should be hidden");
        initialTools.Should().NotContain(t => t.Name == "SolveQuadratic",
            "REGRESSION: skill inside collapsed toolkit should be hidden until parent is expanded");

        // Verify we only have the container
        initialTools.Should().HaveCount(1, "only the toolkit container should be visible");
    }

    [Fact]
    public void CollapsedToolkit_WithIsToolkitContainerFlag_ShowsSkillsAfterExpansion()
    {
        // Arrange: Create a collapsed toolkit using the NEW IsToolkitContainer flag
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "MathToolkit",
            "Math Operations. Contains 3 functions: Add, Multiply, SolveQuadratic"));

        tools.Add(CreateToolkitFunction("Add", "MathToolkit", "Adds two numbers"));
        tools.Add(CreateToolkitFunction("Multiply", "MathToolkit", "Multiplies two numbers"));

        tools.Add(CreateSkillInsideCollapsedToolkit(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathToolkit"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand the toolkit
        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "MathToolkit"),
            ImmutableHashSet<string>.Empty);

        // Assert: Container hidden, contents visible
        expandedTools.Should().NotContain(t => t.Name == "MathToolkit",
            "expanded toolkit container should be hidden");
        expandedTools.Should().Contain(t => t.Name == "Add",
            "functions should be visible after toolkit expansion");
        expandedTools.Should().Contain(t => t.Name == "Multiply",
            "functions should be visible after toolkit expansion");
        expandedTools.Should().Contain(t => t.Name == "SolveQuadratic",
            "skill should be visible after parent toolkit is expanded");
    }

    [Fact]
    public void CollapsedToolkit_BothFlagsWork_LegacyIsCollapseAndNewIsToolkitContainer()
    {
        // Arrange: Test that both the legacy IsCollapse and new IsToolkitContainer flags work
        var tools = new List<AIFunction>();

        // Legacy flag (IsCollapse=true)
        tools.Add(CreateCollapseContainer("LegacyToolkit", "Legacy toolkit using IsCollapse flag"));
        tools.Add(CreateToolkitFunction("LegacyFunc", "LegacyToolkit", "A legacy function"));

        // New flag (IsToolkitContainer=true)
        tools.Add(CreateToolkitContainerWithNewFlag("NewToolkit", "New toolkit using IsToolkitContainer flag"));
        tools.Add(CreateToolkitFunction("NewFunc", "NewToolkit", "A new function"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: No expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Both containers should be visible, both functions hidden
        visibleTools.Should().Contain(t => t.Name == "LegacyToolkit",
            "legacy collapsed toolkit should be visible");
        visibleTools.Should().Contain(t => t.Name == "NewToolkit",
            "new collapsed toolkit should be visible");
        visibleTools.Should().NotContain(t => t.Name == "LegacyFunc",
            "function in legacy collapsed toolkit should be hidden");
        visibleTools.Should().NotContain(t => t.Name == "NewFunc",
            "function in new collapsed toolkit should be hidden");
        visibleTools.Should().HaveCount(2, "only the two toolkit containers should be visible");
    }

    /// <summary>
    /// Creates a toolkit container using the NEW IsToolkitContainer flag.
    /// This simulates what the source generator produces for [Collapse("...", Collapsed = true)]
    /// </summary>
    private AIFunction CreateToolkitContainerWithNewFlag(string name, string description)
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
                    ["IsToolkitContainer"] = true, // NEW flag from [Collapse] attribute
                    ["FunctionNames"] = new string[] { },
                    ["FunctionCount"] = 0
                }
            });
    }

    /// <summary>
    /// Creates a skill that is inside a collapsed toolkit.
    /// The ParentContainer property indicates the skill belongs to the parent toolkit.
    /// </summary>
    private AIFunction CreateSkillInsideCollapsedToolkit(string name, string description, string parentToolkit)
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
                    ["ParentContainer"] = parentToolkit, // Links skill to parent toolkit
                    ["ReferencedFunctions"] = Array.Empty<string>(),
                    ["ReferencedToolkits"] = new[] { parentToolkit }
                }
            });
    }

    #endregion

    #region NeverCollapse Runtime Config Tests

    /// <summary>
    /// Tests the NeverCollapse runtime config feature.
    /// When a toolkit is in the NeverCollapse list, its functions should be visible directly
    /// even if the toolkit has a container (description provided).
    /// </summary>
    [Fact]
    public void NeverCollapse_ToolkitInList_ShowsFunctionsDirectly()
    {
        // Arrange: Create a collapsed toolkit that would normally hide its functions
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "FileToolkit",
            "File operations for reading and writing files"));

        tools.Add(CreateToolkitFunction("ReadFile", "FileToolkit", "Reads a file"));
        tools.Add(CreateToolkitFunction("WriteFile", "FileToolkit", "Writes a file"));

        // Create manager with FileToolkit in NeverCollapse list
        var neverCollapse = new HashSet<string> { "FileToolkit" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act: Get visible tools without any expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Functions should be visible directly, container should be hidden
        visibleTools.Should().NotContain(t => t.Name == "FileToolkit",
            "container should be hidden when toolkit is in NeverCollapse");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "functions should be visible directly");
        visibleTools.Should().Contain(t => t.Name == "WriteFile",
            "functions should be visible directly");
        visibleTools.Should().HaveCount(2, "only the functions should be visible");
    }

    [Fact]
    public void NeverCollapse_ToolkitNotInList_CollapsesNormally()
    {
        // Arrange: Create a collapsed toolkit
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "DatabaseToolkit",
            "Database operations"));

        tools.Add(CreateToolkitFunction("Query", "DatabaseToolkit", "Executes a query"));
        tools.Add(CreateToolkitFunction("Insert", "DatabaseToolkit", "Inserts a record"));

        // Create manager with a DIFFERENT toolkit in NeverCollapse (not DatabaseToolkit)
        var neverCollapse = new HashSet<string> { "FileToolkit" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act: Get visible tools without any expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally - only container visible
        visibleTools.Should().Contain(t => t.Name == "DatabaseToolkit",
            "container should be visible when toolkit is NOT in NeverCollapse");
        visibleTools.Should().NotContain(t => t.Name == "Query",
            "functions should be hidden behind container");
        visibleTools.Should().NotContain(t => t.Name == "Insert",
            "functions should be hidden behind container");
        visibleTools.Should().HaveCount(1, "only the container should be visible");
    }

    [Fact]
    public void NeverCollapse_MixedToolkits_OnlyAffectsListedToolkits()
    {
        // Arrange: Create two collapsed toolkits
        var tools = new List<AIFunction>();

        // FileToolkit - will be in NeverCollapse
        tools.Add(CreateToolkitContainerWithNewFlag(
            "FileToolkit",
            "File operations"));
        tools.Add(CreateToolkitFunction("ReadFile", "FileToolkit", "Reads a file"));

        // DatabaseToolkit - will NOT be in NeverCollapse
        tools.Add(CreateToolkitContainerWithNewFlag(
            "DatabaseToolkit",
            "Database operations"));
        tools.Add(CreateToolkitFunction("Query", "DatabaseToolkit", "Executes a query"));

        // Only FileToolkit in NeverCollapse
        var neverCollapse = new HashSet<string> { "FileToolkit" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: FileToolkit functions visible, DatabaseToolkit collapsed
        visibleTools.Should().NotContain(t => t.Name == "FileToolkit",
            "FileToolkit container should be hidden (in NeverCollapse)");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "FileToolkit functions should be visible directly");

        visibleTools.Should().Contain(t => t.Name == "DatabaseToolkit",
            "DatabaseToolkit container should be visible (not in NeverCollapse)");
        visibleTools.Should().NotContain(t => t.Name == "Query",
            "DatabaseToolkit functions should be hidden behind container");

        visibleTools.Should().HaveCount(2, "ReadFile + DatabaseToolkit container");
    }

    [Fact]
    public void NeverCollapse_CaseInsensitive_MatchesRegardlessOfCase()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "FileToolkit",  // PascalCase
            "File operations"));
        tools.Add(CreateToolkitFunction("ReadFile", "FileToolkit", "Reads a file"));

        // NeverCollapse with different casing
        var neverCollapse = new HashSet<string> { "filetoolkit" };  // lowercase
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should match case-insensitively
        visibleTools.Should().NotContain(t => t.Name == "FileToolkit",
            "container should be hidden (case-insensitive match)");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "functions should be visible directly");
    }

    [Fact]
    public void NeverCollapse_EmptyList_AllToolkitsCollapseNormally()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "FileToolkit",
            "File operations"));
        tools.Add(CreateToolkitFunction("ReadFile", "FileToolkit", "Reads a file"));

        // Empty NeverCollapse list
        var neverCollapse = new HashSet<string>();
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally
        visibleTools.Should().Contain(t => t.Name == "FileToolkit",
            "container should be visible");
        visibleTools.Should().NotContain(t => t.Name == "ReadFile",
            "functions should be hidden");
    }

    [Fact]
    public void NeverCollapse_NullList_AllToolkitsCollapseNormally()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "FileToolkit",
            "File operations"));
        tools.Add(CreateToolkitFunction("ReadFile", "FileToolkit", "Reads a file"));

        // Null NeverCollapse list (uses constructor overload)
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapseToolkits: null);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally
        visibleTools.Should().Contain(t => t.Name == "FileToolkit",
            "container should be visible");
        visibleTools.Should().NotContain(t => t.Name == "ReadFile",
            "functions should be hidden");
    }

    [Fact]
    public void NeverCollapse_WithSkillsInsideToolkit_ShowsSkillsDirectly()
    {
        // Arrange: Toolkit with both functions and skills
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "MathToolkit",
            "Math operations"));
        tools.Add(CreateToolkitFunction("Add", "MathToolkit", "Adds two numbers"));
        tools.Add(CreateSkillInsideCollapsedToolkit(
            "SolveEquation",
            "Solves equations",
            "MathToolkit"));

        // MathToolkit in NeverCollapse
        var neverCollapse = new HashSet<string> { "MathToolkit" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Both functions and skills should be visible directly
        visibleTools.Should().NotContain(t => t.Name == "MathToolkit",
            "container should be hidden");
        visibleTools.Should().Contain(t => t.Name == "Add",
            "functions should be visible directly");
        visibleTools.Should().Contain(t => t.Name == "SolveEquation",
            "skills should be visible directly");
    }

    #endregion

    #region SubAgent Visibility Tests

    /// <summary>
    /// Regression test: SubAgents should use ParentToolkit metadata, not ToolkitName.
    /// This ensures SubAgents follow the same collapsing rules as Functions and Skills.
    /// </summary>
    [Fact]
    public void SubAgent_UsesParentToolkit_NotToolkitName()
    {
        // Arrange: Create a SubAgent with ParentToolkit metadata (correct)
        var subAgent = CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent",
            "MathToolkit");

        // Assert: Should have ParentToolkit, not ToolkitName
        subAgent.AdditionalProperties.Should().ContainKey("ParentToolkit");
        subAgent.AdditionalProperties.Should().NotContainKey("ToolkitName");
        subAgent.AdditionalProperties?["ParentToolkit"].Should().Be("MathToolkit");
    }

    [Fact]
    public void SubAgent_InsideCollapsedToolkit_HiddenUntilToolkitExpanded()
    {
        // Arrange: Collapsed Toolkit with functions and a SubAgent
        var tools = new List<AIFunction>();

        // Toolkit container (collapsed)
        tools.Add(CreateToolkitContainerWithNewFlag(
            "MathToolkit",
            "Math operations with research capabilities"));

        // Regular functions
        tools.Add(CreateToolkitFunction("Add", "MathToolkit", "Adds two numbers"));
        tools.Add(CreateToolkitFunction("Multiply", "MathToolkit", "Multiplies two numbers"));

        // SubAgent inside the toolkit
        tools.Add(CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent for math problems",
            "MathToolkit"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Initial state (no expansions)
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible, SubAgent hidden
        initialTools.Should().Contain(t => t.Name == "MathToolkit");
        initialTools.Should().NotContain(t => t.Name == "Add");
        initialTools.Should().NotContain(t => t.Name == "ResearchAgent",
            "SubAgent should be hidden when parent toolkit is collapsed");
        initialTools.Should().HaveCount(1);
    }

    [Fact]
    public void SubAgent_InsideCollapsedToolkit_VisibleAfterToolkitExpanded()
    {
        // Arrange: Collapsed Toolkit with SubAgent
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "MathToolkit",
            "Math operations with research capabilities"));

        tools.Add(CreateToolkitFunction("Add", "MathToolkit", "Adds two numbers"));
        tools.Add(CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent",
            "MathToolkit"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand the toolkit
        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "MathToolkit"),
            ImmutableHashSet<string>.Empty);

        // Assert: Container hidden, contents visible (including SubAgent)
        expandedTools.Should().NotContain(t => t.Name == "MathToolkit");
        expandedTools.Should().Contain(t => t.Name == "Add");
        expandedTools.Should().Contain(t => t.Name == "ResearchAgent",
            "SubAgent should be visible when parent toolkit is expanded");
    }

    [Fact]
    public void SubAgent_WithoutParentToolkit_AlwaysVisible()
    {
        // Arrange: SubAgent without ParentToolkit (standalone)
        var tools = new List<AIFunction>();

        // SubAgent without ParentToolkit
        var subAgent = AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => "Result",
            new AIFunctionFactoryOptions
            {
                Name = "StandaloneAgent",
                Description = "Standalone agent not in a toolkit",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsSubAgent"] = true,
                    ["SessionMode"] = "Stateless"
                    // No ParentToolkit!
                }
            });

        tools.Add(subAgent);

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: SubAgent should be visible (no parent to collapse it)
        visibleTools.Should().Contain(t => t.Name == "StandaloneAgent");
    }

    [Fact]
    public void SubAgent_MultipleInSameToolkit_AllHiddenAndShownTogether()
    {
        // Arrange: Multiple SubAgents in the same collapsed toolkit
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "ResearchToolkit",
            "Research toolkit with multiple specialized agents"));

        tools.Add(CreateSubAgentFunction("WebSearchAgent", "Web search specialist", "ResearchToolkit"));
        tools.Add(CreateSubAgentFunction("DataAnalysisAgent", "Data analysis specialist", "ResearchToolkit"));
        tools.Add(CreateSubAgentFunction("SummaryAgent", "Summary specialist", "ResearchToolkit"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Before expansion
        var beforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: All SubAgents hidden
        beforeExpansion.Should().Contain(t => t.Name == "ResearchToolkit");
        beforeExpansion.Should().NotContain(t => t.Name == "WebSearchAgent");
        beforeExpansion.Should().NotContain(t => t.Name == "DataAnalysisAgent");
        beforeExpansion.Should().NotContain(t => t.Name == "SummaryAgent");

        // Act: After expansion
        var afterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ResearchToolkit"),
            ImmutableHashSet<string>.Empty);

        // Assert: All SubAgents visible
        afterExpansion.Should().NotContain(t => t.Name == "ResearchToolkit");
        afterExpansion.Should().Contain(t => t.Name == "WebSearchAgent");
        afterExpansion.Should().Contain(t => t.Name == "DataAnalysisAgent");
        afterExpansion.Should().Contain(t => t.Name == "SummaryAgent");
    }

    [Fact]
    public void SubAgent_MixedWithFunctionsAndSkills_AllFollowSameCollapsingRules()
    {
        // Arrange: Toolkit with functions, skills, AND SubAgents
        var tools = new List<AIFunction>();

        tools.Add(CreateToolkitContainerWithNewFlag(
            "ComprehensiveToolkit",
            "Toolkit with functions, skills, and sub-agents"));

        // Regular function
        tools.Add(CreateToolkitFunction("Calculate", "ComprehensiveToolkit", "Calculation function"));

        // Skill inside toolkit
        tools.Add(CreateSkillInsideCollapsedToolkit(
            "AnalysisSkill",
            "Analysis skill",
            "ComprehensiveToolkit"));

        // SubAgent inside toolkit
        tools.Add(CreateSubAgentFunction(
            "ExpertAgent",
            "Expert agent",
            "ComprehensiveToolkit"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Before expansion
        var beforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible
        beforeExpansion.Should().Contain(t => t.Name == "ComprehensiveToolkit");
        beforeExpansion.Should().NotContain(t => t.Name == "Calculate");
        beforeExpansion.Should().NotContain(t => t.Name == "AnalysisSkill");
        beforeExpansion.Should().NotContain(t => t.Name == "ExpertAgent");
        beforeExpansion.Should().HaveCount(1);

        // Act: After expansion
        var afterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ComprehensiveToolkit"),
            ImmutableHashSet<string>.Empty);

        // Assert: All contents visible (function, skill, SubAgent)
        afterExpansion.Should().NotContain(t => t.Name == "ComprehensiveToolkit");
        afterExpansion.Should().Contain(t => t.Name == "Calculate");
        afterExpansion.Should().Contain(t => t.Name == "AnalysisSkill");
        afterExpansion.Should().Contain(t => t.Name == "ExpertAgent");
    }

    /// <summary>
    /// Creates a SubAgent AIFunction with correct ParentToolkit metadata.
    /// This simulates what the source generator produces after the fix.
    /// </summary>
    private AIFunction CreateSubAgentFunction(
        string name,
        string description,
        string parentToolkit)
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
                    ["SessionMode"] = "Stateless",
                    ["ParentToolkit"] = parentToolkit  //  Correct key (not ToolkitName)
                }
            });
    }

    #endregion
}
