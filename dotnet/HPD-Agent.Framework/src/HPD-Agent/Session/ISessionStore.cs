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
    /// <para>Messages are stored as thread events and reconstructed through <see cref="ThreadProjectionReader"/>.</para>
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
    /// Atomically append events to the thread's canonical journal.
    /// Implementations return new committed values with authoritative thread positions.
    /// </summary>
    ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(
        ThreadKey thread,
        IReadOnlyList<AgentEvent> events,
        ThreadAppendCondition condition = default,
        CancellationToken cancellationToken = default);

    /// <summary>Read a lightweight thread descriptor without projecting its journal.</summary>
    ValueTask<ThreadDescriptor?> GetThreadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);

    /// <summary>List lightweight thread descriptors without projecting journal history.</summary>
    IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(
        string sessionId,
        ThreadListRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Read the current committed journal head without decoding events.</summary>
    ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default);

    /// <summary>Read canonical events in bounded, contiguous sequence batches.</summary>
    IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(
        ThreadKey thread,
        ThreadEventReadRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Catch up from a cursor and then observe future committed journal events.</summary>
    IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(
        ThreadKey thread,
        long after,
        ThreadObservationOptions options,
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
