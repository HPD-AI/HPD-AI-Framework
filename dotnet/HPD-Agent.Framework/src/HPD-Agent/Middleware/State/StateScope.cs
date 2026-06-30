// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

namespace HPD.Agent;

/// <summary>
/// Defines the scope of middleware persistent state.
/// Determines whether state is shared across all threads (Session) or per-thread (Thread).
/// </summary>
/// <remarks>
/// <para><b>Design Philosophy:</b></para>
/// <para>
/// Not all middleware state belongs in the same place. This enum enables middleware authors
/// to declare whether their persistent state is about the *user/environment* (Session-scoped)
/// or about the *conversation path* (Thread-scoped).
/// </para>
///
/// <para><b>Session-Scoped State (Shared Across Threads):</b></para>
/// <list type="bullet">
/// <item><b>PermissionPersistentState:</b> "Always Allow Bash" applies everywhere, not just one thread</item>
/// <item><b>User Preferences:</b> Theme, language, display settings</item>
/// <item><b>Environment State:</b> Working directory, environment variables</item>
/// </list>
///
/// <para><b>Thread-Scoped State (Per-Conversation Path):</b></para>
/// <list type="bullet">
/// <item><b>PlanModePersistentState:</b> Different threads explore different plans</item>
/// <item><b>CompactionState:</b> Each thread has different messages → different summarization cache</item>
/// <item><b>Conversation Context:</b> Any state derived from the specific message sequence</item>
/// </list>
///
/// <para><b>On Fork Behavior:</b></para>
/// <para>
/// When a thread is forked:
/// - <b>Session-scoped state:</b> SHARED (all threads read from the same Session.MiddlewareState)
/// - <b>Thread-scoped state:</b> COPIED (new thread gets a copy, then diverges independently)
/// </para>
///
/// <para><b>Example Usage:</b></para>
/// <code>
/// // Session-scoped: Permissions apply everywhere
/// [MiddlewareState(Persistent = true, Scope = StateScope.Session)]
/// public sealed record PermissionPersistentStateData { }
///
/// // Thread-scoped (default): Plan progress is per-conversation
/// [MiddlewareState(Persistent = true)]  // Scope = StateScope.Thread is the default
/// public sealed record PlanModePersistentStateData { }
/// </code>
/// </remarks>
public enum StateScope
{
    /// <summary>
    /// Thread-scoped state (default).
    /// State tied to a specific conversation path.
    /// Each thread has its own copy.
    /// On fork: state is COPIED from source thread.
    /// After fork: threads diverge independently.
    /// </summary>
    /// <remarks>
    /// <para><b>Use for:</b></para>
    /// <list type="bullet">
    /// <item>Plan progress (different threads explore different approaches)</item>
    /// <item>Conversation summarization cache (different messages per thread)</item>
    /// <item>Any state derived from the specific message sequence</item>
    /// </list>
    /// </remarks>
    Thread = 0,

    /// <summary>
    /// Session-scoped state.
    /// State shared across all threads.
    /// All threads read from the same Session.MiddlewareState.
    /// On fork: state is SHARED (not copied).
    /// Updates in one thread affect all threads.
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
    /// Session-scoped state affects ALL threads. Only use this for state
    /// that genuinely applies to the entire session, not individual conversations.
    /// </para>
    /// </remarks>
    Session = 1
}
