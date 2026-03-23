namespace HPD.Agent;

/// <summary>
/// Marks a class for collapsing - groups AI functions, skills, and sub-agents behind a container tool.
/// Classes without this attribute are auto-discovered but remain non-collapsed (all functions visible).
/// </summary>
/// <remarks>
/// <para><b>Terminology:</b></para>
/// <list type="bullet">
/// <item><b>Toolkit</b>: A class containing [AIFunction], [Skill], and/or [SubAgent] methods</item>
/// <item><b>Tool</b>: An individual AI function within a toolkit</item>
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
/// public class MathToolkit
/// {
///     [AIFunction] public int Add(int a, int b) => a + b;
/// }
///
/// // Collapsed toolkit (functions hidden behind container)
/// [Collapse("Search operations across web and code")]
/// public class SearchToolkit
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
/// public class DatabaseToolkit { ... }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CollapseAttribute : Attribute
{
    /// <summary>
    /// Description shown when toolkit is collapsed into a container.
    /// Providing a description enables collapsing (based on CollapsingConfig.Enabled).
    /// To prevent specific toolkits from collapsing at runtime, use CollapsingConfig.NeverCollapse.
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
    /// Middleware types to activate when this container is expanded by the LLM.
    /// Each type must implement <see cref="HPD.Agent.Middleware.IAgentMiddleware"/> and have a
    /// parameterless constructor (or a single-parameter config constructor — see proposal §5A).
    /// Instances are created at expansion time and disposed at turn end (or session end if
    /// <c>CollapsingConfig.PersistSystemPromptInjections = true</c>).
    /// For middleware requiring DI, use <c>WithToolkit&lt;T&gt;(opts =&gt; opts.AddScopedMiddleware(...))</c> instead.
    /// </summary>
    /// <example>
    /// <code>
    /// [Collapse("Database operations",
    ///     Middlewares = [typeof(DbAuditMiddleware), typeof(DbRateLimitMiddleware)])]
    /// public class DatabaseToolkit { ... }
    /// </code>
    /// </example>
    public Type[]? Middlewares { get; set; }

    /// <summary>
    /// Constructor for collapsible toolkits.
    /// Providing a description enables collapsing (based on CollapsingConfig.Enabled).
    /// </summary>
    /// <param name="description">Description shown in the container tool</param>
    /// <exception cref="ArgumentNullException">Thrown when description is null</exception>
    public CollapseAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    /// <summary>
    /// Constructor for collapsible toolkits with dual-context instruction injection.
    /// </summary>
    /// <param name="description">Brief description of container capabilities</param>
    /// <param name="FunctionResult">Optional instructions returned as function result (ephemeral, one-time)</param>
    /// <param name="SystemPrompt">Optional instructions injected into system prompt (persistent, every iteration)</param>
    /// <exception cref="ArgumentNullException">Thrown when description is null</exception>
    public CollapseAttribute(
        string description,
        string? FunctionResult = null,
        string? SystemPrompt = null)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        this.FunctionResult = FunctionResult;
        this.SystemPrompt = SystemPrompt;
    }
}
