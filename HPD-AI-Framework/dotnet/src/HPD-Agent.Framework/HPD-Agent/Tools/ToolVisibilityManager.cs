using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace HPD.Agent.Collapsing;

public class ToolVisibilityManager
{
    private readonly ILogger<ToolVisibilityManager>? _logger;
    private readonly Dictionary<string, AIFunction> _allFunctionsByReference;
    private readonly ImmutableHashSet<string> _explicitlyRegisteredToolkits;
    private readonly ImmutableHashSet<string> _neverCollapseToolkits;

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ILogger<ToolVisibilityManager>? logger = null)
        : this(allFunctions, ImmutableHashSet<string>.Empty, null, logger)
    {
    }

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ImmutableHashSet<string> explicitlyRegisteredToolkits,
        ILogger<ToolVisibilityManager>? logger = null)
        : this(allFunctions, explicitlyRegisteredToolkits, null, logger)
    {
    }

    public ToolVisibilityManager(
        IEnumerable<AIFunction> allFunctions,
        ImmutableHashSet<string> explicitlyRegisteredToolkits,
        HashSet<string>? neverCollapseToolkits,
        ILogger<ToolVisibilityManager>? logger = null)
    {
        _logger = logger;
        _explicitlyRegisteredToolkits = explicitlyRegisteredToolkits ?? ImmutableHashSet<string>.Empty;
        _neverCollapseToolkits = neverCollapseToolkits != null
            ? ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, neverCollapseToolkits)
            : ImmutableHashSet<string>.Empty;
        _allFunctionsByReference = BuildFunctionLookup(allFunctions);
    }

    /// <summary>
    /// Gets tools visible for the current agent turn based on expansion state.
    /// Handles Toolkit containers and type-safe Skill containers.
    ///
    /// Ordering strategy:
    /// 1. Collapse containers (skill class containers with [Collapse])
    /// 2. Toolkit containers (Collapse Toolkits with [Collapse])
    /// 3. Skill containers (type-safe Skills with IsContainer=true)
    /// 4. Non-Collapsed functions (always visible)
    /// 5. Expanded Toolkit functions
    /// 6. Expanded skill functions
    ///
    /// Key insight: Functions in Toolkits that are ONLY referenced by Collapsed skills
    /// are hidden until their parent skill is expanded. This prevents "orphan" functions
    /// from appearing when the skill class Collapse is not expanded.
    /// </summary>
    /// <param name="allTools">All available tools</param>
    /// <param name="expandedContainers">Unified set of expanded containers (both Toolkits and skills)</param>
    public List<AIFunction> GetToolsForAgentTurn(
        List<AIFunction> allTools,
        ImmutableHashSet<string> expandedContainers)
    {
        // Use the same set for both Toolkits and skills (unified container tracking)
        return GetToolsForAgentTurn(allTools, expandedContainers, expandedContainers);
    }

    /// <summary>
    /// Gets tools visible for the current agent turn based on expansion state.
    /// Handles Toolkit containers and type-safe Skill containers.
    ///
    /// Ordering strategy:
    /// 1. Collapse containers (skill class containers with [Collapse])
    /// 2. Toolkit containers (Collapse Toolkits with [Collapse])
    /// 3. Skill containers (type-safe Skills with IsContainer=true)
    /// 4. Non-Collapsed functions (always visible)
    /// 5. Expanded Toolkit functions
    /// 6. Expanded skill functions
    ///
    /// Key insight: Functions in Toolkits that are ONLY referenced by Collapsed skills
    /// are hidden until their parent skill is expanded. This prevents "orphan" functions
    /// from appearing when the skill class Collapse is not expanded.
    /// </summary>
    /// <param name="allTools">All available tools</param>
    /// <param name="expandedCollapsedToolkitContainers">Set of expanded Toolkit containers</param>
    /// <param name="expandedSkillContainers">Set of expanded skill containers</param>
    /// <remarks>
    /// This overload is maintained for backward compatibility. Prefer using the single-parameter
    /// overload with unified ExpandedContainers.
    /// </remarks>
    public List<AIFunction> GetToolsForAgentTurn(
        List<AIFunction> allTools,
        ImmutableHashSet<string> expandedCollapsedToolkitContainers,
        ImmutableHashSet<string> expandedSkillContainers)
    {
        //  Build context (first pass - identify relationships)
        var context = BuildVisibilityContext(allTools, expandedCollapsedToolkitContainers, expandedSkillContainers);

        var CollapseContainers = new List<AIFunction>();
        var skillContainers = new List<AIFunction>();
        var nonCollapsedFunctions = new List<AIFunction>();
        var expandedToolkitFunctions = new List<AIFunction>();
        var expandedSkillFunctions = new List<AIFunction>();

        // Phase 2: Categorize tools using visibility rules (second pass)
        foreach (var tool in allTools)
        {
            var containerType = GetContainerType(tool);

            switch (containerType)
            {
                case ContainerType.CollapseAttributeContainer:
                case ContainerType.CollapsedToolkitContainer:
                    // Both types are Collapse/Toolkit containers - treat identically
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

                            case FunctionVisibility.ExpandedToolkit:
                                expandedToolkitFunctions.Add(tool);
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
            .Concat(expandedToolkitFunctions.OrderBy(f => f.Name))
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

            // Add by qualified name if parent Toolkit exists
            var parentToolkit = GetParentToolkit(function);
            if (!string.IsNullOrEmpty(parentToolkit))
            {
                var qualifiedName = $"{parentToolkit}.{function.Name}";
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

    private string GetToolkitName(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("ToolkitName", out var v) == true && v is string s ? s : function.Name ?? string.Empty;

    private string GetSkillName(AIFunction function) =>
        function.Name ?? string.Empty;

    private string? GetParentToolkit(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("ParentToolkit", out var v) == true && v is string s ? s : null;

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
        if (skillContainer.AdditionalProperties?.TryGetValue("ReferencedToolkits", out var v) == true && v is string[] Toolkits)
            return Toolkits;
        return Array.Empty<string>();
    }

    private string ExtractFunctionName(string reference)
    {
        // "ToolkitName.FunctionName" -> "FunctionName"
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

        // Check if this toolkit is in the NeverCollapse list (runtime override)
        // If so, treat it as not a container - functions will be visible directly
        if (_neverCollapseToolkits.Contains(function.Name))
            return ContainerType.NotAContainer;

        // Check for IsToolkitContainer flag (from [Collapse] attribute) or IsCollapse flag (legacy compatibility)
        if ((function.AdditionalProperties?.TryGetValue("IsToolkitContainer", out var toolkitVal) == true &&
            toolkitVal is bool toolkitFlag && toolkitFlag) ||
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

        // Container with no special flags = legacy collapsed toolkit
        return ContainerType.CollapsedToolkitContainer;
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
        /// Container for a Collapsed Toolkit WITHOUT skills (Toolkit-level Collapsing only).
        /// Example: [Collapse("Math")] on MathToolkit class with only [AIFunction] methods
        /// Metadata: IsContainer=true, no IsSkill/IsCollapse flags
        /// Generated by HPDToolkitSourceGenerator.GenerateToolkitContainer()
        /// Note: Both CollapseAttributeContainer and CollapsedToolkitContainer are treated identically at runtime.
        /// </summary>
        CollapsedToolkitContainer,

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
        ExpandedToolkit,   // Visible because parent Toolkit expanded (goes into expandedToolkitFunctions list)
        ExpandedSkill     // Visible because skill expanded (goes into expandedSkillFunctions list)
    }

    /// <summary>
    /// Rule: Collapse container is visible IFF:
    /// 1. It is NOT expanded, AND
    /// 2. It is NOT implicitly registered via skills (unless explicitly registered)
    /// Collapse containers can be tracked in either expandedCollapsedToolkitContainers or ExpandedSkillContainers.
    /// </summary>
    private bool IsCollapseContainerVisible(AIFunction container, VisibilityContext context)
    {
        // For CollapsedToolkitContainer, use ToolkitName. For CollapseAttributeContainer, use Name.
        // Both should work with the same string since they represent the same Collapse.
        var CollapseName = GetToolkitName(container);
        if (string.IsNullOrEmpty(CollapseName))
        {
            CollapseName = container.Name ?? string.Empty;
        }

        // Hide Collapse containers for Toolkits that were ONLY implicitly registered via skills
        // (i.e., referenced by skills but NOT explicitly registered by the user)
        if (context.ToolkitsWithCollapsedSkills.Contains(CollapseName) &&
            !_explicitlyRegisteredToolkits.Contains(CollapseName))
        {
            _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (implicitly registered via skills)");
            return false;
        }

        // Hide if parent container exists but is not yet expanded
        // This enables nested containers (e.g., MCP_wolfram inside SearchToolkit)
        var parentContainerName = GetParentContainer(container);
        if (!string.IsNullOrEmpty(parentContainerName))
        {
            if (!context.ExpandedCollapsedToolkitContainers.Contains(parentContainerName) &&
                !context.ExpandedSkillContainers.Contains(parentContainerName))
            {
                _logger?.LogDebug($"[VISIBILITY] Collapse container {CollapseName}: HIDDEN (parent {parentContainerName} not expanded)");
                return false;
            }
        }

        // Hide if expanded (in either set)
        if (context.ExpandedCollapsedToolkitContainers.Contains(CollapseName) ||
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
            context.ExpandedCollapsedToolkitContainers.Contains(parentCollapse))
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
    /// PRIORITY 1: Parent Toolkit Collapse check with skill bypass
    ///   - If Toolkit has [Collapse] container AND function is referenced by an expanded skill → VISIBLE (skill bypass)
    ///   - If Toolkit has [Collapse] container AND parent Toolkit is expanded → VISIBLE
    ///   - If Toolkit has [Collapse] container AND not expanded → HIDDEN
    /// PRIORITY 2: Explicit registration check (always show if explicitly registered)
    /// PRIORITY 3: Skill reference check (show if any referencing skill is expanded)
    /// PRIORITY 4: Orphan check (hide functions in implicitly-registered Toolkits that aren't referenced)
    /// DEFAULT: Non-Collapsed, non-referenced, non-orphan functions are always visible
    /// </summary>
    private FunctionVisibility GetFunctionVisibility(AIFunction function, VisibilityContext context)
    {
        var functionName = function.Name ?? string.Empty;
        var parentToolkit = GetParentToolkit(function);

        // PRIORITY 0: If Toolkit is in NeverCollapse, treat as non-collapsed
        if (parentToolkit != null && _neverCollapseToolkits.Contains(parentToolkit))
        {
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (parent {parentToolkit} in NeverCollapse)");
            return FunctionVisibility.NonCollapsed;
        }

        // PRIORITY 1: If Toolkit has [Collapse] container, check skill bypass first
        if (parentToolkit != null && context.ToolkitsWithContainers.Contains(parentToolkit))
        {
            // Check if this function is referenced by an expanded skill (skill bypass for Collapsed Toolkits)
            if (context.FunctionsReferencedBySkills.Contains(functionName))
            {
                var referencingSkills = context.SkillsReferencingFunction.GetValueOrDefault(functionName, new List<string>());
                bool anySkillExpanded = referencingSkills.Any(s => context.ExpandedSkillContainers.Contains(s));

                if (anySkillExpanded)
                {
                    _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (skill bypass for Collapsed Toolkit {parentToolkit})");
                    return FunctionVisibility.ExpandedSkill;
                }
            }

            // Otherwise, Collapsed Toolkit function - only show if parent expanded
            if (context.ExpandedCollapsedToolkitContainers.Contains(parentToolkit))
            {
                _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (Collapsed parent {parentToolkit} expanded)");
                return FunctionVisibility.ExpandedToolkit;
            }

            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (Collapsed parent {parentToolkit} not expanded)");
            return FunctionVisibility.Hidden;
        }

        // PRIORITY 2: If Toolkit is explicitly registered (and NOT Collapsed), show all its functions
        // (Explicit registration takes precedence over skill references)
        if (parentToolkit != null && _explicitlyRegisteredToolkits.Contains(parentToolkit))
        {
            // Explicitly registered Toolkit - always show functions
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: VISIBLE (explicitly registered Toolkit)");
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
        if (parentToolkit != null && context.ToolkitsWithCollapsedSkills.Contains(parentToolkit))
        {
            // This function is in a Toolkit that was auto-registered via Collapsed skills
            // BUT this function is NOT referenced by any skill (it's an orphan)
            // Hide it - don't add to any list
            _logger?.LogDebug($"[VISIBILITY] Function {functionName}: HIDDEN (orphan in implicitly-registered Toolkit {parentToolkit})");
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
        ImmutableHashSet<string> expandedCollapsedToolkitContainers,
        ImmutableHashSet<string> expandedSkillContainers)
    {
        var context = new VisibilityContext
        {
            AllTools = allTools,
            ExpandedCollapsedToolkitContainers = expandedCollapsedToolkitContainers,
            ExpandedSkillContainers = expandedSkillContainers,
            ToolkitsWithContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SkillClassesWithCollapse = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            FunctionsReferencedBySkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            SkillsReferencingFunction = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            ToolkitsWithCollapsedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var tool in allTools)
        {
            var containerType = GetContainerType(tool);

            switch (containerType)
            {
                case ContainerType.CollapseAttributeContainer:
                    // Collapse container (can be class-level or Toolkit-level)
                    var CollapseName = tool.Name ?? string.Empty;
                    context.SkillClassesWithCollapse.Add(CollapseName);
                    // Also track as a Toolkit with container so functions get hidden/shown properly
                    context.ToolkitsWithContainers.Add(CollapseName);
                    break;

                case ContainerType.CollapsedToolkitContainer:
                    var toolName = GetToolkitName(tool);
                    context.ToolkitsWithContainers.Add(toolName);
                    break;

                case ContainerType.SkillMethodContainer:
                    // Track which functions this skill references
                    var skillName = GetSkillName(tool);
                    var referencedFunctions = GetReferencedFunctions(tool);
                    var referencedToolkits = GetReferencedTools(tool);
                    var parentSkillContainer = GetParentContainer(tool);

                    // Mark Toolkits as having Collapsed skills ONLY if they are from a DIFFERENT Toolkit
                    // (i.e., skills referencing functions from external Toolkits)
                    foreach (var referencedToolkit in referencedToolkits)
                    {
                        // Only add if the referenced Toolkit is different from the skill's parent container
                        if (!string.Equals(referencedToolkit, parentSkillContainer, StringComparison.OrdinalIgnoreCase))
                        {
                            context.ToolkitsWithCollapsedSkills.Add(referencedToolkit);
                        }
                    }

                    foreach (var funcRef in referencedFunctions)
                    {
                        // Extract function name from "ToolkitName.FunctionName" format
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
        public required ImmutableHashSet<string> ExpandedCollapsedToolkitContainers { get; init; }
        public required ImmutableHashSet<string> ExpandedSkillContainers { get; init; }
        public required HashSet<string> ToolkitsWithContainers { get; init; }
        public required HashSet<string> SkillClassesWithCollapse { get; init; }
        public required HashSet<string> FunctionsReferencedBySkills { get; init; }
        public required Dictionary<string, List<string>> SkillsReferencingFunction { get; init; }
        public required HashSet<string> ToolkitsWithCollapsedSkills { get; init; }
    }
}
