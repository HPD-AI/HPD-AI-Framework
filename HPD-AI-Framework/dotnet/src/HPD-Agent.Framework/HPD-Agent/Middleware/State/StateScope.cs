// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent;

/// <summary>
/// Defines the scope of middleware persistent state.
/// Determines whether state is shared across all branches (Session) or per-branch (Branch).
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// Not all middleware state belongs in the same place. This enum enables middleware authors
/// to declare whether their persistent state is about the *user/environment* (Session-scoped)
/// or about the *conversation path* (Branch-scoped).
/// </para>
///
/// <para><b>Session-Scoped State (Shared Across Branches):</b></para>
/// <list type="bullet">
/// <item><b>PermissionPersistentState:</b> "Always Allow Bash" applies everywhere, not just one branch</item>
/// <item><b>User Preferences:</b> Theme, language, display settings</item>
/// <item><b>Environment State:</b> Working directory, environment variables</item>
/// </list>
///
/// <para><b>Branch-Scoped State (Per-Conversation Path):</b></para>
/// <list type="bullet">
/// <item><b>PlanModePersistentState:</b> Different branches explore different plans</item>
/// <item><b>HistoryReductionState:</b> Each branch has different messages → different summarization cache</item>
/// <item><b>Conversation Context:</b> Any state derived from the specific message sequence</item>
/// </list>
///
/// <para><b>On Fork Behavior:</b></para>
/// <para>
/// When a branch is forked:
/// - <b>Session-scoped state:</b> SHARED (all branches read from the same Session.MiddlewareState)
/// - <b>Branch-scoped state:</b> COPIED (new branch gets a copy, then diverges independently)
/// </para>
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
/// </code>
/// </remarks>
public enum StateScope
{
    /// <summary>
    /// Branch-scoped state (default).
    /// State tied to a specific conversation path.
    /// Each branch has its own copy.
    /// On fork: state is COPIED from source branch.
    /// After fork: branches diverge independently.
    /// </summary>
    /// <remarks>
    /// <para><b>Use for:</b></para>
    /// <list type="bullet">
    /// <item>Plan progress (different branches explore different approaches)</item>
    /// <item>Conversation summarization cache (different messages per branch)</item>
    /// <item>Any state derived from the specific message sequence</item>
    /// </list>
    /// </remarks>
    Branch = 0,

    /// <summary>
    /// Session-scoped state.
    /// State shared across all branches.
    /// All branches read from the same Session.MiddlewareState.
    /// On fork: state is SHARED (not copied).
    /// Updates in one branch affect all branches.
    /// </summary>
    /// <remarks>
    /// <para><b>Use for:</b></para>
    /// <list type="bullet">
    /// <item>Permission choices ("Always Allow Bash" applies everywhere)</item>
    /// <item>User preferences (theme, language, etc.)</item>
    /// <item>Environment state (working directory, env vars)</item>
    /// </list>
    ///
    /// <para><b>Warning:</b></para>
    /// <para>
    /// Session-scoped state affects ALL branches. Only use this for state
    /// that genuinely applies to the entire session, not individual conversations.
    /// </para>
    /// </remarks>
    Session = 1
}
