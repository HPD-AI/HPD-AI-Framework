// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent;

/// <summary>
/// Per-toolkit configuration provided at builder registration time via
/// <c>WithToolkit&lt;T&gt;(opts => opts.AddScopedMiddleware(...))</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the §5B (builder-time DI override) path from .
/// Use it when your toolkit-scoped middleware requires constructor parameters
/// that cannot be expressed as a parameterless constructor in
/// <c>[Collapse(Middlewares = [typeof(T)])]</c>.
/// </para>
/// <example>
/// <code>
/// builder.WithToolkit&lt;DatabaseToolkit&gt;(opts =>
///     opts.AddScopedMiddleware(new DbAuditMiddleware(sp.GetRequiredService&lt;IAuditLog&gt;()))
///         .AddScopedMiddleware(new DbRateLimitMiddleware(new DbRateLimitConfig { RequestsPerMinute = 20 })));
/// </code>
/// </example>
/// </remarks>
public sealed class ToolkitOptions
{
    internal readonly List<Middleware.IAgentMiddleware> ScopedMiddlewares = [];

    /// <summary>
    /// Adds a middleware instance that will be activated whenever this toolkit's container is
    /// expanded by the LLM. The instance is merged with any middlewares declared on the toolkit
    /// class via <c>[Collapse(Middlewares = [...])]</c>, with DI-provided instances appended after
    /// attribute-declared ones.
    /// </summary>
    /// <param name="middleware">Middleware instance to activate on toolkit expansion.</param>
    /// <returns>This <see cref="ToolkitOptions"/> for chaining.</returns>
    public ToolkitOptions AddScopedMiddleware(Middleware.IAgentMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        ScopedMiddlewares.Add(middleware);
        return this;
    }
}
