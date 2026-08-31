namespace HPD.Agent;

/// <summary>Identifies one child using vocabulary local to its controlling parent thread.</summary>
public readonly record struct SubAgentLocalId
{
    /// <summary>Creates a validated parent-local child identifier.</summary>
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
    /// <summary>Gets the current registry availability.</summary>
    public required SubAgentChildAvailability Availability { get; init; }
    /// <summary>Gets the exact child route when one is available.</summary>
    public ThreadKey? ChildThread { get; init; }
    /// <summary>Gets the resolved creation topology.</summary>
    public required SubAgentCreationContext CreationContext { get; init; }
    /// <summary>Gets the semantic invocation that created the child.</summary>
    public required string CreationInvocationId { get; init; }
    /// <summary>Gets the parent tool-call idempotency key.</summary>
    public required string ParentToolCallId { get; init; }
    /// <summary>Gets the durable creation time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets a bounded explanation when the route is unavailable.</summary>
    public string? UnavailableReason { get; init; }
}

/// <summary>Immutable projection of a parent thread's child registry at one journal cursor.</summary>
public sealed record SubAgentChildRegistryProjection(
    ThreadKey Parent,
    ThreadJournalCursor Cursor,
    IReadOnlyDictionary<SubAgentLocalId, SubAgentChildReference> Children,
    IReadOnlySet<SubAgentLocalId> ControllerGrants)
{
    /// <summary>Resolves one local identifier without inspecting any other session or thread.</summary>
    public bool TryGet(SubAgentLocalId localId, out SubAgentChildReference child) =>
        Children.TryGetValue(localId, out child!);
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
    IReadOnlyList<SubAgentChildReference> Children,
    IReadOnlyList<SubAgentCreationRecord> PendingCreations,
    IReadOnlyList<SubAgentLocalId>? ControllerGrants = null) : AgentEvent;

/// <summary>Durably grants one parent authority to control an explicitly shared child route.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("SUBAGENT_CONTROLLER_GRANTED")]
public sealed record SubAgentControllerGrantedEvent(
    SubAgentLocalId LocalId,
    ThreadKey ChildThread) : AgentEvent;

/// <summary>Child-journal authority granting or revoking one exact parent controller.</summary>
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
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var head = await store.GetThreadEventHeadAsync(child, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("subagent_shared_child_missing");
            var existing = await ReadLatestAsync(store, child, controller, localId, head, cancellationToken)
                .ConfigureAwait(false);
            if (existing is { Revoked: false } &&
                string.Equals(existing.ForkOperationId, forkOperationId, StringComparison.Ordinal))
                return;
            try
            {
                await store.AppendThreadEventsAsync(
                    child,
                    [new SubAgentChildControllerAuthorityEvent(
                        controller, localId, forkOperationId, forkOperationSource, Revoked: false)
                    {
                        SessionId = child.SessionId,
                        ThreadId = child.ThreadId
                    }],
                    new ThreadAppendCondition(head.Cursor),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ThreadAppendConflictException) when (attempt < 15) { }
        }
        throw new InvalidOperationException("subagent_controller_grant_conflict");
    }

    /// <summary>Checks the latest exact child/controller grant and its committed fork authority.</summary>
    public static async ValueTask<bool> IsGrantedAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey controller,
        SubAgentLocalId localId,
        CancellationToken cancellationToken = default)
    {
        var head = await store.GetThreadEventHeadAsync(child, cancellationToken).ConfigureAwait(false);
        if (head is null) return false;
        var grant = await ReadLatestAsync(store, child, controller, localId, head, cancellationToken)
            .ConfigureAwait(false);
        if (grant is null || grant.Revoked) return false;
        var operation = await new JournalThreadForkOperationStore(store, grant.ForkOperationSource)
            .GetThreadForkOperationAsync(grant.ForkOperationId, cancellationToken).ConfigureAwait(false);
        return operation?.Status is ThreadForkOperationStatus.Committed or
            ThreadForkOperationStatus.ReconciliationRequired;
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

        var children = new Dictionary<SubAgentLocalId, SubAgentChildReference>();
        var grants = new HashSet<SubAgentLocalId>();
        await foreach (var batch in _store.ReadThreadEventsAsync(
            parent,
            new ThreadEventReadRequest(ThreadJournalCursor.Start(limit.Generation), limit.SequenceNumber),
            cancellationToken).ConfigureAwait(false))
        {
            foreach (var evt in batch.Events)
            {
                Apply(children, evt);
                if (evt is SubAgentRegistrySeedEvent seed)
                {
                    grants.Clear();
                    grants.UnionWith(seed.ControllerGrants ?? []);
                }
                else if (evt is SubAgentControllerGrantedEvent grant)
                    grants.Add(grant.LocalId);
            }
        }
        return new(parent, limit, children, grants);
    }

    /// <summary>Registers one child idempotently by parent tool call and capability.</summary>
    public async ValueTask<SubAgentChildReference> RegisterAsync(
        ThreadKey parent,
        SubAgentChildReference child,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(child);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var projection = await ProjectAsync(parent, cancellationToken: cancellationToken).ConfigureAwait(false);
            var replay = projection.Children.Values.FirstOrDefault(existing =>
                string.Equals(existing.ParentToolCallId, child.ParentToolCallId, StringComparison.Ordinal) &&
                existing.CapabilityId == child.CapabilityId);
            if (replay is not null) return replay;
            if (projection.Children.ContainsKey(child.LocalId))
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
        Dictionary<SubAgentLocalId, SubAgentChildReference> children,
        AgentEvent evt)
    {
        switch (evt)
        {
            case SubAgentRegistrySeedEvent seed:
                children.Clear();
                foreach (var child in seed.Children) children[child.LocalId] = child;
                break;
            case SubAgentChildRegisteredEvent registered:
                children[registered.Child.LocalId] = registered.Child;
                break;
            case SubAgentChildDetachedEvent detached when children.TryGetValue(detached.LocalId, out var child):
                children[detached.LocalId] = child with
                {
                    Availability = SubAgentChildAvailability.Detached,
                    ChildThread = null,
                    UnavailableReason = detached.Reason
                };
                break;
            case SubAgentChildRemappedEvent remapped when children.TryGetValue(remapped.LocalId, out var child):
                children[remapped.LocalId] = child with
                {
                    Availability = SubAgentChildAvailability.Available,
                    ChildThread = remapped.ChildThread,
                    UnavailableReason = null
                };
                break;
            case SubAgentChildUnavailableEvent unavailable when children.TryGetValue(unavailable.LocalId, out var child):
                children[unavailable.LocalId] = child with
                {
                    Availability = unavailable.Availability,
                    ChildThread = unavailable.Availability == SubAgentChildAvailability.Available
                        ? child.ChildThread
                        : null,
                    UnavailableReason = unavailable.Reason
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
        if (projection.Children.Count == 0 && creations.Count == 0) return Array.Empty<AgentEvent>();
        return
        [
            new SubAgentRegistrySeedEvent(
                projection.Children.Values
                    .OrderBy(static child => child.LocalId.Value, StringComparer.Ordinal)
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
