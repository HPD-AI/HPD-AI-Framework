using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace HPD.Agent;

/// <summary>
/// Interface for persisting and loading session and branch state.
/// V3 Architecture: Supports session metadata, branches, crash recovery, and asset storage.
/// </summary>
/// <remarks>
/// <para><b>V3 Changes:</b></para>
/// <list type="bullet">
/// <item>Session methods now work with Session (metadata only, no messages)</item>
/// <item>New branch methods for managing conversation branches</item>
/// <item>UncommittedTurn remains session-scoped (contains BranchId internally)</item>
/// <item>Assets remain session-scoped (shared across all branches)</item>
/// </list>
/// </remarks>
public interface ISessionStore
{
    // ═══════════════════════════════════════════════════════════════════
    // SESSION PERSISTENCE (V3: Metadata only, no messages)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load session metadata from persistent storage by ID.
    /// Returns null if session doesn't exist.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Change:</b> Returns Session (metadata) instead of the former monolithic session type.</para>
    /// <para>Messages are now in Branch objects - use LoadBranchAsync to get conversation data.</para>
    /// </remarks>
    Task<Session?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save session metadata to persistent storage.
    /// This persists metadata and session-scoped middleware state only.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Change:</b> Saves Session (metadata) instead of the former monolithic session type.</para>
    /// <para>Messages are in Branch objects - use SaveBranchAsync to persist conversation data.</para>
    /// </remarks>
    Task SaveSessionAsync(
        Session session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all session IDs in storage.
    /// </summary>
    Task<List<string>> ListSessionIdsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a session and all its data from persistent storage.
    /// This deletes the session metadata, all branches, uncommitted turn, and assets.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Behavior:</b> Deletes session + all branches + assets (full cleanup).</para>
    /// </remarks>
    Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════
    // BRANCH PERSISTENCE (V3: New - conversation paths)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load a branch (conversation path) from persistent storage.
    /// Returns null if branch doesn't exist.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Branches contain messages and branch-scoped middleware state.</para>
    /// </remarks>
    Task<Branch?> LoadBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a branch to persistent storage.
    /// This persists messages and branch-scoped middleware state.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Each branch is saved independently.</para>
    /// </remarks>
    Task SaveBranchAsync(
        string sessionId,
        Branch branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all branch IDs for a session.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Enables UI to show all conversation variants.</para>
    /// </remarks>
    Task<List<string>> ListBranchIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific branch from a session.
    /// Does not delete the session itself or other branches.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Allows cleanup of unwanted conversation paths.</para>
    /// </remarks>
    Task DeleteBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════
    // UNCOMMITTED TURN (Crash Recovery — one per session)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load the uncommitted turn for a session, if one exists.
    /// Returns null if no turn is in progress (session is idle).
    /// </summary>
    Task<UncommittedTurn?> LoadUncommittedTurnAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save (overwrite) the uncommitted turn for a session.
    /// Called after each tool batch completes (fire-and-forget from agent loop).
    /// </summary>
    Task SaveUncommittedTurnAsync(
        UncommittedTurn turn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the uncommitted turn for a session.
    /// Called when a message turn completes successfully.
    /// </summary>
    Task DeleteUncommittedTurnAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════
    // CONTENT STORAGE (Session-Scoped)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the content store for session-scoped binary content (uploads, artifacts).
    /// Returns null if this store doesn't support content storage.
    /// In V3, content is stored using IContentStore with scope=sessionId and folder tags.
    /// </summary>
    IContentStore? GetContentStore(string sessionId);

    // ═══════════════════════════════════════════════════════════════════
    // CLEANUP
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Delete sessions inactive longer than the threshold.
    /// Also cleans up any orphaned uncommitted turns.
    /// </summary>
    Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
