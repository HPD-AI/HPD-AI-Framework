// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

/// <summary>
/// Per-toolharness configuration provided at builder registration time via
/// <c>WithToolHarness&lt;T&gt;(opts => opts.AddScopedMiddleware(...))</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the §5B (builder-time DI override) path from .
/// Use it when your toolharness-scoped middleware requires constructor parameters
/// that cannot be expressed as a parameterless constructor in
/// <c>[Collapse(Middlewares = [typeof(T)])]</c>.
/// </para>
/// <example>
/// <code>
/// builder.WithToolHarness&lt;DatabaseToolHarness&gt;(opts =>
///     opts.AddScopedMiddleware(new DbAuditMiddleware(sp.GetRequiredService&lt;IAuditLog&gt;()))
///         .AddScopedMiddleware(new DbRateLimitMiddleware(new DbRateLimitConfig { RequestsPerMinute = 20 })));
/// </code>
/// </example>
/// </remarks>
public sealed class ToolHarnessOptions
{
    internal readonly List<Middleware.IAgentMiddleware> ScopedMiddlewares = [];
    internal readonly List<ISkillSource> SkillSources = [];
    internal readonly List<StoredSkillSourceRegistration> StoredSkillSources = [];

    /// <summary>
    /// Adds a middleware instance that will be activated whenever this toolharness's container is
    /// expanded by the LLM. The instance is merged with any middlewares declared on the toolharness
    /// class via <c>[Collapse(Middlewares = [...])]</c>, with DI-provided instances appended after
    /// attribute-declared ones.
    /// </summary>
    /// <param name="middleware">Middleware instance to activate on toolharness expansion.</param>
    /// <returns>This <see cref="ToolHarnessOptions"/> for chaining.</returns>
    public ToolHarnessOptions AddScopedMiddleware(Middleware.IAgentMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        ScopedMiddlewares.Add(middleware);
        return this;
    }

    /// <summary>Adds a runtime skill source owned by this tool harness.</summary>
    public ToolHarnessOptions AddSkillSource(ISkillSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SkillSources.Add(source);
        return this;
    }

    /// <summary>Loads managed skills from the builder- or DI-configured content-backed skill store.</summary>
    public ToolHarnessOptions AddSkillsFromStore(SkillQuery? query = null)
    {
        StoredSkillSources.Add(new(null, query ?? new SkillQuery()));
        return this;
    }

    /// <summary>Loads managed skills from an explicit content-backed store.</summary>
    public ToolHarnessOptions AddSkillsFromStore(IContentBackedSkillStore store, SkillQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        StoredSkillSources.Add(new(store, query ?? new SkillQuery()));
        return this;
    }
}

internal sealed record StoredSkillSourceRegistration(IContentBackedSkillStore? Store, SkillQuery Query);
