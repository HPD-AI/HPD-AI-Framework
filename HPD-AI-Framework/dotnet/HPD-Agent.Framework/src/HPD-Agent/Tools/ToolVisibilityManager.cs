using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace HPD.Agent.Collapsing;

public class ToolVisibilityManager
{
    private readonly ILogger<ToolVisibilityManager>? _logger;
    private readonly Dictionary<string, AIFunction> _allFunctionsByReference;
    private readonly ImmutableHashSet<string> _explicitlyRegisteredToolHarnesses;
    private readonly ImmutableHashSet<string> _neverCollapseToolHarnesses;

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ILogger<ToolVisibilityManager>? logger = null)
        : this(allFunctions, ImmutableHashSet<string>.Empty, null, logger)
    {
    }

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ImmutableHashSet<string> explicitlyRegisteredToolHarnesses,
        ILogger<ToolVisibilityManager>? logger = null)
        : this(allFunctions, explicitlyRegisteredToolHarnesses, null, logger)
    {
    }

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ImmutableHashSet<string> explicitlyRegisteredToolHarnesses,
        HashSet<string>? neverCollapseToolHarnesses,
        ILogger<ToolVisibilityManager>? logger = null)
    {
        _logger = logger;
        _explicitlyRegisteredToolHarnesses = explicitlyRegisteredToolHarnesses ?? ImmutableHashSet<string>.Empty;
        _neverCollapseToolHarnesses = neverCollapseToolHarnesses != null
            ? ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, neverCollapseToolHarnesses)
            : ImmutableHashSet<string>.Empty;
        _allFunctionsByReference = BuildFunctionLookup(allFunctions);
    }

    /// <summary>
    /// Gets tools visible for the current agent turn based on expansion state.
    /// Handles ToolHarness containers and type-safe Skill containers.
    ///
    /// Ordering strategy:
    /// 1. Collapse containers (skill class containers with [Collapse])
    /// 2. ToolHarness containers (Collapse ToolHarnesses with [Collapse])
    /// 3. Skill containers (type-safe Skills with IsContainer=true)
    /// 4. Non-Collapsed functions (always visible)
    /// 5. Expanded ToolHarness functions
    /// 6. Expanded skill functions
    ///
    /// Key insight: Functions in ToolHarnesses that are ONLY referenced by Collapsed skills
    /// are hidden until their parent skill is expanded. This prevents "orphan" functions
    /// from appearing when the skill class Collapse is not expanded.
    /// </summary>
    /// <param name="allTools">All available tools</param>
    /// <param name="expandedContainers">Unified set of expanded containers (both ToolHarnesses and skills)</param>
    public List<AIFunction> GetToolsForAgentTurn(
        List<AIFunction> allTools,
        ImmutableHashSet<string> expandedContainers)
    {
        //  Build context (first pass - identify relationships)
        var context = BuildVisibilityContext(allTools, expandedContainers, expandedContainers);

        var CollapseContainers = new List<AIFunction>();
        var skillContainers = new List<AIFunction>();
        var nonCollapsedFunctions = new List<AIFunction>();
        var expandedToolHarnessFunctions = new List<AIFunction>();
        var expandedSkillFunctions = new List<AIFunction>();

        // Phase 2: Categorize tools using visibility rules (second pass)
        foreach (var tool in allTools)
        {
            var containerType = GetContainerType(tool);

            switch (containerType)
            {
                case ContainerType.CollapseAttributeContainer:
                case ContainerType.CollapsedToolHarnessContainer:
                    // Both types are Collapse/ToolHarness containers - treat identically
                    if (IsToolHarnessContainerVisible(tool, context))
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

                            case FunctionVisibility.ExpandedToolHarness:
                                expandedToolHarnessFunctions.Add(tool);
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
            .Concat(expandedToolHarnessFunctions.OrderBy(f => f.Name))
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

            // Add by qualified name if parent ToolHarness exists
            var parentToolHarness = GetParentToolHarness(function);
            if (!string.IsNullOrEmpty(parentToolHarness))
            {
                var qualifiedName = $"{parentToolHarness}.{function.Name}";
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

    private string GetToolHarnessName(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("ToolHarnessName", out var v) == true && v is string s ? s : function.Name ?? string.Empty;

    private string GetSkillName(AIFunction function) =>
        function.Name ?? string.Empty;

    private string? GetParentToolHarness(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("ParentToolHarness", out var v) == true && v is string s ? s : null;

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
        if (skillContainer.AdditionalProperties?.TryGetValue("ReferencedToolHarnesses", out var v) == true && v is string[] ToolHarnesses)
            return ToolHarnesses;
        return Array.Empty<string>();
    }

    private string ExtractFunctionName(string reference)
    {
        // "ToolHarnessName.FunctionName" -> "FunctionName"
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

        // Check if this toolharness is in the NeverCollapse list (runtime override)
        // If so, treat it as not a container - functions will be visible directly
        if (_neverCollapseToolHarnesses.Contains(function.Name))
            return ContainerType.NotAContainer;

        // Check for IsToolHarnessContainer flag from [Collapse].
        if ((function.AdditionalProperties?.TryGetValue("IsToolHarnessContainer", out var toolharnessVal) == true &&
            toolharnessVal is bool toolharnessFlag && toolharnessFlag))
        {
            return ContainerType.CollapseAttributeContainer;
        }

        // Check for IsSkill flag ([Skill] method container)
        if (function.AdditionalProperties?.TryGetValue("IsSkill", out var skillVal) == true &&
            skillVal is bool skillFlag && skillFlag)
        {
            return ContainerType.SkillMethodContainer;
        }

        // Container with no special flags is a collapsed toolharness container.
        return ContainerType.CollapsedToolHarnessContainer;
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
        /// Metadata: IsToolHarnessContainer=true, IsContainer=true
        /// Generated by SkillCodeGenerator.GenerateCollapseContainer()
        /// </summary>
        CollapseAttributeContainer,

        /// <summary>
        /// Container for a Collapsed ToolHarness WITHOUT skills (ToolHarness-level Collapsing only).
        /// Example: [Collapse("Math")] on MathToolHarness class with only [AIFunction] methods
        /// Metadata: IsContainer=true, no IsSkill/IsToolHarnessContainer flags
        /// Generated by HPDHARNESSourceGenerator.GenerateToolHarnessContainer()
        /// Note: Both CollapseAttributeContainer and CollapsedToolHarnessContainer are treated identically at runtime.
        /// </summary>
        CollapsedToolHarnessContainer,

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
        ExpandedToolHarness,   // Visible because parent ToolHarness expanded (goes into expandedToolHarnessFunctions list)
        ExpandedSkill     // Visible because skill expanded (goes into expandedSkillFunctions list)
    }

    /// <summary>
    /// Rule: Collapse container is visible IFF:
    /// 1. It is NOT expanded, AND
    /// 2. It is NOT implicitly registered via skills (unless explicitly registered)
    /// Collapse containers can be tracked in either expandedCollapsedToolHarnessContainers or ExpandedSkillContainers.
    /// </summary>
    private bool IsToolHarnessContainerVisible(AIFunction container, VisibilityContext context)
    {
        // For CollapsedToolHarnessContainer, use ToolHarnessName. For CollapseAttributeContainer, use Name.
        // Both should work with the same string since they represent the same Collapse.
        var CollapseName = GetToolHarnessName(container);
        if (string.IsNullOrEmpty(CollapseName))
        {
            CollapseName = container.Name ?? string.Empty;
        }

        // Hide Collapse containers for ToolHarnesses that were ONLY implicitly registered via skills
        // (i.e., referenced by skills but NOT explicitly registered by the user)
        if (context.ToolHarnessesWithCollapsedSkills.Contains(CollapseName) &&
            !_explicitlyRegisteredToolHarnesses.Contains(CollapseName))
        {
            _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (implicitly registered via skills)");
            return false;
        }

        // Hide if parent container exists but is not yet expanded
        // This enables nested containers (e.g., MCP_wolfram inside SearchToolHarness)
        var parentContainerName = GetParentContainer(container);
        if (!string.IsNullOrEmpty(parentContainerName))
        {
            if (!context.ExpandedCollapsedToolHarnessContainers.Contains(parentContainerName) &&
                !context.ExpandedSkillContainers.Contains(parentContainerName))
            {
                _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (parent {parentContainerName} not expanded)");
                return false;
            }
        }

        // Hide if expanded (in either set)
        if (context.ExpandedCollapsedToolHarnessContainers.Contains(CollapseName) ||
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
            context.ExpandedCollapsedToolHarnessContainers.Contains(parentCollapse))
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
    /// PRIORITY 1: Parent ToolHarness Collapse check with skill bypass
    ///   - If ToolHarness has [Collapse] container AND function is referenced by an expanded skill → VISIBLE (skill bypass)
    ///   - If ToolHarness has [Collapse] container AND parent ToolHarness is expanded → VISIBLE
    ///   - If ToolHarness has [Collapse] container AND not expanded → HIDDEN
    /// PRIORITY 2: Explicit registration check (always show if explicitly registered)
    /// PRIORITY 3: Skill reference check (show if any referencing skill is expanded)
    /// PRIORITY 4: Orphan check (hide functions in implicitly-registered ToolHarnesses that aren't referenced)
    /// DEFAULT: Non-Collapsed, non-referenced, non-orphan functions are always visible
    /// </summary>
    private FunctionVisibility GetFunctionVisibility(AIFunction function, VisibilityContext context)
    {
        var functionName = function.Name ?? string.Empty;
        var parentToolHarness = GetParentToolHarness(function);

        // PRIORITY 0: If ToolHarness is in NeverCollapse, treat as non-collapsed
        if (parentToolHarness != null && _neverCollapseToolHarnesses.Contains(parentToolHarness))
        {
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (parent {parentToolHarness} in NeverCollapse)");
            return FunctionVisibility.NonCollapsed;
        }

        // PRIORITY 1: If ToolHarness has [Collapse] container, check skill bypass first
        if (parentToolHarness != null && context.ToolHarnessesWithContainers.Contains(parentToolHarness))
        {
            // Check if this function is referenced by an expanded skill (skill bypass for Collapsed ToolHarnesses)
            if (context.FunctionsReferencedBySkills.Contains(functionName))
            {
                var referencingSkills = context.SkillsReferencingFunction.GetValueOrDefault(functionName, new List<string>());
                bool anySkillExpanded = referencingSkills.Any(s => context.ExpandedSkillContainers.Contains(s));

                if (anySkillExpanded)
                {
                    _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (skill bypass for Collapsed ToolHarness {parentToolHarness})");
                    return FunctionVisibility.ExpandedSkill;
                }
            }

            // Otherwise, Collapsed ToolHarness function - only show if parent expanded
            if (context.ExpandedCollapsedToolHarnessContainers.Contains(parentToolHarness))
            {
                _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (Collapsed parent {parentToolHarness} expanded)");
                return FunctionVisibility.ExpandedToolHarness;
            }

            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (Collapsed parent {parentToolHarness} not expanded)");
            return FunctionVisibility.Hidden;
        }

        // PRIORITY 2: If ToolHarness is explicitly registered (and NOT Collapsed), show all its functions
        // (Explicit registration takes precedence over skill references)
        if (parentToolHarness != null && _explicitlyRegisteredToolHarnesses.Contains(parentToolHarness))
        {
            // Explicitly registered ToolHarness - always show functions
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (explicitly registered ToolHarness)");
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
        if (parentToolHarness != null && context.ToolHarnessesWithCollapsedSkills.Contains(parentToolHarness))
        {
            // This function is in a ToolHarness that was auto-registered via Collapsed skills
            // BUT this function is NOT referenced by any skill (it's an orphan)
            // Hide it - don't add to any list
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (orphan in implicitly-registered ToolHarness {parentToolHarness})");
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
        ImmutableHashSet<string> expandedCollapsedToolHarnessContainers,
        ImmutableHashSet<string> expandedSkillContainers)
    {
        var context = new VisibilityContext
        {
            AllTools = allTools,
            ExpandedCollapsedToolHarnessContainers = expandedCollapsedToolHarnessContainers,
            ExpandedSkillContainers = expandedSkillContainers,
            ToolHarnessesWithContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SkillClassesWithCollapse = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            FunctionsReferencedBySkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SkillsReferencingFunction = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            ToolHarnessesWithCollapsedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var tool in allTools)
        {
            var containerType = GetContainerType(tool);

            switch (containerType)
            {
                case ContainerType.CollapseAttributeContainer:
                    // Collapse container (can be class-level or ToolHarness-level)
                    var CollapseName = tool.Name ?? string.Empty;
                    context.SkillClassesWithCollapse.Add(CollapseName);
                    // Also track as a ToolHarness with container so functions get hidden/shown properly
                    context.ToolHarnessesWithContainers.Add(CollapseName);
                    break;

                case ContainerType.CollapsedToolHarnessContainer:
                    var toolName = GetToolHarnessName(tool);
                    context.ToolHarnessesWithContainers.Add(toolName);
                    break;

                case ContainerType.SkillMethodContainer:
                    // Track which functions this skill references
                    var skillName = GetSkillName(tool);
                    var referencedFunctions = GetReferencedFunctions(tool);
                    var referencedToolHarnesses = GetReferencedTools(tool);
                    var parentSkillContainer = GetParentContainer(tool);

                    // Mark ToolHarnesses as having Collapsed skills ONLY if they are from a DIFFERENT ToolHarness
                    // (i.e., skills referencing functions from external ToolHarnesses)
                    foreach (var referencedToolHarness in referencedToolHarnesses)
                    {
                        // Only add if the referenced ToolHarness is different from the skill's parent container
                        if (!string.Equals(referencedToolHarness, parentSkillContainer, StringComparison.OrdinalIgnoreCase))
                        {
                            context.ToolHarnessesWithCollapsedSkills.Add(referencedToolHarness);
                        }
                    }

                    foreach (var funcRef in referencedFunctions)
                    {
                        // Extract function name from "ToolHarnessName.FunctionName" format
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
        public required ImmutableHashSet<string> ExpandedCollapsedToolHarnessContainers { get; init; }
        public required ImmutableHashSet<string> ExpandedSkillContainers { get; init; }
        public required HashSet<string> ToolHarnessesWithContainers { get; init; }
        public required HashSet<string> SkillClassesWithCollapse { get; init; }
        public required HashSet<string> FunctionsReferencedBySkills { get; init; }
        public required Dictionary<string, List<string>> SkillsReferencingFunction { get; init; }
        public required HashSet<string> ToolHarnessesWithCollapsedSkills { get; init; }
    }
}
