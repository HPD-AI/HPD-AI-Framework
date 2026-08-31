namespace HPD.Agent;

/// <summary>
/// Marks a class for collapsing - groups AI functions, skills, and sub-agents behind a container tool.
/// Classes without this attribute are auto-discovered but remain non-collapsed (all functions visible).
/// </summary>
/// <remarks>
/// <para><b>Terminology:</b></para>
/// <list type="bullet">
/// <item><b>ToolHarness</b>: A class containing [AIFunction], [Skill], and/or [SubAgent] methods</item>
/// <item><b>Tool</b>: An individual AI function within a toolharness</item>
/// <item><b>Collapsing</b>: Hiding tools behind a container that must be activated first</item>
/// </list>
///
/// <para><b>Auto-Discovery:</b></para>
/// <para>
/// The source generator automatically discovers classes with [AIFunction], [Skill], or [SubAgent] methods.
/// The [Collapse] attribute is ONLY needed if you want to hide the tools behind a container.
/// </para>
///
/// <para><b>Usage Patterns:</b></para>
/// <code>
/// // No attribute - auto-discovered, all functions visible
/// public class MathToolHarness
/// {
///     [AIFunction] public int Add(int a, int b) => a + b;
/// }
///
/// // Collapsed toolharness (functions hidden behind container)
/// [Collapse("Search operations across web and code")]
/// public class SearchToolHarness
/// {
///     [AIFunction] public Task&lt;string&gt; WebSearch(string query) { ... }
/// }
///
/// // Collapsed with dual-context instructions
/// [Collapse(
///     "Database operations",
///     FunctionResult = "Transaction functions available",
///     SystemPrompt = "Always use transactions for data modifications"
/// )]
/// public class DatabaseToolHarness { ... }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CollapseAttribute : Attribute
{
    /// <summary>
    /// Description shown when toolharness is collapsed into a container.
    /// Providing a description enables collapsing (based on CollapsingConfig.Enabled).
    /// To prevent specific toolharnesses from collapsing at runtime, use CollapsingConfig.NeverCollapse.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Instructions returned as FUNCTION RESULT when container is activated.
    /// Visible to LLM once, as contextual acknowledgment.
    /// Use for: Status messages, operation lists, dynamic feedback.
    /// </summary>
    public string? FunctionResult { get; set; }

    /// <summary>
    /// Instructions injected into SYSTEM PROMPT persistently after activation.
    /// Visible to LLM on every iteration after container expansion.
    /// Use for: Core rules, safety guidelines, best practices, permanent context.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Middleware types owned by this ToolHarness.
    /// Each type must implement <see cref="HPD.Agent.Middleware.IToolHarnessMiddleware"/> and
    /// declare a supported source-generated constructor shape, explicit exact-type override
    /// factory, or explicitly services-owned activation contract.
    /// Middleware is activated lazily in the execution-local ToolHarness pipeline on first
    /// applicable use, including rehydrated executions, and is released during final execution-owner
    /// teardown. Its lifetime is independent of descriptive system-prompt persistence.
    /// Middleware resolved from the execution child scope must declare
    /// <c>ToolHarnessMiddlewareLifetime(Services)</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// [Collapse("Database operations",
    ///     Middlewares = [typeof(DbAuditMiddleware), typeof(DbRateLimitMiddleware)])]
    /// public class DatabaseToolHarness { ... }
    /// </code>
    /// </example>
    public Type[]? Middlewares { get; set; }

    /// <summary>
    /// Constructor for collapsible toolharnesses.
    /// Providing a description enables collapsing (based on CollapsingConfig.Enabled).
    /// </summary>
    /// <param name="description">Description shown in the container tool</param>
    /// <exception cref="ArgumentNullException">Thrown when description is null</exception>
    public CollapseAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

}
