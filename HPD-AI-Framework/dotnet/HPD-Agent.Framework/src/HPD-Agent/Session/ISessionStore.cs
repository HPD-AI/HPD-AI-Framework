using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace HPD.Agent;

/// <summary>
/// Interface for persisting and loading session and thread state.
/// Thread events are the durable execution and transcript source of truth.
/// </summary>
/// <remarks>
/// <para><b>V3 Changes:</b></para>
/// <list type="bullet">
/// <item>Session methods now work with Session (metadata only, no messages)</item>
/// <item>New thread methods for managing conversation threads</item>
/// <item>Thread runs and recovery are projected from thread events</item>
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
    /// <para>Messages are stored as thread events and projected into Thread objects by LoadThreadAsync.</para>
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
    /// <para>Messages are persisted as thread events and projected into Thread objects on load.</para>
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
    /// This deletes the session metadata and all threads.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Behavior:</b> Deletes session + all threads. Content cleanup is handled by IContentStore policy.</para>
    /// </remarks>
    Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════
    // THREAD PERSISTENCE ( New - conversation paths)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load a thread (conversation path) from persistent storage.
    /// Returns null if thread doesn't exist.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Threads contain messages and thread-scoped middleware state.</para>
    /// </remarks>
    Task<Thread?> LoadThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the event-sourced thread document from persistent storage.
    /// Returns null if the thread does not exist.
    /// </summary>
    Task<ThreadEventDocument?> LoadThreadDocumentAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Append a thread event to the thread's durable event stream.
    /// Implementations assign the event sequence number before persisting.
    /// </summary>
    Task AppendThreadEventAsync(
        string sessionId,
        string threadId,
        AgentEvent evt,
        long? expectedSequenceNumber = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read thread events for deterministic replay.
    /// </summary>
    IAsyncEnumerable<AgentEvent> ReadThreadEventsAsync(
        string sessionId,
        string threadId,
        HPD.Events.ReplayReadOptions options,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(cancellationToken);

        async IAsyncEnumerable<AgentEvent> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var document = await LoadThreadDocumentAsync(sessionId, threadId, ct).ConfigureAwait(false);
            if (document is null)
                yield break;

            await foreach (var evt in document.Events.FilterByReplayOptions(options, ct).ConfigureAwait(false))
                yield return evt;
        }
    }

    /// <summary>
    /// List all thread IDs for a session.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Enables UI to show all conversation variants.</para>
    /// </remarks>
    Task<List<string>> ListThreadIdsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific thread from a session.
    /// Does not delete the session itself or other threads.
    /// </summary>
    /// <remarks>
    /// <para><b>V3 Addition:</b> Allows cleanup of unwanted conversation paths.</para>
    /// </remarks>
    Task DeleteThreadAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════
    // CLEANUP
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Delete sessions inactive longer than the threshold.
    /// </summary>
    Task<int> DeleteInactiveSessionsAsync(
        TimeSpan inactivityThreshold,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
