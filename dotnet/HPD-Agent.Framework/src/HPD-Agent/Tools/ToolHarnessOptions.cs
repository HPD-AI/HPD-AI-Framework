// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

using HPD.Agent.Middleware;

/// <summary>
/// Per-toolharness configuration and exceptional exact-type activation overrides.
/// </summary>
/// <remarks>
/// <para>
/// This is the exceptional exact-type activation override path.
/// Use it when your toolharness-scoped middleware requires constructor parameters
/// that cannot be expressed as a parameterless constructor in
/// <c>[Collapse(Middlewares = [typeof(T)])]</c>.
/// </para>
/// <example>
/// <code>
/// builder.WithToolHarness&lt;DatabaseToolHarness&gt;(opts =>
///     opts.OverrideMiddleware&lt;DbAuditMiddleware&gt;(context =&gt;
///         ToolHarnessMiddlewareActivation.ExecutionOwned(new DbAuditMiddleware(context.GetRequiredService&lt;IAuditLog&gt;()))));
/// </code>
/// </example>
/// </remarks>
public sealed class ToolHarnessOptions
{
    internal readonly Dictionary<Type, ToolHarnessMiddlewareFactory> MiddlewareOverrides = [];
    internal readonly List<ISkillSource> SkillSources = [];
    internal readonly List<StoredSkillSourceRegistration> StoredSkillSources = [];

    /// <summary>
    /// Replaces one already-declared middleware at its generated position for every input execution.
    /// </summary>
    /// <param name="factory">Per-execution factory returning an explicitly owned activation.</param>
    /// <returns>This <see cref="ToolHarnessOptions"/> for chaining.</returns>
    public ToolHarnessOptions OverrideMiddleware<TMiddleware>(ToolHarnessMiddlewareFactory factory)
        where TMiddleware : class, IToolHarnessMiddleware
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!MiddlewareOverrides.TryAdd(typeof(TMiddleware), factory))
            throw new InvalidOperationException($"Middleware '{typeof(TMiddleware)}' is already overridden.");
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
