using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using HPD.Agent.SourceGenerator.Capabilities;

/// <summary>
/// Information about a Toolkit discovered during source generation.
/// </summary>
internal class ToolkitInfo
{
    /// <summary>
    /// The class name (always set from ClassDeclarationSyntax.Identifier).
    /// Used for file names, type references, registry lookup, and container names.
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// The effective name used in registry, container functions, and skill references.
    /// Always returns ClassName (CustomName support has been removed).
    /// </summary>
    public string EffectiveName => ClassName;

    /// <summary>
    /// Description of the toolkit capabilities.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Namespace where the toolkit is defined.
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    // ========== UNIFIED DATA STRUCTURE (Phase 5: Complete) ==========

    /// <summary>
    /// Unified list of all capabilities (Functions, Skills, SubAgents).
    /// Phase 5: Single source of truth for all capability types.
    /// </summary>
    public List<ICapability> Capabilities { get; set; } = new();

    // ========== HELPER PROPERTIES (Type-Specific Queries) ==========

    /// <summary>
    /// Gets all skill capabilities from the unified Capabilities list.
    /// This provides backward compatibility for code that needs to filter by type.
    /// </summary>
    public IEnumerable<SkillCapability> SkillCapabilities =>
        Capabilities.OfType<SkillCapability>();

    /// <summary>
    /// Gets all function capabilities from the unified Capabilities list.
    /// </summary>
    public IEnumerable<FunctionCapability> FunctionCapabilities =>
        Capabilities.OfType<FunctionCapability>();

    /// <summary>
    /// Gets all sub-agent capabilities from the unified Capabilities list.
    /// </summary>
    public IEnumerable<SubAgentCapability> SubAgentCapabilities =>
        Capabilities.OfType<SubAgentCapability>();

    /// <summary>
    /// Gets all multi-agent capabilities from the unified Capabilities list.
    /// </summary>
    public IEnumerable<MultiAgentCapability> MultiAgentCapabilities =>
        Capabilities.OfType<MultiAgentCapability>();

    /// <summary>
    /// Gets all MCP server capabilities from the unified Capabilities list.
    /// </summary>
    public IEnumerable<MCPServerCapability> MCPServerCapabilities =>
        Capabilities.OfType<MCPServerCapability>();

    /// <summary>
    /// Gets all OpenAPI spec capabilities from the unified Capabilities list.
    /// </summary>
    public IEnumerable<OpenApiCapability> OpenApiCapabilities =>
        Capabilities.OfType<OpenApiCapability>();

    /// <summary>
    /// Total count of all capabilities.
    /// </summary>
    public int CapabilityCount => Capabilities.Count;

    /// <summary>
    /// Whether this Toolkit requires an instance parameter in CreateToolkit().
    /// All capability types (Functions, Skills, SubAgents) can access instance state.
    /// This enables dynamic container instructions via instance methods.
    /// Phase 4: Uses only unified Capabilities list.
    /// </summary>
    public bool RequiresInstance =>
        Capabilities.Any(c => c.RequiresInstance);

    /// <summary>
    /// Whether any capabilities have conditional logic or dynamic descriptions requiring context resolution.
    /// Phase 4: Uses only unified Capabilities list.
    /// </summary>
    public bool RequiresContext =>
        Capabilities.Any(c => c.IsConditional || c.HasDynamicDescription);

    /// <summary>
    /// Whether this Toolkit has a parameterless constructor.
    /// Only Toolkits with parameterless constructors can be included in the ToolkitRegistry.All catalog.
    /// Toolkits without parameterless constructors (e.g., those requiring DI) must be registered through
    /// special extension methods like WithDynamicMemory() or WithPlanMode().
    /// </summary>
    public bool HasParameterlessConstructor { get; set; } = true;

    /// <summary>
    /// Whether this Toolkit has a constructor whose sole parameter is ISecretResolver.
    /// When true, the source generator emits a CreateWithSecrets delegate and includes
    /// the toolkit in ToolkitRegistry.All so AgentBuilder can instantiate it automatically.
    /// Example: public StripeToolkit(ISecretResolver secrets)
    /// </summary>
    public bool HasSecretsConstructor { get; set; } = false;

    /// <summary>
    /// Whether this Toolkit class is publicly accessible.
    /// Only publicly accessible classes can be included in the ToolkitRegistry.All catalog.
    /// Private/internal classes (e.g., test fixtures) are still processed for individual Registration files
    /// but are excluded from the registry.
    /// </summary>
    public bool IsPubliclyAccessible { get; set; } = true;

    /// <summary>
    /// Diagnostics collected during Toolkit analysis (e.g., dual-context validation errors).
    /// These are reported during source generation in GenerateToolRegistrations.
    /// </summary>
    public List<Diagnostic> Diagnostics { get; set; } = new();

    // ========== CONFIG SERIALIZATION PROPERTIES (Phase: Config Serialization) ==========

    /// <summary>
    /// Fully qualified type name of the config constructor parameter, if detected.
    /// Example: "MyApp.Config.SearchToolkitConfig" for SearchToolkit(SearchToolkitConfig config).
    /// Null if toolkit has no config constructor.
    /// Used by source generator to emit CreateFromConfig delegate.
    /// </summary>
    public string? ConfigConstructorTypeName { get; set; }

    /// <summary>
    /// Metadata type name from [AIFunction&lt;TMetadata&gt;] on toolkit methods.
    /// Example: "MyApp.Context.SearchContext" for [AIFunction&lt;SearchContext&gt;].
    /// Null if toolkit methods don't use typed metadata.
    /// Used by source generator to emit MetadataType in ToolkitFactory.
    /// </summary>
    public string? MetadataTypeName { get; set; }

    /// <summary>
    /// All function names in this toolkit.
    /// Example: ["WebSearch", "ImageSearch", "NewsSearch"]
    /// Used for selective function registration via ToolkitReference.Functions.
    /// Populated during capability analysis.
    /// </summary>
    public List<string> FunctionNames { get; set; } = new();

    // ========== TOOLKIT ATTRIBUTE PROPERTIES ==========

    /// <summary>
    /// Whether this toolkit should be collapsed (has description).
    /// When true, functions are hidden behind a container that must be expanded.
    /// Runtime override available via CollapsingConfig.NeverCollapse.
    /// </summary>
    public bool IsCollapsed { get; set; }

    /// <summary>
    /// Description from [Collapse] attribute (if present).
    /// Used for the container function description when collapsed.
    /// </summary>
    public string? ContainerDescription { get; set; }

    /// <summary>
    /// Instructions returned as FUNCTION RESULT when container is activated (literal value).
    /// </summary>
    public string? FunctionResult { get; set; }

    /// <summary>
    /// Instructions returned as FUNCTION RESULT when container is activated (expression/method call).
    /// </summary>
    public string? FunctionResultExpression { get; set; }

    /// <summary>
    /// Whether FunctionResultExpression is a static member (true) or instance member (false).
    /// </summary>
    public bool FunctionResultIsStatic { get; set; } = true;

    /// <summary>
    /// Instructions injected into SYSTEM PROMPT persistently after activation (literal value).
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Instructions injected into SYSTEM PROMPT persistently after activation (expression/method call).
    /// </summary>
    public string? SystemPromptExpression { get; set; }

    /// <summary>
    /// Whether SystemPromptExpression is a static member (true) or instance member (false).
    /// </summary>
    public bool SystemPromptIsStatic { get; set; } = true;

    // ========== TOOLKIT-SCOPED MIDDLEWARE (015) ==========

    /// <summary>
    /// Fully-qualified type names of middleware declared in <c>[Collapse(Middlewares = [...])]</c>
    /// that have a public parameterless constructor.
    /// Example: ["MyApp.DbAuditMiddleware"]
    /// Null/empty when toolkit has no parameterless-constructor scoped middlewares.
    /// Source generator emits these as direct <c>static () =&gt; new T()</c> delegates in CollapseMiddlewareFactories.
    /// </summary>
    public List<string>? CollapseMiddlewareTypeNames { get; set; }

    /// <summary>
    /// Config-constructor middleware types declared in <c>[Collapse(Middlewares = [...])]</c>
    /// that have a single config-parameter constructor (§5A).
    /// Each entry carries the simple class name, the fully-qualified middleware type name,
    /// and the fully-qualified config parameter type name.
    /// Null/empty when toolkit has no config-constructor scoped middlewares.
    /// Source generator emits these as <c>static json =&gt; new T(json.Deserialize&lt;TConfig&gt;()!)</c>
    /// delegates in CollapseMiddlewareConfigFactories.
    /// </summary>
    public List<CollapseMiddlewareConfigEntry>? CollapseMiddlewareConfigTypeNames { get; set; }

    // ========== BACKWARD COMPATIBILITY ==========

    /// <summary>
    /// Alias for ClassName to maintain backward compatibility.
    /// New code should use ClassName or EffectiveName instead.
    /// </summary>
    public string Name
    {
        get => ClassName;
        set => ClassName = value;
    }
}

// ========== TOOLKIT-SCOPED MIDDLEWARE CONFIG ENTRY (015 §5A) ==========

/// <summary>
/// Metadata for a config-constructor scoped middleware type discovered by the source generator.
/// </summary>
internal sealed record CollapseMiddlewareConfigEntry(
    /// <summary>Simple class name, e.g. "DbRateLimitMiddleware". Used as the MiddlewareTypeName in the generated CollapseMiddlewareConfigFactory.</summary>
    string SimpleName,
    /// <summary>Fully-qualified middleware type name for the constructor call in generated code.</summary>
    string FullyQualifiedTypeName,
    /// <summary>Fully-qualified config parameter type name for the Deserialize call in generated code.</summary>
    string ConfigTypeFqn
);

// ========== OLD CLASSES REMOVED (Phase 4) ==========
// FunctionInfo, ParameterInfo, and ValidationData have been removed.
// These are now defined in the Capabilities namespace:
// - HPD.Agent.SourceGenerator.Capabilities.FunctionCapability (replaces FunctionInfo)
// - HPD.Agent.SourceGenerator.Capabilities.ParameterInfo (shared by all capabilities)
// - HPD.Agent.SourceGenerator.Capabilities.ValidationData (part of FunctionCapability)

// ========== RENAMED (Phase: Toolkit Consolidation) ==========
// ToolInfo has been renamed to ToolkitInfo.
// The Name property is now ClassName. CustomName support has been removed - always use ClassName.
