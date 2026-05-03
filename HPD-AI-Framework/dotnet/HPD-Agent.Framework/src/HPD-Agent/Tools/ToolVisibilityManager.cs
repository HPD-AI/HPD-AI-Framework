using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace HPD.Agent.Collapsing;

public class ToolVisibilityManager
{
    private readonly ILogger<ToolVisibilityManager>? _logger;
    private readonly Dictionary<string, AIFunction> _allFunctionsByReference;
    private readonly ImmutableHashSet<string> _explicitlyRegisteredHarneses;
    private readonly ImmutableHashSet<string> _neverCollapseHarneses;

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ILogger<ToolVisibilityManager>? logger = null)
        : this(allFunctions, ImmutableHashSet<string>.Empty, null, logger)
    {
    }

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ImmutableHashSet<string> explicitlyRegisteredHarneses,
        ILogger<ToolVisibilityManager>? logger = null)
        : this(allFunctions, explicitlyRegisteredHarneses, null, logger)
    {
    }

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ImmutableHashSet<string> explicitlyRegisteredHarneses,
        HashSet<string>? neverCollapseHarneses,
        ILogger<ToolVisibilityManager>? logger = null)
    {
        _logger = logger;
        _explicitlyRegisteredHarneses = explicitlyRegisteredHarneses ?? ImmutableHashSet<string>.Empty;
        _neverCollapseHarneses = neverCollapseHarneses != null
            ? ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, neverCollapseHarneses)
            : ImmutableHashSet<string>.Empty;
        _allFunctionsByReference = BuildFunctionLookup(allFunctions);
    }

    /// <summary>
    /// Gets tools visible for the current agent turn based on expansion state.
    /// Handles Harness containers and type-safe Skill containers.
    ///
    /// Ordering strategy:
    /// 1. Collapse containers (skill class containers with [Collapse])
    /// 2. Harness containers (Collapse Harneses with [Collapse])
    /// 3. Skill containers (type-safe Skills with IsContainer=true)
    /// 4. Non-Collapsed functions (always visible)
    /// 5. Expanded Harness functions
    /// 6. Expanded skill functions
    ///
    /// Key insight: Functions in Harneses that are ONLY referenced by Collapsed skills
    /// are hidden until their parent skill is expanded. This prevents "orphan" functions
    /// from appearing when the skill class Collapse is not expanded.
    /// </summary>
    /// <param name="allTools">All available tools</param>
    /// <param name="expandedContainers">Unified set of expanded containers (both Harneses and skills)</param>
    public List<AIFunction> GetToolsForAgentTurn(
        List<AIFunction> allTools,
        ImmutableHashSet<string> expandedContainers)
    {
        // Use the same set for both Harneses and skills (unified container tracking)
        return GetToolsForAgentTurn(allTools, expandedContainers, expandedContainers);
    }

    /// <summary>
    /// Gets tools visible for the current agent turn based on expansion state.
    /// Handles Harness containers and type-safe Skill containers.
    ///
    /// Ordering strategy:
    /// 1. Collapse containers (skill class containers with [Collapse])
    /// 2. Harness containers (Collapse Harneses with [Collapse])
    /// 3. Skill containers (type-safe Skills with IsContainer=true)
    /// 4. Non-Collapsed functions (always visible)
    /// 5. Expanded Harness functions
    /// 6. Expanded skill functions
    ///
    /// Key insight: Functions in Harneses that are ONLY referenced by Collapsed skills
    /// are hidden until their parent skill is expanded. This prevents "orphan" functions
    /// from appearing when the skill class Collapse is not expanded.
    /// </summary>
    /// <param name="allTools">All available tools</param>
    /// <param name="expandedCollapsedHarnessContainers">Set of expanded Harness containers</param>
    /// <param name="expandedSkillContainers">Set of expanded skill containers</param>
    /// <remarks>
    /// This overload is maintained for backward compatibility. Prefer using the single-parameter
    /// overload with unified ExpandedContainers.
    /// </remarks>
    public List<AIFunction> GetToolsForAgentTurn(
        List<AIFunction> allTools,
        ImmutableHashSet<string> expandedCollapsedHarnessContainers,
        ImmutableHashSet<string> expandedSkillContainers)
    {
        //  Build context (first pass - identify relationships)
        var context = BuildVisibilityContext(allTools, expandedCollapsedHarnessContainers, expandedSkillContainers);

        var CollapseContainers = new List<AIFunction>();
        var skillContainers = new List<AIFunction>();
        var nonCollapsedFunctions = new List<AIFunction>();
        var expandedHarnessFunctions = new List<AIFunction>();
        var expandedSkillFunctions = new List<AIFunction>();

        // Phase 2: Categorize tools using visibility rules (second pass)
        foreach (var tool in allTools)
        {
            var containerType = GetContainerType(tool);

            switch (containerType)
            {
                case ContainerType.CollapseAttributeContainer:
                case ContainerType.CollapsedHarnessContainer:
                    // Both types are Collapse/Harness containers - treat identically
                    if (IsCollapseContainerVisible(tool, context))
                    {
                        CollapseContainers.Add(tool);
                    }
                    break;

                case ContainerType.SkillMethodContainer:
                    if (IsSkillContainerVisible(tool, context))
                    {
                        skillContainers.Add(tool);
                    }
                    break;

                case ContainerType.NotAContainer:
                    // Check if this is a container that was classified as NotAContainer
                    // due to NeverCollapse - if so, skip it (hide the container)
                    if (IsContainer(tool))
                    {
                        // Container in NeverCollapse list - hide the container itself
                        // (Functions will be shown directly)
                        break;
                    }

                    // Not a container - check if it's a skill or regular function
                    if (IsSkill(tool))
                    {
                        if (IsSkillVisible(tool, context))
                        {
                            expandedSkillFunctions.Add(tool);
                        }
                    }
                    else
                    {
                        // Regular function - categorize by visibility reason
                        var visibility = GetFunctionVisibility(tool, context);

                        switch (visibility)
                        {
                            case FunctionVisibility.NonCollapsed:
                                nonCollapsedFunctions.Add(tool);
                                break;

                            case FunctionVisibility.ExpandedHarness:
                                expandedHarnessFunctions.Add(tool);
                                break;

                            case FunctionVisibility.ExpandedSkill:
                                expandedSkillFunctions.Add(tool);
                                break;

                            case FunctionVisibility.Hidden:
                                // Not visible this turn
                                break;
                        }
                    }
                    break;
            }
        }

        // Phase 3: Combine in priority order and deduplicate
        // Order: Collapse containers -> skill containers -> non-Collapsed -> expanded functions
        var result = CollapseContainers.OrderBy(c => c.Name)
            .Concat(skillContainers.OrderBy(c => c.Name))
            .Concat(nonCollapsedFunctions.OrderBy(f => f.Name))
            .Concat(expandedHarnessFunctions.OrderBy(f => f.Name))
            .Concat(expandedSkillFunctions.OrderBy(f => f.Name))
            .DistinctBy(f => f.Name)
            .ToList();

        return result;
    }

    /// <summary>
    /// Builds function lookup by reference identifier.
    /// </summary>
    private Dictionary<string, AIFunction> BuildFunctionLookup(IEnumerable<AIFunction> functions)
    {
        var lookup = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);

        foreach (var function in functions)
        {
            if (string.IsNullOrEmpty(function.Name))
                continue;

            // Add by function name
            lookup[function.Name] = function;

            // Add by qualified name if parent Harness exists
            var parentHarness = GetParentHarness(function);
            if (!string.IsNullOrEmpty(parentHarness))
            {
                var qualifiedName = $"{parentHarness}.{function.Name}";
                lookup[qualifiedName] = function;
            }
        }

        return lookup;
    }

    // Helper methods for metadata extraction

    private bool IsContainer(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("IsContainer", out var v) == true && v is bool b && b;

    private bool IsSkill(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("IsSkill", out var v) == true && v is bool b && b &&
        !IsContainer(function);

    private string GetHarnessName(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("HarnessName", out var v) == true && v is string s ? s : function.Name ?? string.Empty;

    private string GetSkillName(AIFunction function) =>
        function.Name ?? string.Empty;

    private string? GetParentHarness(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("ParentHarness", out var v) == true && v is string s ? s : null;

    private string? GetParentContainer(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("ParentContainer", out var v) == true && v is string s ? s : null;

    private string[] GetReferencedFunctions(AIFunction skillContainer)
    {
        if (skillContainer.AdditionalProperties?.TryGetValue("ReferencedFunctions", out var v) == true && v is string[] refs)
            return refs;
        return Array.Empty<string>();
    }

    private string[] GetReferencedTools(AIFunction skillContainer)
    {
        if (skillContainer.AdditionalProperties?.TryGetValue("ReferencedHarneses", out var v) == true && v is string[] Harneses)
            return Harneses;
        return Array.Empty<string>();
    }

    private string ExtractFunctionName(string reference)
    {
        // "HarnessName.FunctionName" -> "FunctionName"
        var lastDot = reference.LastIndexOf('.');
        return lastDot >= 0 ? reference.Substring(lastDot + 1) : reference;
    }

    /// <summary>
    /// Determines the type of container based on metadata flags.
    /// This is the single source of truth for container type classification.
    /// </summary>
    private ContainerType GetContainerType(AIFunction function)
    {
        if (!IsContainer(function))
            return ContainerType.NotAContainer;

        // Check if this harness is in the NeverCollapse list (runtime override)
        // If so, treat it as not a container - functions will be visible directly
        if (_neverCollapseHarneses.Contains(function.Name))
            return ContainerType.NotAContainer;

        // Check for IsHarnessContainer flag (from [Collapse] attribute) or IsCollapse flag (legacy compatibility)
        if ((function.AdditionalProperties?.TryGetValue("IsHarnessContainer", out var harnessVal) == true &&
            harnessVal is bool harnessFlag && harnessFlag) ||
            (function.AdditionalProperties?.TryGetValue("IsCollapse", out var CollapseVal) == true &&
            CollapseVal is bool CollapseFlag && CollapseFlag))
        {
            return ContainerType.CollapseAttributeContainer;
        }

        // Check for IsSkill flag ([Skill] method container)
        if (function.AdditionalProperties?.TryGetValue("IsSkill", out var skillVal) == true &&
            skillVal is bool skillFlag && skillFlag)
        {
            return ContainerType.SkillMethodContainer;
        }

        // Container with no special flags = legacy collapsed harness
        return ContainerType.CollapsedHarnessContainer;
    }

    // ============================================
    // VISIBILITY RULES (Formal Model Implementation)
    // ============================================

    /// <summary>
    /// Container types in HPD-Agent, distinguished by metadata flags.
    /// </summary>
    private enum ContainerType
    {
        /// <summary>Not a container at all.</summary>
        NotAContainer,

        /// <summary>
        /// Container created by [Collapse] attribute on skill class WITH skills.
        /// Example: [Collapse("Analysis")] on FinancialAnalysisSkills class that has [Skill] methods
        /// Metadata: IsCollapse=true, IsContainer=true
        /// Generated by SkillCodeGenerator.GenerateCollapseContainer()
        /// </summary>
        CollapseAttributeContainer,

        /// <summary>
        /// Container for a Collapsed Harness WITHOUT skills (Harness-level Collapsing only).
        /// Example: [Collapse("Math")] on MathHarness class with only [AIFunction] methods
        /// Metadata: IsContainer=true, no IsSkill/IsCollapse flags
        /// Generated by HPDHARNESSourceGenerator.GenerateHarnessContainer()
        /// Note: Both CollapseAttributeContainer and CollapsedHarnessContainer are treated identically at runtime.
        /// </summary>
        CollapsedHarnessContainer,

        /// <summary>
        /// Container created by [Skill] method.
        /// Example: [Skill] public Skill QuickAnalysis()
        /// Metadata: IsSkill=true, IsContainer=true
        /// </summary>
        SkillMethodContainer
    }

    /// <summary>
    /// Indicates why a function is visible (or if it's hidden).
    /// Maps directly to the categorization lists in GetToolsForAgentTurn.
    /// </summary>
    private enum FunctionVisibility
    {
        Hidden,           // Not visible
        NonCollapsed,        // Always visible (goes into nonCollapsedFunctions list)
        ExpandedHarness,   // Visible because parent Harness expanded (goes into expandedHarnessFunctions list)
        ExpandedSkill     // Visible because skill expanded (goes into expandedSkillFunctions list)
    }

    /// <summary>
    /// Rule: Collapse container is visible IFF:
    /// 1. It is NOT expanded, AND
    /// 2. It is NOT implicitly registered via skills (unless explicitly registered)
    /// Collapse containers can be tracked in either expandedCollapsedHarnessContainers or ExpandedSkillContainers.
    /// </summary>
    private bool IsCollapseContainerVisible(AIFunction container, VisibilityContext context)
    {
        // For CollapsedHarnessContainer, use HarnessName. For CollapseAttributeContainer, use Name.
        // Both should work with the same string since they represent the same Collapse.
        var CollapseName = GetHarnessName(container);
        if (string.IsNullOrEmpty(CollapseName))
        {
            CollapseName = container.Name ?? string.Empty;
        }

        // Hide Collapse containers for Harneses that were ONLY implicitly registered via skills
        // (i.e., referenced by skills but NOT explicitly registered by the user)
        if (context.HarnesesWithCollapsedSkills.Contains(CollapseName) &&
            !_explicitlyRegisteredHarneses.Contains(CollapseName))
        {
            _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (implicitly registered via skills)");
            return false;
        }

        // Hide if parent container exists but is not yet expanded
        // This enables nested containers (e.g., MCP_wolfram inside SearchHarness)
        var parentContainerName = GetParentContainer(container);
        if (!string.IsNullOrEmpty(parentContainerName))
        {
            if (!context.ExpandedCollapsedHarnessContainers.Contains(parentContainerName) &&
                !context.ExpandedSkillContainers.Contains(parentContainerName))
            {
                _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (parent {parentContainerName} not expanded)");
                return false;
            }
        }

        // Hide if expanded (in either set)
        if (context.ExpandedCollapsedHarnessContainers.Contains(CollapseName) ||
            context.ExpandedSkillContainers.Contains(CollapseName))
        {
            _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (expanded)");
            return false;
        }

        _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: VISIBLE (not expanded)");
        return true;
    }

    /// <summary>
    /// Rule: Skill container is visible IFF:
    /// 1. It is NOT expanded, AND
    /// 2. Parent Collapse is expanded (if it has a parent Collapse)
    /// </summary>
    private bool IsSkillContainerVisible(AIFunction container, VisibilityContext context)
    {
        var skillName = GetSkillName(container);

        // Check if skill itself is expanded
        if (context.ExpandedSkillContainers.Contains(skillName))
        {
            _logger?.LogDebug($"[VISIBILITY] Skill container {skillName}: HIDDEN (skill expanded)");
            return false;
        }

        var parentCollapse = GetParentContainer(container);

        // Case 1: No parent Collapse - treat like regular skill
        if (string.IsNullOrEmpty(parentCollapse))
        {
            _logger?.LogDebug($"[VISIBILITY] Skill container {skillName}: VISIBLE (no parent Collapse)");
            return true;
        }

        // Case 2: Parent Collapse doesn't exist - treat like standalone skill
        if (!context.SkillClassesWithCollapse.Contains(parentCollapse))
        {
            _logger?.LogDebug($"[VISIBILITY] Skill container {skillName}: VISIBLE (parent Collapse {parentCollapse} doesn't exist)");
            return true;
        }

        // Case 3: Parent Collapse exists - must be expanded
        if (context.ExpandedSkillContainers.Contains(parentCollapse) ||
            context.ExpandedCollapsedHarnessContainers.Contains(parentCollapse))
        {
            _logger?.LogDebug($"[VISIBILITY] Skill container {skillName}: VISIBLE (parent Collapse {parentCollapse} expanded)");
            return true;
        }

        // Otherwise: parent Collapse not expanded - hide
        _logger?.LogDebug($"[VISIBILITY] Skill container {skillName}: HIDDEN (parent Collapse {parentCollapse} not expanded)");
        return false;
    }

    /// <summary>
    /// Rule: Type-safe Skill is visible IFF:
    /// Parent skill container is expanded (or no parent)
    /// </summary>
    private bool IsSkillVisible(AIFunction skill, VisibilityContext context)
    {
        var parentContainer = GetParentContainer(skill);

        if (string.IsNullOrEmpty(parentContainer))
        {
            _logger?.LogDebug($"[VISIBILITY] Skill {skill.Name}: VISIBLE (no parent container)");
            return true;
        }

        if (context.ExpandedSkillContainers.Contains(parentContainer))
        {
            _logger?.LogDebug($"[VISIBILITY] Skill {skill.Name}: VISIBLE (parent container {parentContainer} expanded)");
            return true;
        }

        _logger?.LogDebug($"[VISIBILITY] Skill {skill.Name}: HIDDEN (parent container {parentContainer} not expanded)");
        return false;
    }

    /// <summary>
    /// Determines visibility and categorization for a function.
    /// Returns the visibility reason, which determines which list the function goes into.
    ///
    /// PRIORITY 1: Parent Harness Collapse check with skill bypass
    ///   - If Harness has [Collapse] container AND function is referenced by an expanded skill → VISIBLE (skill bypass)
    ///   - If Harness has [Collapse] container AND parent Harness is expanded → VISIBLE
    ///   - If Harness has [Collapse] container AND not expanded → HIDDEN
    /// PRIORITY 2: Explicit registration check (always show if explicitly registered)
    /// PRIORITY 3: Skill reference check (show if any referencing skill is expanded)
    /// PRIORITY 4: Orphan check (hide functions in implicitly-registered Harneses that aren't referenced)
    /// DEFAULT: Non-Collapsed, non-referenced, non-orphan functions are always visible
    /// </summary>
    private FunctionVisibility GetFunctionVisibility(AIFunction function, VisibilityContext context)
    {
        var functionName = function.Name ?? string.Empty;
        var parentHarness = GetParentHarness(function);

        // PRIORITY 0: If Harness is in NeverCollapse, treat as non-collapsed
        if (parentHarness != null && _neverCollapseHarneses.Contains(parentHarness))
        {
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (parent {parentHarness} in NeverCollapse)");
            return FunctionVisibility.NonCollapsed;
        }

        // PRIORITY 1: If Harness has [Collapse] container, check skill bypass first
        if (parentHarness != null && context.HarnesesWithContainers.Contains(parentHarness))
        {
            // Check if this function is referenced by an expanded skill (skill bypass for Collapsed Harneses)
            if (context.FunctionsReferencedBySkills.Contains(functionName))
            {
                var referencingSkills = context.SkillsReferencingFunction.GetValueOrDefault(functionName, new List<string>());
                bool anySkillExpanded = referencingSkills.Any(s => context.ExpandedSkillContainers.Contains(s));

                if (anySkillExpanded)
                {
                    _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (skill bypass for Collapsed Harness {parentHarness})");
                    return FunctionVisibility.ExpandedSkill;
                }
            }

            // Otherwise, Collapsed Harness function - only show if parent expanded
            if (context.ExpandedCollapsedHarnessContainers.Contains(parentHarness))
            {
                _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (Collapsed parent {parentHarness} expanded)");
                return FunctionVisibility.ExpandedHarness;
            }

            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (Collapsed parent {parentHarness} not expanded)");
            return FunctionVisibility.Hidden;
        }

        // PRIORITY 2: If Harness is explicitly registered (and NOT Collapsed), show all its functions
        // (Explicit registration takes precedence over skill references)
        if (parentHarness != null && _explicitlyRegisteredHarneses.Contains(parentHarness))
        {
            // Explicitly registered Harness - always show functions
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (explicitly registered Harness)");
            return FunctionVisibility.NonCollapsed;
        }

        // PRIORITY 3: Check if this function is referenced by any skills
        if (context.FunctionsReferencedBySkills.Contains(functionName))
        {
            // Function is referenced by skill(s)
            // Only show if at least one referencing skill is expanded
            var referencingSkills = context.SkillsReferencingFunction.GetValueOrDefault(functionName, new List<string>());
            bool anySkillExpanded = referencingSkills.Any(s => context.ExpandedSkillContainers.Contains(s));

            if (anySkillExpanded)
            {
                _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (referenced by expanded skill)");
                return FunctionVisibility.ExpandedSkill;
            }

            // If no skills expanded, function is hidden
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (referenced by skills, none expanded)");
            return FunctionVisibility.Hidden;
        }

        // PRIORITY 4: Orphan check
        if (parentHarness != null && context.HarnesesWithCollapsedSkills.Contains(parentHarness))
        {
            // This function is in a Harness that was auto-registered via Collapsed skills
            // BUT this function is NOT referenced by any skill (it's an orphan)
            // Hide it - don't add to any list
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (orphan in implicitly-registered Harness {parentHarness})");
            return FunctionVisibility.Hidden;
        }

        // DEFAULT: Non-Collapsed function - always visible
        _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (non-Collapsed, default)");
        return FunctionVisibility.NonCollapsed;
    }

    /// <summary>
    /// Builds the visibility context by analyzing all tools and computing relationships.
    /// This is the first pass that identifies Collapsed items and parent-child relationships.
    /// </summary>
    private VisibilityContext BuildVisibilityContext(
        List<AIFunction> allTools,
        ImmutableHashSet<string> expandedCollapsedHarnessContainers,
        ImmutableHashSet<string> expandedSkillContainers)
    {
        var context = new VisibilityContext
        {
            AllTools = allTools,
            ExpandedCollapsedHarnessContainers = expandedCollapsedHarnessContainers,
            ExpandedSkillContainers = expandedSkillContainers,
            HarnesesWithContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SkillClassesWithCollapse = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            FunctionsReferencedBySkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SkillsReferencingFunction = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            HarnesesWithCollapsedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var tool in allTools)
        {
            var containerType = GetContainerType(tool);

            switch (containerType)
            {
                case ContainerType.CollapseAttributeContainer:
                    // Collapse container (can be class-level or Harness-level)
                    var CollapseName = tool.Name ?? string.Empty;
                    context.SkillClassesWithCollapse.Add(CollapseName);
                    // Also track as a Harness with container so functions get hidden/shown properly
                    context.HarnesesWithContainers.Add(CollapseName);
                    break;

                case ContainerType.CollapsedHarnessContainer:
                    var toolName = GetHarnessName(tool);
                    context.HarnesesWithContainers.Add(toolName);
                    break;

                case ContainerType.SkillMethodContainer:
                    // Track which functions this skill references
                    var skillName = GetSkillName(tool);
                    var referencedFunctions = GetReferencedFunctions(tool);
                    var referencedHarneses = GetReferencedTools(tool);
                    var parentSkillContainer = GetParentContainer(tool);

                    // Mark Harneses as having Collapsed skills ONLY if they are from a DIFFERENT Harness
                    // (i.e., skills referencing functions from external Harneses)
                    foreach (var referencedHarness in referencedHarneses)
                    {
                        // Only add if the referenced Harness is different from the skill's parent container
                        if (!string.Equals(referencedHarness, parentSkillContainer, StringComparison.OrdinalIgnoreCase))
                        {
                            context.HarnesesWithCollapsedSkills.Add(referencedHarness);
                        }
                    }

                    foreach (var funcRef in referencedFunctions)
                    {
                        // Extract function name from "HarnessName.FunctionName" format
                        var funcName = ExtractFunctionName(funcRef);
                        context.FunctionsReferencedBySkills.Add(funcName);

                        if (!context.SkillsReferencingFunction.ContainsKey(funcName))
                            context.SkillsReferencingFunction[funcName] = [];

                        context.SkillsReferencingFunction[funcName].Add(skillName);
                    }
                    break;

                case ContainerType.NotAContainer:
                    // Not a container - nothing to track in context
                    break;
            }
        }

        return context;
    }

    /// <summary>
    /// Context object holding all computed relationships for visibility checks.
    /// Built once per GetToolsForAgentTurn call for efficiency.
    /// </summary>
    private class VisibilityContext
    {
        public required List<AIFunction> AllTools { get; init; }
        public required ImmutableHashSet<string> ExpandedCollapsedHarnessContainers { get; init; }
        public required ImmutableHashSet<string> ExpandedSkillContainers { get; init; }
        public required HashSet<string> HarnesesWithContainers { get; init; }
        public required HashSet<string> SkillClassesWithCollapse { get; init; }
        public required HashSet<string> FunctionsReferencedBySkills { get; init; }
        public required Dictionary<string, List<string>> SkillsReferencingFunction { get; init; }
        public required HashSet<string> HarnesesWithCollapsedSkills { get; init; }
    }
}
