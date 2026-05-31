using Xunit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent.Collapsing;
using System.Collections.Immutable;

namespace HPD.Agent.Tests.Collapsing;

/// <summary>
/// Comprehensive tests for ToolVisibilityManager to validate all Collapsing scenarios.
/// These tests cover explicit/implicit Harness registration, [Collapse] attribute behavior,
/// orphan function hiding, and skill parent Collapse detection.
/// </summary>
public class ToolVisibilityManagerTests
{
    #region Test Scenario 1: Both Harness and Skills with [Collapse], Both Explicit

    [Fact]
    public void Scenario1_BothCollapsed_BothExplicit_ShowsOnlyContainers()
    {
        // Arrange: Harness has [Collapse], Skills have [Collapse], both explicitly registered
        var tools = CreateTestTools(
            HarnessHasCollapse: true,
            skillsHaveCollapse: true,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty, // No expanded Harneses
            ImmutableHashSet<string>.Empty); // No expanded skills

        // Assert
        visibleTools.Should().HaveCount(2); // Only containers, no read_skill_document (no skills expanded)
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisHarness"); // Collapse container
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisSkills"); // Collapse container

        // Should NOT contain individual Harness functions
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should NOT contain individual skills
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // Should NOT contain read_skill_document (no skills with documents expanded)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");
    }

    #endregion

    #region Test Scenario 2: Harness Explicit WITHOUT [Collapse], Skills With [Collapse]

    [Fact]
    public void Scenario2_HarnessNotCollapsed_SkillsCollapsed_ShowsAllHarnessFunctions()
    {
        // Arrange: Harness NO [Collapse] but explicit, Skills have [Collapse]
        var tools = CreateTestTools(
            HarnessHasCollapse: false,
            skillsHaveCollapse: true,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - Should show all Harness functions (explicit, no Collapse)
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

    #region Test Scenario 3: Harness With [Collapse], Skills WITHOUT [Collapse], Both Explicit

    [Fact]
    public void Scenario3_HarnessCollapsed_SkillsNotCollapsed_ShowsIndividualSkills()
    {
        // Arrange: Harness has [Collapse], Skills NO [Collapse], both explicit
        var tools = CreateTestTools(
            HarnessHasCollapse: true,
            skillsHaveCollapse: false,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert
        visibleTools.Should().Contain(t => t.Name == "FinancialAnalysisHarness"); // Collapse container
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis"); // Individual skill
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis"); // Individual skill

        // Should NOT show Harness functions (Collapsed Harness not expanded)
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Should NOT contain read_skill_document (no skills expanded - only containers visible)
        visibleTools.Should().NotContain(t => t.Name == "ReadSkillDocument");

        // Total: 1 Harness container + 5 skills = 6
        visibleTools.Should().HaveCount(6);
    }

    #endregion

    #region Test Scenario 4: Only Skills Registered (No Explicit Harness), Skills WITHOUT [Collapse]

    [Fact]
    public void Scenario4_OnlySkillsExplicit_NoCollapse_HidesOrphanFunctions()
    {
        // Arrange: Only skills registered (Harness auto-registered), skills NO [Collapse]
        var tools = CreateTestTools(
            HarnessHasCollapse: false,
            skillsHaveCollapse: false,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisSkills"); // Only skills explicit
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - Skills visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().Contain(t => t.Name == "CapitalStructureAnalysis");

        // Orphan functions should be hidden (Harness auto-registered, not explicit)
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
            HarnessHasCollapse: false,
            skillsHaveCollapse: true,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

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

        // Harness functions hidden (orphans)
        visibleTools.Should().NotContain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");

        visibleTools.Should().HaveCount(1); // Only Collapse container, no read_skill_document
    }

    #endregion

    #region Test Scenario 6: Collapsed Harness Explicit, No Skills

    [Fact]
    public void Scenario6_CollapsedHarnessExplicit_NoSkills_HidesFunctions()
    {
        // Arrange: Harness has [Collapse], explicit, no skills
        var tools = CreateTestTools(
            HarnessHasCollapse: true,
            skillsHaveCollapse: false,
            includeHarnessFunctions: true,
            includeSkills: false);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisHarness");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act - Not expanded
        var visibleToolsBeforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert - Before expansion
        visibleToolsBeforeExpansion.Should().Contain(t => t.Name == "FinancialAnalysisHarness");
        visibleToolsBeforeExpansion.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleToolsBeforeExpansion.Should().NotContain(t => t.Name == "ReadSkillDocument"); // No skills expanded
        visibleToolsBeforeExpansion.Should().HaveCount(1); // Only Harness container

        // Act - After expansion
        var visibleToolsAfterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisHarness"), // Expanded
            ImmutableHashSet<string>.Empty);

        // Assert - After expansion, all functions visible
        visibleToolsAfterExpansion.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleToolsAfterExpansion.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");
        // Still no read_skill_document (no skills expanded, only Harness expanded)
        visibleToolsAfterExpansion.Should().NotContain(t => t.Name == "ReadSkillDocument");
    }

    #endregion

    #region Test Expansion Behavior

    [Fact]
    public void ExpandSkillCollapse_ShowsIndividualSkills()
    {
        // Arrange
        var tools = CreateTestTools(
            HarnessHasCollapse: true,
            skillsHaveCollapse: true,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

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
            HarnessHasCollapse: true,
            skillsHaveCollapse: false,
            includeHarnessFunctions: true,
            includeSkills: true);
        
        var explicitHarneses = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "FinancialAnalysisHarness",
            "FinancialAnalysisSkills");
        
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act - Expand both the Harness (so functions are available) AND the skill (so it references them)
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisHarness"), // Expand Harness
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "QuickLiquidityAnalysis")); // Expand skill

        // Assert - Functions referenced by QuickLiquidityAnalysis now visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateWorkingCapital");
    }

    #endregion

    #region Helper Methods

    private IEnumerable<AIFunction> CreateTestTools(
        bool HarnessHasCollapse,
        bool skillsHaveCollapse,
        bool includeHarnessFunctions,
        bool includeSkills)
    {
        var tools = new List<AIFunction>();

        // Add Harness container if Collapsed
        if (HarnessHasCollapse)
        {
            tools.Add(CreateHarnessContainer("FinancialAnalysisHarness"));
        }

        // Add skills Collapse container if Collapsed
        if (skillsHaveCollapse)
        {
            tools.Add(CreateCollapseContainer("FinancialAnalysisSkills"));
        }

        // Add Harness functions
        if (includeHarnessFunctions)
        {
            tools.AddRange(CreateHarnessFunctions("FinancialAnalysisHarness"));
        }

        // Add skills
        if (includeSkills)
        {
            tools.AddRange(CreateSkills(skillsHaveCollapse ? "FinancialAnalysisSkills" : null));
        }

        return tools;
    }

    private AIFunction CreateHarnessContainer(string toolName)
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
                    ["HarnessName"] = toolName,
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

    private IEnumerable<AIFunction> CreateHarnessFunctions(string parentHarness)
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
                    ["ParentHarness"] = parentHarness
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
                ["ReferencedHarneses"] = new[] { "FinancialAnalysisHarness" }
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
                "FinancialAnalysisHarness.CalculateCurrentRatio",
                "FinancialAnalysisHarness.CalculateQuickRatio",
                "FinancialAnalysisHarness.CalculateWorkingCapital"
            },
            "CapitalStructureAnalysis" => new[]
            {
                "FinancialAnalysisHarness.CalculateDebtToEquityRatio",
                "FinancialAnalysisHarness.CalculateDebtToAssetsRatio"
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
                ["ReferencedHarneses"] = new[] { "FinancialAnalysisHarness" }
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

    #region Collapsed Harness/Skill Expansion Tests

    [Fact]
    public void CollapsedHarness_HidesAfterExpansion()
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
    public void CollapsedHarness_ShowsFunctionsAfterExpansion()
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
    public void CollapsedHarness_ShowsSkillsAfterExpansion_ExpandedSkillContainers()
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
    public void CollapsedHarness_ShowsSkillsAfterExpansion_ExpandedSkillsParameter()
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
    public void CollapsedHarness_OnlyHidesItself_NotOtherContainers()
    {
        // Arrange: Two separate Collapse containers
        var tools = new List<AIFunction>();
        tools.AddRange(CreateMathToolsTools());
        tools.Add(CreateCollapseContainer("OtherHarness", "Other Harness for testing"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand only MathTools
        var ExpandedSkillContainers = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "MathTools");

        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ExpandedSkillContainers,
            ImmutableHashSet<string>.Empty);

        // Assert: MathTools hidden, but OtherHarness still visible
        visibleTools.Should().NotContain(t => t.Name == "MathTools");
        visibleTools.Should().Contain(t => t.Name == "OtherHarness");
    }

    [Fact]
    public void SkillContainer_VisibleWhenParentCollapseExpandedInHarneses()
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
        tools.Add(CreateHarnessFunction("Add", "MathTools", "Adds two numbers"));
        tools.Add(CreateHarnessFunction("Multiply", "MathTools", "Multiplies two numbers"));
        tools.Add(CreateHarnessFunction("Abs", "MathTools", "Returns absolute value"));
        tools.Add(CreateHarnessFunction("Square", "MathTools", "Squares a number"));
        tools.Add(CreateHarnessFunction("Subtract", "MathTools", "Subtracts b from a"));
        tools.Add(CreateHarnessFunction("Min", "MathTools", "Returns minimum of two numbers"));

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

    private AIFunction CreateHarnessFunction(string name, string parentHarness, string description)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => "Result",
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["ParentHarness"] = parentHarness,
                    ["IsContainer"] = false
                }
            });
    }

    private AIFunction CreateSkillContainer(
        string name,
        string description,
        string parentSkillContainer,
        string[] referencedFunctions,
        string[] referencedHarneses)
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
                    ["ReferencedHarneses"] = referencedHarneses
                }
            });
    }

    private AIFunction CreateSkillWithReferences(
        string name,
        string description,
        string? parentCollapse,
        string[] referencedFunctions,
        string[] referencedHarneses)
    {
        var additionalProps = new Dictionary<string, object>
        {
            ["IsContainer"] = true,
            ["IsSkill"] = true,
            ["ReferencedFunctions"] = referencedFunctions,
            ["ReferencedHarneses"] = referencedHarneses
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

    #region Collapsed Harness Referenced by Skill Tests

    [Fact]
    public void CollapsedHarnessReferencedBySkill_HidesHarnessContainer_ShowsOnlySkill()
    {
        // Arrange: Collapsed Harness referenced by a skill (NOT explicitly registered)
        var tools = new List<AIFunction>();

        // Add Collapsed Harness container
        tools.Add(CreateHarnessContainer("FinancialAnalysisHarness"));

        // Add Harness functions
        tools.AddRange(CreateHarnessFunctions("FinancialAnalysisHarness"));

        // Add skill that references the Collapsed Harness
        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisHarness.CalculateCurrentRatio",
                "FinancialAnalysisHarness.CalculateQuickRatio"
            },
            referencedHarneses: new[] { "FinancialAnalysisHarness" }));

        // Harness is NOT explicitly registered - only implicitly via skill reference
        var explicitHarneses = ImmutableHashSet<string>.Empty;

        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act: No expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should show ONLY the skill container, NOT the Harness Collapse container
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisHarness");

        // Functions should be hidden (skill not expanded yet)
        visibleTools.Should().NotContain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().NotContain(t => t.Name == "CalculateQuickRatio");
    }

    [Fact]
    public void CollapsedHarnessReferencedBySkill_ExpandSkill_ShowsReferencedFunctions()
    {
        // Arrange: Collapsed Harness referenced by a skill
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainer("FinancialAnalysisHarness"));
        tools.AddRange(CreateHarnessFunctions("FinancialAnalysisHarness"));

        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisHarness.CalculateCurrentRatio",
                "FinancialAnalysisHarness.CalculateQuickRatio"
            },
            referencedHarneses: new[] { "FinancialAnalysisHarness" }));

        var explicitHarneses = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act: Expand the skill (NOT the Harness)
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty, // Harness Collapse NOT expanded
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "QuickLiquidityAnalysis")); // Skill expanded

        // Assert: Skill bypass should make referenced functions visible
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");

        // Skill container should be hidden (it's expanded)
        visibleTools.Should().NotContain(t => t.Name == "QuickLiquidityAnalysis");

        // Harness Collapse container should still be hidden (implicitly registered)
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisHarness");
    }

    [Fact]
    public void CollapsedHarnessReferencedBySkill_OrphanFunctions_StayHidden()
    {
        // Arrange: Collapsed Harness with some functions referenced by skill, others are orphans
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainer("FinancialAnalysisHarness"));
        tools.AddRange(CreateHarnessFunctions("FinancialAnalysisHarness"));

        // Skill only references 2 functions out of 6
        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisHarness.CalculateCurrentRatio",
                "FinancialAnalysisHarness.CalculateQuickRatio"
            },
            referencedHarneses: new[] { "FinancialAnalysisHarness" }));

        var explicitHarneses = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

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
    public void CollapsedHarnessReferencedBySkill_ExpandHarness_ShowsAllFunctions()
    {
        // Arrange: Collapsed Harness referenced by skill
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainer("FinancialAnalysisHarness"));
        tools.AddRange(CreateHarnessFunctions("FinancialAnalysisHarness"));

        tools.Add(CreateSkillWithReferences(
            "QuickLiquidityAnalysis",
            "Quick liquidity analysis skill",
            parentCollapse: null,
            referencedFunctions: new[]
            {
                "FinancialAnalysisHarness.CalculateCurrentRatio",
                "FinancialAnalysisHarness.CalculateQuickRatio"
            },
            referencedHarneses: new[] { "FinancialAnalysisHarness" }));

        var explicitHarneses = ImmutableHashSet<string>.Empty;
        var manager = new ToolVisibilityManager(tools, explicitHarneses);

        // Act: Expand the Harness Collapse (not the skill)
        // This is an edge case - user manually expands the Harness even though it was implicitly registered
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "FinancialAnalysisHarness"), // Harness expanded
            ImmutableHashSet<string>.Empty);

        // Assert: ALL Harness functions should be visible (Harness Collapse expanded)
        visibleTools.Should().Contain(t => t.Name == "CalculateCurrentRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateQuickRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateWorkingCapital");
        visibleTools.Should().Contain(t => t.Name == "CalculateDebtToEquityRatio");
        visibleTools.Should().Contain(t => t.Name == "CalculateDebtToAssetsRatio");
        visibleTools.Should().Contain(t => t.Name == "ComprehensiveBalanceSheetAnalysis");

        // Harness container should be hidden (expanded)
        visibleTools.Should().NotContain(t => t.Name == "FinancialAnalysisHarness");

        // Skill container should still be visible
        visibleTools.Should().Contain(t => t.Name == "QuickLiquidityAnalysis");
    }

    #endregion

    #region Regression Tests: IsHarnessContainer Flag (Harness Attribute Migration)

    /// <summary>
    /// Regression test for the Harness attribute migration.
    /// When a harness is marked with [Collapse(Collapsed=true)], the source generator sets
    /// IsHarnessContainer=true (not the legacy IsCollapse flag).
    /// ToolVisibilityManager must recognize both flags to properly hide skills inside collapsed harnesses.
    ///
    /// Bug fix: ToolVisibilityManager.GetContainerType() was only checking IsCollapse flag,
    /// but the new [Collapse] attribute sets IsHarnessContainer flag.
    /// </summary>
    [Fact]
    public void CollapsedHarness_WithIsHarnessContainerFlag_HidesSkillsUntilExpanded()
    {
        // Arrange: Create a collapsed harness using the NEW IsHarnessContainer flag
        // This simulates what the source generator produces for [Collapse("...", Collapsed = true)]
        var tools = new List<AIFunction>();

        // Harness container with IsHarnessContainer=true (new flag from [Collapse] attribute)
        tools.Add(CreateHarnessContainerWithNewFlag(
            "MathHarness",
            "Math Operations. Contains 3 functions: Add, Multiply, SolveQuadratic"));

        // Functions in the harness
        tools.Add(CreateHarnessFunction("Add", "MathHarness", "Adds two numbers"));
        tools.Add(CreateHarnessFunction("Multiply", "MathHarness", "Multiplies two numbers"));

        // Skill inside the collapsed harness (should be hidden initially)
        tools.Add(CreateSkillInsideCollapsedHarness(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: No expansions - initial state
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Only the harness container should be visible initially
        initialTools.Should().Contain(t => t.Name == "MathHarness",
            "collapsed harness container should be visible");
        initialTools.Should().NotContain(t => t.Name == "Add",
            "functions inside collapsed harness should be hidden");
        initialTools.Should().NotContain(t => t.Name == "Multiply",
            "functions inside collapsed harness should be hidden");
        initialTools.Should().NotContain(t => t.Name == "SolveQuadratic",
            "REGRESSION: skill inside collapsed harness should be hidden until parent is expanded");

        // Verify we only have the container
        initialTools.Should().HaveCount(1, "only the harness container should be visible");
    }

    [Fact]
    public void CollapsedHarness_WithIsHarnessContainerFlag_ShowsSkillsAfterExpansion()
    {
        // Arrange: Create a collapsed harness using the NEW IsHarnessContainer flag
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "MathHarness",
            "Math Operations. Contains 3 functions: Add, Multiply, SolveQuadratic"));

        tools.Add(CreateHarnessFunction("Add", "MathHarness", "Adds two numbers"));
        tools.Add(CreateHarnessFunction("Multiply", "MathHarness", "Multiplies two numbers"));

        tools.Add(CreateSkillInsideCollapsedHarness(
            "SolveQuadratic",
            "Solves quadratic equations",
            "MathHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand the harness
        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "MathHarness"),
            ImmutableHashSet<string>.Empty);

        // Assert: Container hidden, contents visible
        expandedTools.Should().NotContain(t => t.Name == "MathHarness",
            "expanded harness container should be hidden");
        expandedTools.Should().Contain(t => t.Name == "Add",
            "functions should be visible after harness expansion");
        expandedTools.Should().Contain(t => t.Name == "Multiply",
            "functions should be visible after harness expansion");
        expandedTools.Should().Contain(t => t.Name == "SolveQuadratic",
            "skill should be visible after parent harness is expanded");
    }

    [Fact]
    public void CollapsedHarness_BothFlagsWork_LegacyIsCollapseAndNewIsHarnessContainer()
    {
        // Arrange: Test that both the legacy IsCollapse and new IsHarnessContainer flags work
        var tools = new List<AIFunction>();

        // Legacy flag (IsCollapse=true)
        tools.Add(CreateCollapseContainer("LegacyHarness", "Legacy harness using IsCollapse flag"));
        tools.Add(CreateHarnessFunction("LegacyFunc", "LegacyHarness", "A legacy function"));

        // New flag (IsHarnessContainer=true)
        tools.Add(CreateHarnessContainerWithNewFlag("NewHarness", "New harness using IsHarnessContainer flag"));
        tools.Add(CreateHarnessFunction("NewFunc", "NewHarness", "A new function"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: No expansions
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Both containers should be visible, both functions hidden
        visibleTools.Should().Contain(t => t.Name == "LegacyHarness",
            "legacy collapsed harness should be visible");
        visibleTools.Should().Contain(t => t.Name == "NewHarness",
            "new collapsed harness should be visible");
        visibleTools.Should().NotContain(t => t.Name == "LegacyFunc",
            "function in legacy collapsed harness should be hidden");
        visibleTools.Should().NotContain(t => t.Name == "NewFunc",
            "function in new collapsed harness should be hidden");
        visibleTools.Should().HaveCount(2, "only the two harness containers should be visible");
    }

    /// <summary>
    /// Creates a harness container using the NEW IsHarnessContainer flag.
    /// This simulates what the source generator produces for [Collapse("...", Collapsed = true)]
    /// </summary>
    private AIFunction CreateHarnessContainerWithNewFlag(string name, string description)
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
                    ["IsHarnessContainer"] = true, // NEW flag from [Collapse] attribute
                    ["FunctionNames"] = new string[] { },
                    ["FunctionCount"] = 0
                }
            });
    }

    /// <summary>
    /// Creates a skill that is inside a collapsed harness.
    /// The ParentContainer property indicates the skill belongs to the parent harness.
    /// </summary>
    private AIFunction CreateSkillInsideCollapsedHarness(string name, string description, string parentHarness)
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
                    ["ParentContainer"] = parentHarness, // Links skill to parent harness
                    ["ReferencedFunctions"] = Array.Empty<string>(),
                    ["ReferencedHarneses"] = new[] { parentHarness }
                }
            });
    }

    #endregion

    #region NeverCollapse Runtime Config Tests

    /// <summary>
    /// Tests the NeverCollapse runtime config feature.
    /// When a harness is in the NeverCollapse list, its functions should be visible directly
    /// even if the harness has a container (description provided).
    /// </summary>
    [Fact]
    public void NeverCollapse_HarnessInList_ShowsFunctionsDirectly()
    {
        // Arrange: Create a collapsed harness that would normally hide its functions
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "FileHarness",
            "File operations for reading and writing files"));

        tools.Add(CreateHarnessFunction("ReadFile", "FileHarness", "Reads a file"));
        tools.Add(CreateHarnessFunction("WriteFile", "FileHarness", "Writes a file"));

        // Create manager with FileHarness in NeverCollapse list
        var neverCollapse = new HashSet<string> { "FileHarness" };
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
        visibleTools.Should().NotContain(t => t.Name == "FileHarness",
            "container should be hidden when harness is in NeverCollapse");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "functions should be visible directly");
        visibleTools.Should().Contain(t => t.Name == "WriteFile",
            "functions should be visible directly");
        visibleTools.Should().HaveCount(2, "only the functions should be visible");
    }

    [Fact]
    public void NeverCollapse_HarnessNotInList_CollapsesNormally()
    {
        // Arrange: Create a collapsed harness
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "DatabaseHarness",
            "Database operations"));

        tools.Add(CreateHarnessFunction("Query", "DatabaseHarness", "Executes a query"));
        tools.Add(CreateHarnessFunction("Insert", "DatabaseHarness", "Inserts a record"));

        // Create manager with a DIFFERENT harness in NeverCollapse (not DatabaseHarness)
        var neverCollapse = new HashSet<string> { "FileHarness" };
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
        visibleTools.Should().Contain(t => t.Name == "DatabaseHarness",
            "container should be visible when harness is NOT in NeverCollapse");
        visibleTools.Should().NotContain(t => t.Name == "Query",
            "functions should be hidden behind container");
        visibleTools.Should().NotContain(t => t.Name == "Insert",
            "functions should be hidden behind container");
        visibleTools.Should().HaveCount(1, "only the container should be visible");
    }

    [Fact]
    public void NeverCollapse_MixedHarneses_OnlyAffectsListedHarneses()
    {
        // Arrange: Create two collapsed harnesses
        var tools = new List<AIFunction>();

        // FileHarness - will be in NeverCollapse
        tools.Add(CreateHarnessContainerWithNewFlag(
            "FileHarness",
            "File operations"));
        tools.Add(CreateHarnessFunction("ReadFile", "FileHarness", "Reads a file"));

        // DatabaseHarness - will NOT be in NeverCollapse
        tools.Add(CreateHarnessContainerWithNewFlag(
            "DatabaseHarness",
            "Database operations"));
        tools.Add(CreateHarnessFunction("Query", "DatabaseHarness", "Executes a query"));

        // Only FileHarness in NeverCollapse
        var neverCollapse = new HashSet<string> { "FileHarness" };
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapse);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: FileHarness functions visible, DatabaseHarness collapsed
        visibleTools.Should().NotContain(t => t.Name == "FileHarness",
            "FileHarness container should be hidden (in NeverCollapse)");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "FileHarness functions should be visible directly");

        visibleTools.Should().Contain(t => t.Name == "DatabaseHarness",
            "DatabaseHarness container should be visible (not in NeverCollapse)");
        visibleTools.Should().NotContain(t => t.Name == "Query",
            "DatabaseHarness functions should be hidden behind container");

        visibleTools.Should().HaveCount(2, "ReadFile + DatabaseHarness container");
    }

    [Fact]
    public void NeverCollapse_CaseInsensitive_MatchesRegardlessOfCase()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "FileHarness",  // PascalCase
            "File operations"));
        tools.Add(CreateHarnessFunction("ReadFile", "FileHarness", "Reads a file"));

        // NeverCollapse with different casing
        var neverCollapse = new HashSet<string> { "fileharness" };  // lowercase
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
        visibleTools.Should().NotContain(t => t.Name == "FileHarness",
            "container should be hidden (case-insensitive match)");
        visibleTools.Should().Contain(t => t.Name == "ReadFile",
            "functions should be visible directly");
    }

    [Fact]
    public void NeverCollapse_EmptyList_AllHarnesesCollapseNormally()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "FileHarness",
            "File operations"));
        tools.Add(CreateHarnessFunction("ReadFile", "FileHarness", "Reads a file"));

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
        visibleTools.Should().Contain(t => t.Name == "FileHarness",
            "container should be visible");
        visibleTools.Should().NotContain(t => t.Name == "ReadFile",
            "functions should be hidden");
    }

    [Fact]
    public void NeverCollapse_NullList_AllHarnesesCollapseNormally()
    {
        // Arrange
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "FileHarness",
            "File operations"));
        tools.Add(CreateHarnessFunction("ReadFile", "FileHarness", "Reads a file"));

        // Null NeverCollapse list (uses constructor overload)
        var manager = new ToolVisibilityManager(
            tools,
            ImmutableHashSet<string>.Empty,
            neverCollapseHarneses: null);

        // Act
        var visibleTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Should collapse normally
        visibleTools.Should().Contain(t => t.Name == "FileHarness",
            "container should be visible");
        visibleTools.Should().NotContain(t => t.Name == "ReadFile",
            "functions should be hidden");
    }

    [Fact]
    public void NeverCollapse_WithSkillsInsideHarness_ShowsSkillsDirectly()
    {
        // Arrange: Harness with both functions and skills
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "MathHarness",
            "Math operations"));
        tools.Add(CreateHarnessFunction("Add", "MathHarness", "Adds two numbers"));
        tools.Add(CreateSkillInsideCollapsedHarness(
            "SolveEquation",
            "Solves equations",
            "MathHarness"));

        // MathHarness in NeverCollapse
        var neverCollapse = new HashSet<string> { "MathHarness" };
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
        visibleTools.Should().NotContain(t => t.Name == "MathHarness",
            "container should be hidden");
        visibleTools.Should().Contain(t => t.Name == "Add",
            "functions should be visible directly");
        visibleTools.Should().Contain(t => t.Name == "SolveEquation",
            "skills should be visible directly");
    }

    #endregion

    #region SubAgent Visibility Tests

    /// <summary>
    /// Regression test: SubAgents should use ParentHarness metadata, not HarnessName.
    /// This ensures SubAgents follow the same collapsing rules as Functions and Skills.
    /// </summary>
    [Fact]
    public void SubAgent_UsesParentHarness_NotHarnessName()
    {
        // Arrange: Create a SubAgent with ParentHarness metadata (correct)
        var subAgent = CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent",
            "MathHarness");

        // Assert: Should have ParentHarness, not HarnessName
        subAgent.AdditionalProperties.Should().ContainKey("ParentHarness");
        subAgent.AdditionalProperties.Should().NotContainKey("HarnessName");
        subAgent.AdditionalProperties?["ParentHarness"].Should().Be("MathHarness");
    }

    [Fact]
    public void SubAgent_InsideCollapsedHarness_HiddenUntilHarnessExpanded()
    {
        // Arrange: Collapsed Harness with functions and a SubAgent
        var tools = new List<AIFunction>();

        // Harness container (collapsed)
        tools.Add(CreateHarnessContainerWithNewFlag(
            "MathHarness",
            "Math operations with research capabilities"));

        // Regular functions
        tools.Add(CreateHarnessFunction("Add", "MathHarness", "Adds two numbers"));
        tools.Add(CreateHarnessFunction("Multiply", "MathHarness", "Multiplies two numbers"));

        // SubAgent inside the harness
        tools.Add(CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent for math problems",
            "MathHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Initial state (no expansions)
        var initialTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible, SubAgent hidden
        initialTools.Should().Contain(t => t.Name == "MathHarness");
        initialTools.Should().NotContain(t => t.Name == "Add");
        initialTools.Should().NotContain(t => t.Name == "ResearchAgent",
            "SubAgent should be hidden when parent harness is collapsed");
        initialTools.Should().HaveCount(1);
    }

    [Fact]
    public void SubAgent_InsideCollapsedHarness_VisibleAfterHarnessExpanded()
    {
        // Arrange: Collapsed Harness with SubAgent
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "MathHarness",
            "Math operations with research capabilities"));

        tools.Add(CreateHarnessFunction("Add", "MathHarness", "Adds two numbers"));
        tools.Add(CreateSubAgentFunction(
            "ResearchAgent",
            "Specialized research agent",
            "MathHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Expand the harness
        var expandedTools = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "MathHarness"),
            ImmutableHashSet<string>.Empty);

        // Assert: Container hidden, contents visible (including SubAgent)
        expandedTools.Should().NotContain(t => t.Name == "MathHarness");
        expandedTools.Should().Contain(t => t.Name == "Add");
        expandedTools.Should().Contain(t => t.Name == "ResearchAgent",
            "SubAgent should be visible when parent harness is expanded");
    }

    [Fact]
    public void SubAgent_WithoutParentHarness_AlwaysVisible()
    {
        // Arrange: SubAgent without ParentHarness (standalone)
        var tools = new List<AIFunction>();

        // SubAgent without ParentHarness
        var subAgent = AIFunctionFactory.Create(
            async (AIFunctionArguments args, CancellationToken ct) => "Result",
            new AIFunctionFactoryOptions
            {
                Name = "StandaloneAgent",
                Description = "Standalone agent not in a harness",
                AdditionalProperties = new Dictionary<string, object>
                {
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "BranchNative"
                    // No ParentHarness!
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
    public void SubAgent_MultipleInSameHarness_AllHiddenAndShownTogether()
    {
        // Arrange: Multiple SubAgents in the same collapsed harness
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "ResearchHarness",
            "Research harness with multiple specialized agents"));

        tools.Add(CreateSubAgentFunction("WebSearchAgent", "Web search specialist", "ResearchHarness"));
        tools.Add(CreateSubAgentFunction("DataAnalysisAgent", "Data analysis specialist", "ResearchHarness"));
        tools.Add(CreateSubAgentFunction("SummaryAgent", "Summary specialist", "ResearchHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Before expansion
        var beforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: All SubAgents hidden
        beforeExpansion.Should().Contain(t => t.Name == "ResearchHarness");
        beforeExpansion.Should().NotContain(t => t.Name == "WebSearchAgent");
        beforeExpansion.Should().NotContain(t => t.Name == "DataAnalysisAgent");
        beforeExpansion.Should().NotContain(t => t.Name == "SummaryAgent");

        // Act: After expansion
        var afterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ResearchHarness"),
            ImmutableHashSet<string>.Empty);

        // Assert: All SubAgents visible
        afterExpansion.Should().NotContain(t => t.Name == "ResearchHarness");
        afterExpansion.Should().Contain(t => t.Name == "WebSearchAgent");
        afterExpansion.Should().Contain(t => t.Name == "DataAnalysisAgent");
        afterExpansion.Should().Contain(t => t.Name == "SummaryAgent");
    }

    [Fact]
    public void SubAgent_MixedWithFunctionsAndSkills_AllFollowSameCollapsingRules()
    {
        // Arrange: Harness with functions, skills, AND SubAgents
        var tools = new List<AIFunction>();

        tools.Add(CreateHarnessContainerWithNewFlag(
            "ComprehensiveHarness",
            "Harness with functions, skills, and sub-agents"));

        // Regular function
        tools.Add(CreateHarnessFunction("Calculate", "ComprehensiveHarness", "Calculation function"));

        // Skill inside harness
        tools.Add(CreateSkillInsideCollapsedHarness(
            "AnalysisSkill",
            "Analysis skill",
            "ComprehensiveHarness"));

        // SubAgent inside harness
        tools.Add(CreateSubAgentFunction(
            "ExpertAgent",
            "Expert agent",
            "ComprehensiveHarness"));

        var manager = new ToolVisibilityManager(tools, ImmutableHashSet<string>.Empty);

        // Act: Before expansion
        var beforeExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet<string>.Empty);

        // Assert: Only container visible
        beforeExpansion.Should().Contain(t => t.Name == "ComprehensiveHarness");
        beforeExpansion.Should().NotContain(t => t.Name == "Calculate");
        beforeExpansion.Should().NotContain(t => t.Name == "AnalysisSkill");
        beforeExpansion.Should().NotContain(t => t.Name == "ExpertAgent");
        beforeExpansion.Should().HaveCount(1);

        // Act: After expansion
        var afterExpansion = manager.GetToolsForAgentTurn(
            tools.ToList(),
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "ComprehensiveHarness"),
            ImmutableHashSet<string>.Empty);

        // Assert: All contents visible (function, skill, SubAgent)
        afterExpansion.Should().NotContain(t => t.Name == "ComprehensiveHarness");
        afterExpansion.Should().Contain(t => t.Name == "Calculate");
        afterExpansion.Should().Contain(t => t.Name == "AnalysisSkill");
        afterExpansion.Should().Contain(t => t.Name == "ExpertAgent");
    }

    /// <summary>
    /// Creates a SubAgent AIFunction with correct ParentHarness metadata.
    /// This simulates what the source generator produces after the fix.
    /// </summary>
    private AIFunction CreateSubAgentFunction(
        string name,
        string description,
        string parentHarness)
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
                    ["ExecutionModel"] = "BranchNative",
                    ["ParentHarness"] = parentHarness  //  Correct key (not HarnessName)
                }
            });
    }

    #endregion
}
