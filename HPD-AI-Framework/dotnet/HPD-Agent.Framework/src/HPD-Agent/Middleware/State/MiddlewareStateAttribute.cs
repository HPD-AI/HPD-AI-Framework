// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System;

namespace HPD.Agent;

/// <summary>
/// Marks a record as middleware state, triggering source generation
/// of properties on MiddlewareState.
/// </summary>
/// <remarks>
/// <para><b>Requirements:</b></para>
/// <list type="bullet">
/// <item>Must be applied to a record type (not class)</item>
/// <item>Record should be sealed for performance</item>
/// <item>All members must be JSON-serializable</item>
/// </list>
///
/// <para><b>Example:</b></para>
/// <code>
/// [MiddlewareState(Version = 1)]
/// public sealed record MyMiddlewareState
/// {
///     public int Count { get; init; }
/// }
/// </code>
///
/// <para><b>Versioning:</b></para>
/// <para>
/// Increment the version when making breaking changes to the state schema:
/// </para>
/// <list type="bullet">
/// <item>Removing or renaming properties</item>
/// <item>Changing property types</item>
/// <item>Changing collection types (e.g., List → ImmutableList)</item>
/// </list>
/// <para>
/// Non-breaking changes (no version bump needed):
/// </para>
/// <list type="bullet">
/// <item>Adding new optional properties with default values</item>
/// <item>Adding helper methods</item>
/// <item>Updating documentation</item>
/// </list>
///
/// <para><b>Generated Code:</b></para>
/// <para>
/// The source generator will create a property on MiddlewareState:
/// </para>
/// <code>
/// public sealed partial class MiddlewareState
/// {
///     public MyMiddlewareState? MyMiddleware
///     {
///         get => GetState&lt;MyMiddlewareState&gt;("YourNamespace.MyMiddlewareState");
///     }
///
///     public MiddlewareState WithMyMiddleware(MyMiddlewareState? value)
///     {
///         return value == null ? this : SetState("YourNamespace.MyMiddlewareState", value);
///     }
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MiddlewareStateAttribute : Attribute
{
    /// <summary>
    /// Version of this middleware state schema. Defaults to 1.
    /// Increment when making breaking changes to the state record.
    /// </summary>
    /// <remarks>
    /// <para><b>When to Bump Version:</b></para>
    /// <list type="bullet">
    /// <item>Removing or renaming properties</item>
    /// <item>Changing property types</item>
    /// <item>Changing collection types (e.g., List → ImmutableList)</item>
    /// </list>
    ///
    /// <para><b>No Version Bump Needed:</b></para>
    /// <list type="bullet">
    /// <item>Adding new optional properties with default values</item>
    /// <item>Adding helper methods</item>
    /// <item>Updating documentation</item>
    /// </list>
    /// </remarks>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Whether this middleware state should persist across agent runs.
    /// Defaults to false (transient state).
    /// </summary>
    /// <remarks>
    /// <para><b>Persistent States:</b></para>
    /// <para>
    /// Use Persistent = true for state that should survive across runs:
    /// </para>
    /// <list type="bullet">
    /// <item>Expensive caches (e.g., HistoryReductionStateData - avoid re-summarization)</item>
    /// <item>User preferences (e.g., PermissionStateData - remember permission choices)</item>
    /// </list>
    ///
    /// <para><b>Transient States (Default):</b></para>
    /// <para>
    /// Most states are transient and reset on each agent run:
    /// </para>
    /// <list type="bullet">
    /// <item>Error tracking (ErrorTrackingStateData - per-run metric)</item>
    /// <item>Circuit breakers (CircuitBreakerStateData - safety must reset)</item>
    /// <item>Batch optimization (BatchPermissionStateData - per-iteration optimization)</item>
    /// <item>Per-run metrics (TotalErrorThresholdStateData - accumulator resets)</item>
    /// </list>
    ///
    /// <para><b>Why Transient by Default?</b></para>
    /// <para>
    /// Safety middlewares MUST reset on new runs. Persisting error counts
    /// or circuit breaker state would cause incorrect behavior on subsequent runs.
    /// Only opt-in to persistence when truly needed.
    /// </para>
    ///
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// // Persistent state (survives across runs)
    /// [MiddlewareState(Persistent = true)]
    /// public sealed record PermissionStateData { }
    ///
    /// // Transient state (resets each run - default)
    /// [MiddlewareState]
    /// public sealed record ErrorTrackingStateData { }
    /// </code>
    /// </remarks>
    public bool Persistent { get; set; } = false;

    /// <summary>
    /// The scope of this middleware state.
    /// Determines whether state is shared across all branches (Session) or per-branch (Branch).
    /// Defaults to Branch (per-conversation path).
    /// </summary>
    /// <remarks>
    /// <para><b>Session-Scoped (Shared Across Branches):</b></para>
    /// <para>
    /// Use Scope = StateScope.Session for state about the *user/environment*:
    /// </para>
    /// <list type="bullet">
    /// <item>Permissions: "Always Allow Bash" applies everywhere, not just one branch</item>
    /// <item>User preferences: Theme, language, display settings</item>
    /// <item>Environment state: Working directory, env vars</item>
    /// </list>
    ///
    /// <para><b>Branch-Scoped (Default - Per-Conversation):</b></para>
    /// <para>
    /// Most states are branch-scoped (tied to specific conversation path):
    /// </para>
    /// <list type="bullet">
    /// <item>Plan progress: Different branches explore different plans</item>
    /// <item>History cache: Each branch has different messages → different cache</item>
    /// <item>Conversation context: Any state derived from message sequence</item>
    /// </list>
    ///
    /// <para><b>On Fork Behavior:</b></para>
    /// <list type="bullet">
    /// <item><b>Session-scoped:</b> SHARED (all branches read from same Session.MiddlewareState)</item>
    /// <item><b>Branch-scoped:</b> COPIED (new branch gets copy, then diverges)</item>
    /// </list>
    ///
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// // Session-scoped: Permissions apply everywhere
    /// [MiddlewareState(Persistent = true, Scope = StateScope.Session)]
    /// public sealed record PermissionPersistentStateData { }
    ///
    /// // Branch-scoped (default): Plan progress is per-conversation
    /// [MiddlewareState(Persistent = true)]  // Scope = StateScope.Branch is the default
    /// public sealed record PlanModePersistentStateData { }
    ///
    /// // Transient branch-scoped (most common)
    /// [MiddlewareState]  // Persistent = false, Scope = Branch (both defaults)
    /// public sealed record ErrorTrackingStateData { }
    /// </code>
    /// </remarks>
    public StateScope Scope { get; set; } = StateScope.Branch;
}
