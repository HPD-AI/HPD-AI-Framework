using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace HPD.Agent;

/// <summary>
/// Interface for persisting and loading session and branch state.
/// V3 Architecture: Supports session metadata, branches, and crash recovery.
/// </summary>
/// <remarks>
/// <para><b>V3 Changes:</b></para>
/// <list type="bullet">
/// <item>Session methods now work with Session (metadata only, no messages)</item>
/// <item>New branch methods for managing conversation branches</item>
/// <item>UncommittedTurn remains session-scoped (contains BranchId internally)</item>
/// </list>
/// </remarks>
public interface ISessionStore
{
    // ═══════════════════════════════════════════════════════════════════
    // SESSION PERSISTENCE ( Metadata only, no messages)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load session metadata from persistent storage by ID.
    /// Returns null if session doesn't exist.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Change:</b> Returns Session (metadata) instead of the former monolithic session type.</para>
    /// <para>Messages are stored as branch events and projected into Branch objects by LoadBranchAsync.</para>
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
    /// <para>Messages are persisted as branch events and projected into Branch objects on load.</para>
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
    /// This deletes the session metadata, all branches, and uncommitted turn.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Behavior:</b> Deletes session + all branches. Content cleanup is handled by IContentStore policy.</para>
    /// </remarks>
    Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════
    // BRANCH PERSISTENCE ( New - conversation paths)
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
    /// Load the event-sourced branch document from persistent storage.
    /// Returns null if the branch does not exist.
    /// </summary>
    Task<BranchEventDocument?> LoadBranchDocumentAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        return LoadBranchAsync(sessionId, branchId, cancellationToken)
            .ContinueWith(
                task => task.Result is null
                    ? null
                    : BranchEventDocumentBuilder.FromBranchSnapshot(sessionId, task.Result),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    /// <summary>
    /// Save the event-sourced branch document to persistent storage.
    /// Implementations may use <paramref name="expectedSequenceNumber"/> for optimistic concurrency.
    /// </summary>
    Task SaveBranchDocumentAsync(
        BranchEventDocument document,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        throw new NotSupportedException(
            $"{GetType().Name} must implement event-sourced branch document persistence.");
    }

    /// <summary>
    /// Append a branch event to the branch's durable event stream.
    /// Implementations assign the event sequence number before persisting.
    /// </summary>
    Task AppendBranchEventAsync(
        string sessionId,
        string branchId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return AppendAsync();

        async Task AppendAsync()
        {
            var document = await LoadBranchDocumentAsync(sessionId, branchId, cancellationToken).ConfigureAwait(false)
                ?? new BranchEventDocument { SessionId = sessionId, BranchId = branchId };

            evt = BranchEventValidation.PrepareForAppend(sessionId, branchId, evt);
            evt.SequenceNumber = document.NextSequenceNumber;
            document = document with
            {
                UpdatedAt = evt.Timestamp,
                NextSequenceNumber = document.NextSequenceNumber + 1,
                Events = [.. document.Events, evt]
            };

            await SaveBranchDocumentAsync(document, expectedSequenceNumber, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Read branch events for deterministic replay.
    /// </summary>
    IAsyncEnumerable<AgentEvent> ReadBranchEventsAsync(
        string sessionId,
        string branchId,
        HPD.Events.ReplayReadOptions options,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(cancellationToken);

        async IAsyncEnumerable<AgentEvent> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var document = await LoadBranchDocumentAsync(sessionId, branchId, ct).ConfigureAwait(false);
            if (document is null)
                yield break;

            await foreach (var evt in document.Events.FilterByReplayOptions(options, ct).ConfigureAwait(false))
                yield return evt;
        }
    }

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
