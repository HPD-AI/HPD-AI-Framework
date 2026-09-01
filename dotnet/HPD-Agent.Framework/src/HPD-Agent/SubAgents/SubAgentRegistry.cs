using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>Identifies one child using vocabulary local to its controlling parent thread.</summary>
public readonly record struct SubAgentLocalId
{
    /// <summary>Creates a validated parent-local child identifier.</summary>
    [JsonConstructor]
    public SubAgentLocalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>Gets the opaque model-facing value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Describes whether a registered child can currently be controlled.</summary>
public enum SubAgentChildAvailability
{
    /// <summary>The registry maps the local identifier to a usable child thread.</summary>
    Available,
    /// <summary>A parent fork deliberately retained only an explanatory tombstone.</summary>
    Detached,
    /// <summary>The underlying child was deleted.</summary>
    Deleted,
    /// <summary>The child exists but cannot currently be reconstructed or controlled.</summary>
    Unavailable
}

/// <summary>Describes the concrete topology selected when a child was created.</summary>
public enum SubAgentCreationContext
{
    /// <summary>The child starts from a coherent fork of parent history.</summary>
    Fork,
    /// <summary>The child is empty and shares its parent's session.</summary>
    Fresh,
    /// <summary>The child is empty in a dedicated session.</summary>
    Isolated
}

/// <summary>One canonical entry in a parent thread's durable child registry.</summary>
public sealed record SubAgentChildReference
{
    /// <summary>Gets the stable parent-local identifier.</summary>
    public required SubAgentLocalId LocalId { get; init; }
    /// <summary>Gets the declared role discriminator.</summary>
    public required string RoleName { get; init; }
    /// <summary>Gets the capability that created the child.</summary>
    public required CapabilityId CapabilityId { get; init; }
    /// <summary>Gets the child's durable agent definition identifier.</summary>
    public required string ChildAgentId { get; init; }
    /// <summary>Gets the exact executable child route.</summary>
    public required ThreadKey ChildThread { get; init; }
    /// <summary>Gets the resolved creation topology.</summary>
    public required SubAgentCreationContext CreationContext { get; init; }
    /// <summary>Gets the semantic invocation that created the child.</summary>
    public required string CreationInvocationId { get; init; }
    /// <summary>Gets the parent tool-call idempotency key.</summary>
    public required string ParentToolCallId { get; init; }
    /// <summary>Gets the complete durable child execution policy.</summary>
    public required SubAgentExecutionPolicy ExecutionPolicy { get; init; }
    /// <summary>Gets the durable creation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Closed registry projection branch for either executable authority or a tombstone.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(SubAgentAvailableChild), "available")]
[JsonDerivedType(typeof(SubAgentChildTombstone), "tombstone")]
public abstract record SubAgentRegistryEntry
{
    /// <summary>Gets the identifier local to the controlling parent.</summary>
    public abstract SubAgentLocalId LocalId { get; init; }
    /// <summary>Gets the declared role name.</summary>
    public abstract string RoleName { get; init; }
    /// <summary>Gets the projected availability.</summary>
    public abstract SubAgentChildAvailability Availability { get; init; }
}

/// <summary>Executable registry entry containing the complete durable child authority.</summary>
public sealed record SubAgentAvailableChild : SubAgentRegistryEntry
{
    /// <summary>Gets the complete executable child reference.</summary>
    public required SubAgentChildReference Child { get; init; }
    /// <inheritdoc />
    public override SubAgentLocalId LocalId { get => Child.LocalId; init { } }
    /// <inheritdoc />
    public override string RoleName { get => Child.RoleName; init { } }
    /// <inheritdoc />
    public override SubAgentChildAvailability Availability
    {
        get => SubAgentChildAvailability.Available;
        init { }
    }
}

/// <summary>Non-executable registry entry retaining only bounded model-facing identity and reason.</summary>
public sealed record SubAgentChildTombstone : SubAgentRegistryEntry
{
    /// <inheritdoc />
    public override required SubAgentLocalId LocalId { get; init; }
    /// <inheritdoc />
    public override required string RoleName { get; init; }
    /// <inheritdoc />
    public override required SubAgentChildAvailability Availability { get; init; }
    /// <summary>Gets the bounded reason execution is unavailable.</summary>
    public required string Reason { get; init; }
    /// <summary>Gets the original durable child creation time for presentation.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets the optional safe policy correlation without executable policy contents.</summary>
    public string? ExecutionPolicyFingerprint { get; init; }
}

/// <summary>Immutable projection of a parent thread's child registry at one journal cursor.</summary>
public sealed record SubAgentChildRegistryProjection(
    ThreadKey Parent,
    ThreadJournalCursor Cursor,
    IReadOnlyDictionary<SubAgentLocalId, SubAgentRegistryEntry> Entries,
    IReadOnlySet<SubAgentLocalId> ControllerGrants)
{
    /// <summary>Gets only executable child references from the closed registry projection.</summary>
    public IReadOnlyDictionary<SubAgentLocalId, SubAgentChildReference> AvailableChildren =>
        Entries.Values.OfType<SubAgentAvailableChild>()
            .ToDictionary(static entry => entry.LocalId, static entry => entry.Child);

    /// <summary>Resolves one executable local identifier without inspecting another route.</summary>
    public bool TryGetAvailable(SubAgentLocalId localId, out SubAgentChildReference child)
    {
        if (Entries.TryGetValue(localId, out var entry) && entry is SubAgentAvailableChild available)
        {
            child = available.Child;
            return true;
        }
        child = null!;
        return false;
    }
}

/// <summary>Durably registers one child route under its parent-local identifier.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CHILD_REGISTERED")]
public sealed record SubAgentChildRegisteredEvent(SubAgentChildReference Child) : AgentEvent;

/// <summary>Replaces an available child route with an explanatory detached tombstone.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CHILD_DETACHED")]
public sealed record SubAgentChildDetachedEvent(SubAgentLocalId LocalId, string Reason) : AgentEvent;

/// <summary>Remaps an existing local identifier to a prepared forked child route.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CHILD_REMAPPED")]
public sealed record SubAgentChildRemappedEvent(SubAgentLocalId LocalId, ThreadKey ChildThread) : AgentEvent;

/// <summary>Marks a child route unavailable while preserving its local identity.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CHILD_UNAVAILABLE")]
public sealed record SubAgentChildUnavailableEvent(
    SubAgentLocalId LocalId,
    SubAgentChildAvailability Availability,
    string Reason) : AgentEvent;

/// <summary>Seeds the registry and durable creation replay receipts during destructive journal compaction.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_REGISTRY_SEED")]
public sealed record SubAgentRegistrySeedEvent(
    IReadOnlyList<SubAgentRegistryEntry> Entries,
    IReadOnlyList<SubAgentCreationRecord> PendingCreations,
    IReadOnlyList<SubAgentLocalId>? ControllerGrants = null) : AgentEvent;

/// <summary>Durably grants one parent authority to control an explicitly shared child route.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CONTROLLER_GRANTED")]
public sealed record SubAgentControllerGrantedEvent(
    SubAgentLocalId LocalId,
    ThreadKey ChildThread) : AgentEvent;

/// <summary>Dedicated child-keyed authority-journal entry granting or revoking one exact parent controller.</summary>
/// <param name="Controller">The parent route receiving or losing authority.</param>
/// <param name="LocalId">The child identifier in the controller's registry.</param>
/// <param name="ForkOperationId">The topology operation that admitted the authority.</param>
/// <param name="ForkOperationSource">The root journal containing that operation.</param>
/// <param name="Revoked">Whether this entry is a revocation tombstone.</param>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CHILD_CONTROLLER_AUTHORITY")]
public sealed record SubAgentChildControllerAuthorityEvent(
    ThreadKey Controller,
    SubAgentLocalId LocalId,
    string ForkOperationId,
    ThreadKey ForkOperationSource,
    bool Revoked) : AgentEvent;

/// <summary>Conditional child-keyed authority for shared-subagent control.</summary>
public static class SubAgentControllerAuthority
{
    /// <summary>Idempotently grants one parent control through the owning fork operation.</summary>
    /// <param name="store">The durable session store.</param>
    /// <param name="child">The shared child route.</param>
    /// <param name="controller">The parent route receiving authority.</param>
    /// <param name="localId">The child identifier local to the controller.</param>
    /// <param name="forkOperationId">The admitting fork operation identifier.</param>
    /// <param name="forkOperationSource">The root journal containing the operation.</param>
    /// <param name="cancellationToken">Cancels the conditional write.</param>
    /// <returns>A task that completes after the conditional authority append commits.</returns>
    /// <exception cref="InvalidOperationException">The child is missing or conditional admission cannot converge.</exception>
    public static async ValueTask GrantAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey controller,
        SubAgentLocalId localId,
        string forkOperationId,
        ThreadKey forkOperationSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (await store.GetThreadEventHeadAsync(child, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException("subagent_shared_child_missing");
        var authorityThread = GetAuthorityThread(child);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var head = await store.GetThreadEventHeadAsync(authorityThread, cancellationToken).ConfigureAwait(false);
            if (head is not null)
                await ValidateAuthorityThreadAsync(store, authorityThread, child, head, cancellationToken).ConfigureAwait(false);
            var existing = head is null ? null : await ReadLatestAsync(
                store, authorityThread, controller, localId, head, cancellationToken).ConfigureAwait(false);
            if (existing is { Revoked: false } &&
                string.Equals(existing.ForkOperationId, forkOperationId, StringComparison.Ordinal))
                return;
            try
            {
                await store.AppendThreadEventsAsync(
                    authorityThread,
                    CreateAuthorityAppend(authorityThread, child, head, new SubAgentChildControllerAuthorityEvent(
                        controller, localId, forkOperationId, forkOperationSource, Revoked: false)
                    {
                        SessionId = authorityThread.SessionId,
                        ThreadId = authorityThread.ThreadId
                    }),
                    new ThreadAppendCondition(head?.Cursor ?? ThreadJournalCursor.Start(1)),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_controller_grant_conflict");
    }

    /// <summary>Idempotently revokes one exact child/controller grant with a durable tombstone.</summary>
    /// <param name="store">The durable session store.</param>
    /// <param name="child">The shared child route.</param>
    /// <param name="controller">The parent route losing authority.</param>
    /// <param name="localId">The child identifier local to the controller.</param>
    /// <param name="forkOperationId">The admitting fork operation identifier.</param>
    /// <param name="forkOperationSource">The root journal containing the operation.</param>
    /// <param name="cancellationToken">Cancels the conditional write.</param>
    /// <returns>A task that completes after the conditional tombstone append commits.</returns>
    /// <exception cref="InvalidOperationException">The child is missing or conditional admission cannot converge.</exception>
    public static async ValueTask RevokeAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey controller,
        SubAgentLocalId localId,
        string forkOperationId,
        ThreadKey forkOperationSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (await store.GetThreadEventHeadAsync(child, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException("subagent_shared_child_missing");
        var authorityThread = GetAuthorityThread(child);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var head = await store.GetThreadEventHeadAsync(authorityThread, cancellationToken).ConfigureAwait(false);
            if (head is not null)
                await ValidateAuthorityThreadAsync(store, authorityThread, child, head, cancellationToken).ConfigureAwait(false);
            var existing = head is null ? null : await ReadLatestAsync(
                store, authorityThread, controller, localId, head, cancellationToken).ConfigureAwait(false);
            if (existing is { Revoked: true }) return;
            try
            {
                await store.AppendThreadEventsAsync(
                    authorityThread,
                    CreateAuthorityAppend(authorityThread, child, head, new SubAgentChildControllerAuthorityEvent(
                        controller, localId, forkOperationId, forkOperationSource, Revoked: true)
                    {
                        SessionId = authorityThread.SessionId,
                        ThreadId = authorityThread.ThreadId
                    }),
                    new ThreadAppendCondition(head?.Cursor ?? ThreadJournalCursor.Start(1)),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_controller_revoke_conflict");
    }

    /// <summary>Checks the latest exact child/controller grant and its committed fork authority.</summary>
    /// <param name="store">The durable session store.</param>
    /// <param name="child">The shared child route.</param>
    /// <param name="controller">The candidate controlling parent.</param>
    /// <param name="localId">The child identifier local to the controller.</param>
    /// <param name="cancellationToken">Cancels the authority projection.</param>
    /// <returns><see langword="true"/> only when a live grant and committed matching topology outcome exist.</returns>
    public static async ValueTask<bool> IsGrantedAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey controller,
        SubAgentLocalId localId,
        CancellationToken cancellationToken = default)
    {
        var authorityThread = GetAuthorityThread(child);
        var head = await store.GetThreadEventHeadAsync(authorityThread, cancellationToken).ConfigureAwait(false);
        if (head is null) return false;
        await ValidateAuthorityThreadAsync(store, authorityThread, child, head, cancellationToken).ConfigureAwait(false);
        var grant = await ReadLatestAsync(store, authorityThread, controller, localId, head, cancellationToken)
            .ConfigureAwait(false);
        if (grant is null || grant.Revoked) return false;
        var operation = await new JournalThreadForkOperationStore(store, grant.ForkOperationSource)
            .GetThreadForkOperationAsync(grant.ForkOperationId, cancellationToken).ConfigureAwait(false);
        return operation is
            {
                Status: ThreadForkOperationStatus.Committed or ThreadForkOperationStatus.ReconciliationRequired
            } &&
            operation.ChildOutcomes.Any(outcome =>
                string.Equals(outcome.LocalId, localId.Value, StringComparison.Ordinal) &&
                outcome.Policy == SubAgentForkPolicy.Share &&
                outcome.Target == child &&
                outcome.Controller == controller &&
                outcome.Availability == SubAgentChildAvailability.Available);
    }

    private static async ValueTask<SubAgentChildControllerAuthorityEvent?> ReadLatestAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey controller,
        SubAgentLocalId localId,
        ThreadEventHead head,
        CancellationToken cancellationToken)
    {
        SubAgentChildControllerAuthorityEvent? latest = null;
        await foreach (var batch in store.ReadThreadEventsAsync(
                           child,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                           cancellationToken).ConfigureAwait(false))
            foreach (var evt in batch.Events)
                if (evt is SubAgentChildControllerAuthorityEvent authority &&
                    authority.Controller == controller && authority.LocalId == localId)
                    latest = authority;
        return latest;
    }

    private static ThreadKey GetAuthorityThread(ThreadKey child)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{child.SessionId}\u001f{child.ThreadId}"))).ToLowerInvariant();
        return new ThreadKey(child.SessionId, $"__hpd/subagent-controller-authority/{digest}");
    }

    private static async ValueTask ValidateAuthorityThreadAsync(
        ISessionStore store,
        ThreadKey authorityThread,
        ThreadKey child,
        ThreadEventHead head,
        CancellationToken cancellationToken)
    {
        ThreadCreatedEvent? created = null;
        await foreach (var batch in store.ReadThreadEventsAsync(
                           authorityThread,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber),
                           cancellationToken).ConfigureAwait(false))
        {
            created = batch.Events.OfType<ThreadCreatedEvent>().FirstOrDefault();
            if (created is not null) break;
        }
        if (created is null || created.ThreadKind != ThreadKind.FrameworkInternal ||
            !string.Equals(created.DefaultAgentId, "hpd.subagent-controller-authority", StringComparison.Ordinal) ||
            created.ThreadMetadata is null ||
            !created.ThreadMetadata.TryGetValue("childSessionId", out var sessionValue) ||
            !created.ThreadMetadata.TryGetValue("childThreadId", out var threadValue) ||
            !string.Equals(sessionValue?.ToString(), child.SessionId, StringComparison.Ordinal) ||
            !string.Equals(threadValue?.ToString(), child.ThreadId, StringComparison.Ordinal))
            throw new InvalidOperationException("subagent_controller_authority_route_collision");
    }

    private static IReadOnlyList<AgentEvent> CreateAuthorityAppend(
        ThreadKey authorityThread,
        ThreadKey child,
        ThreadEventHead? head,
        SubAgentChildControllerAuthorityEvent authority)
    {
        if (head is not null) return [authority];
        return
        [
            new ThreadCreatedEvent(
                "hpd.subagent-controller-authority", null, null, null,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["childSessionId"] = child.SessionId,
                    ["childThreadId"] = child.ThreadId
                },
                DateTime.UtcNow,
                ThreadKind.FrameworkInternal,
                ThreadVisibility.Hidden)
            {
                SessionId = authorityThread.SessionId,
                ThreadId = authorityThread.ThreadId
            },
            authority
        ];
    }
}

/// <summary>Projects and conditionally mutates a parent-local registry using its canonical thread journal.</summary>
public sealed class SubAgentChildRegistry
{
    private readonly ISessionStore _store;

    internal ISessionStore Store => _store;

    /// <summary>Creates a registry over the supplied durable session store.</summary>
    public SubAgentChildRegistry(ISessionStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Projects registry state through the exact requested cursor.</summary>
    public async ValueTask<SubAgentChildRegistryProjection> ProjectAsync(
        ThreadKey parent,
        ThreadJournalCursor? through = null,
        CancellationToken cancellationToken = default)
    {
        var head = await _store.GetThreadEventHeadAsync(parent, cancellationToken).ConfigureAwait(false);
        if (head is null)
            throw new InvalidOperationException($"Parent thread '{parent.ThreadId}' does not exist.");
        var limit = through ?? head.Cursor;
        if (limit.Generation != head.Generation || limit.SequenceNumber > head.ThreadSequenceNumber)
            throw new ThreadCursorConflictException(parent, limit, head.Cursor);

        var entries = new Dictionary<SubAgentLocalId, SubAgentRegistryEntry>();
        var grants = new HashSet<SubAgentLocalId>();
        await foreach (var batch in _store.ReadThreadEventsAsync(
            parent,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(limit.Generation), limit.SequenceNumber),
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
            {
                Apply(entries, evt);
                if (evt is SubAgentRegistrySeedEvent seed)
                {
                    grants.Clear();
                    grants.UnionWith(seed.ControllerGrants ?? []);
                }
                else if (evt is SubAgentControllerGrantedEvent grant)
                    grants.Add(grant.LocalId);
            }
        }
        return new(parent, limit, entries, grants);
    }

    /// <summary>Registers one child idempotently by parent tool call and capability.</summary>
    public async ValueTask<SubAgentChildReference> RegisterAsync(
        ThreadKey parent,
        SubAgentChildReference child,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.ExecutionPolicy.Validate();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var projection = await ProjectAsync(parent, cancellationToken: cancellationToken).ConfigureAwait(false);
            var replay = projection.Entries.Values.OfType<SubAgentAvailableChild>()
                .Select(static entry => entry.Child).FirstOrDefault(existing =>
                string.Equals(existing.ParentToolCallId, child.ParentToolCallId, StringComparison.Ordinal) &&
                existing.CapabilityId == child.CapabilityId);
            if (replay is not null) return replay;
            if (projection.Entries.ContainsKey(child.LocalId))
                throw new InvalidOperationException("subagent_creation_conflict");

            var scoped = new SubAgentChildRegisteredEvent(child)
            {
                SessionId = parent.SessionId,
                ThreadId = parent.ThreadId
            };
            try
            {
                await _store.AppendThreadEventsAsync(
                    parent,
                    [scoped],
                    new ThreadAppendCondition(projection.Cursor),
                    cancellationToken).ConfigureAwait(false);
                return child;
            }
            catch (ThreadAppendConflictException) when (attempt < 7) { }
        }
        throw new InvalidOperationException("subagent_creation_conflict");
    }

    private static void Apply(
        Dictionary<SubAgentLocalId, SubAgentRegistryEntry> entries,
        AgentEvent evt)
    {
        switch (evt)
        {
            case SubAgentRegistrySeedEvent seed:
                entries.Clear();
                foreach (var entry in seed.Entries)
                {
                    if (entry is SubAgentAvailableChild available)
                        available.Child.ExecutionPolicy.Validate();
                    else if (entry is SubAgentChildTombstone { Availability: SubAgentChildAvailability.Available })
                        throw new InvalidOperationException("subagent_registry_entry_invalid");
                    entries[entry.LocalId] = entry;
                }
                break;
            case SubAgentChildRegisteredEvent registered:
                registered.Child.ExecutionPolicy.Validate();
                entries[registered.Child.LocalId] = new SubAgentAvailableChild { Child = registered.Child };
                break;
            case SubAgentChildDetachedEvent detached when entries.TryGetValue(detached.LocalId, out var detachedEntry):
                entries[detached.LocalId] = new SubAgentChildTombstone
                {
                    LocalId = detached.LocalId,
                    RoleName = detachedEntry.RoleName,
                    Availability = SubAgentChildAvailability.Detached,
                    Reason = detached.Reason,
                    CreatedAt = detachedEntry is SubAgentAvailableChild detachedAvailable
                        ? detachedAvailable.Child.CreatedAt
                        : ((SubAgentChildTombstone)detachedEntry).CreatedAt,
                    ExecutionPolicyFingerprint = detachedEntry is SubAgentAvailableChild detachedExecutable
                        ? detachedExecutable.Child.ExecutionPolicy.Fingerprint
                        : (detachedEntry as SubAgentChildTombstone)?.ExecutionPolicyFingerprint
                };
                break;
            case SubAgentChildRemappedEvent remapped when entries.TryGetValue(remapped.LocalId, out var remapEntry) &&
                                                             remapEntry is SubAgentAvailableChild remapAvailable:
                entries[remapped.LocalId] = new SubAgentAvailableChild
                {
                    Child = remapAvailable.Child with { ChildThread = remapped.ChildThread }
                };
                break;
            case SubAgentChildUnavailableEvent unavailable when entries.TryGetValue(unavailable.LocalId, out var unavailableEntry):
                if (unavailable.Availability == SubAgentChildAvailability.Available)
                    throw new InvalidOperationException("subagent_registry_entry_invalid");
                entries[unavailable.LocalId] = new SubAgentChildTombstone
                {
                    LocalId = unavailable.LocalId,
                    RoleName = unavailableEntry.RoleName,
                    Availability = unavailable.Availability,
                    Reason = unavailable.Reason,
                    CreatedAt = unavailableEntry is SubAgentAvailableChild unavailableAvailable
                        ? unavailableAvailable.Child.CreatedAt
                        : ((SubAgentChildTombstone)unavailableEntry).CreatedAt,
                    ExecutionPolicyFingerprint = unavailableEntry is SubAgentAvailableChild executable
                        ? executable.Child.ExecutionPolicy.Fingerprint
                        : (unavailableEntry as SubAgentChildTombstone)?.ExecutionPolicyFingerprint
                };
                break;
        }
    }
}

/// <summary>Preserves the latest parent-local child registry through destructive compaction.</summary>
public sealed class SubAgentRegistryRebaseSeedProvider(SubAgentChildRegistry registry)
    : IThreadJournalRebaseSeedProvider
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentEvent>> CreateSeedEventsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken = default)
    {
        var projection = await registry.ProjectAsync(thread, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var creations = await CollectCreationsAsync(thread, cancellationToken).ConfigureAwait(false);
        if (projection.Entries.Count == 0 && creations.Count == 0) return Array.Empty<AgentEvent>();
        return
        [
            new SubAgentRegistrySeedEvent(
                projection.Entries.Values
                    .OrderBy(static entry => entry.LocalId.Value, StringComparer.Ordinal)
                    .ToArray(),
                creations,
                projection.ControllerGrants.OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray())
            {
                SessionId = thread.SessionId,
                ThreadId = thread.ThreadId
            }
        ];
    }

    private async ValueTask<IReadOnlyList<SubAgentCreationRecord>> CollectCreationsAsync(
        ThreadKey thread,
        CancellationToken cancellationToken)
    {
        var records = new List<SubAgentCreationRecord>();
        await foreach (var record in new JournalSubAgentCreationStore(registry.Store)
            .ReadSubAgentCreationsAsync(thread, cancellationToken).ConfigureAwait(false))
            records.Add(record);
        return records
            .OrderBy(static record => record.LocalId.Value, StringComparer.Ordinal)
            .ToArray();
    }
}
