#nullable enable

namespace HPD.Environment.Runtime;

using HPD.Environment.Contracts;

internal readonly record struct ProviderResourceKey(
    Type ResourceType,
    ResourceScope Scope,
    string Id);

internal sealed record ProviderResourceShape(
    TargetKind TargetKind,
    TargetRouteSegmentKind SegmentKind,
    TargetHandleLifetime Lifetime,
    TargetHandleAuthority Authority,
    SchemaId HandleSchema);

internal sealed record ProviderResourceEntry<TResource, TSpec, TStatus>(
    ResourceRef<TResource> Resource,
    TSpec Spec,
    TStatus Status,
    TargetHandle<TResource> TargetHandle,
    ProviderOpaqueHandle ProviderHandle,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ulong ProviderGeneration)
    where TResource : IExecutionResourceMarker, IOperationTargetMarker
    where TSpec : notnull
    where TStatus : ResourceStatus;

internal readonly record struct ProviderLedgerLookup<TEntry>(
    TEntry? Entry,
    Diagnostic? Diagnostic)
    where TEntry : class
{
    public bool Succeeded => Entry is not null && Diagnostic is null;
}

internal sealed class BoundedAuditLedger<TKey, TEvent>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly int _maximumEventsPerKey;
    private readonly Dictionary<TKey, TEvent[]> _events = [];

    public BoundedAuditLedger(int maximumEventsPerKey)
    {
        if (maximumEventsPerKey is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(
                nameof(maximumEventsPerKey));
        _maximumEventsPerKey = maximumEventsPerKey;
    }

    public void Append(
        TKey key,
        IReadOnlyList<TEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return;
        lock (_gate)
        {
            _events.TryGetValue(key, out TEvent[]? existing);
            int total = (existing?.Length ?? 0) + events.Count;
            int keep = Math.Min(total, _maximumEventsPerKey);
            var updated = new TEvent[keep];
            int existingKept = Math.Min(
                existing?.Length ?? 0,
                Math.Max(0, keep - events.Count));
            if (existingKept > 0)
                Array.Copy(
                    existing!,
                    existing!.Length - existingKept,
                    updated,
                    0,
                    existingKept);
            int newKept = keep - existingKept;
            int newStart = events.Count - newKept;
            for (int index = 0; index < newKept; index++)
                updated[existingKept + index] =
                    events[newStart + index];
            _events[key] = updated;
        }
    }

    public TEvent[] Get(TKey key)
    {
        lock (_gate)
            return _events.TryGetValue(key, out TEvent[]? events)
                ? events.ToArray()
                : [];
    }

    public bool Remove(TKey key)
    {
        lock (_gate)
            return _events.Remove(key);
    }
}

internal sealed class ProviderGenerationFence
{
    private readonly object _gate = new();
    private ulong _generation;

    public ProviderGenerationFence(ulong initialGeneration = 1)
    {
        if (initialGeneration == 0)
            throw new ArgumentOutOfRangeException(
                nameof(initialGeneration));
        _generation = initialGeneration;
    }

    public ulong Current
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public ulong Advance()
    {
        lock (_gate)
            return checked(++_generation);
    }

    public bool IsCurrent(ulong generation) =>
        generation == Current;
}

/// <summary>
/// Owns the provider-local identity, handle, and generation mechanics shared by
/// physical environment providers. Provider-specific dependency graphs and
/// durable physical recovery remain outside this ledger.
/// </summary>
internal sealed class ProviderResourceLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<ProviderResourceKey, object> _entries = [];
    private readonly Dictionary<string, ProviderResourceKey> _handles =
        new(StringComparer.Ordinal);
    private long _tokenSequence;
    private readonly ProviderGenerationFence _providerGeneration;

    public ProviderResourceLedger(
        ProviderId providerId,
        ulong initialProviderGeneration = 1)
    {
        if (string.IsNullOrWhiteSpace(providerId.Value))
            throw new ArgumentException(
                "Provider identity must be present.",
                nameof(providerId));
        if (initialProviderGeneration == 0)
            throw new ArgumentOutOfRangeException(
                nameof(initialProviderGeneration));
        ProviderId = providerId;
        _providerGeneration =
            new ProviderGenerationFence(initialProviderGeneration);
    }

    public ProviderId ProviderId { get; }

    public ulong ProviderGeneration
    {
        get
        {
            lock (_gate)
                return _providerGeneration.Current;
        }
    }

    public ulong AdvanceProviderGeneration()
    {
        lock (_gate)
            return _providerGeneration.Advance();
    }

    public ProviderResourceEntry<TResource, TSpec, TStatus> Upsert<
        TResource,
        TSpec,
        TStatus>(
        ResourceMetadata<TResource> metadata,
        TSpec spec,
        TStatus status,
        ProviderResourceShape shape)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(shape);

        lock (_gate)
        {
            ProviderResourceKey key = Key(metadata.Id, metadata.Scope);
            ProviderResourceEntry<TResource, TSpec, TStatus>? previous =
                _entries.GetValueOrDefault(key) as
                    ProviderResourceEntry<TResource, TSpec, TStatus>;
            bool retainHandle =
                previous is not null &&
                previous.ProviderGeneration == _providerGeneration.Current &&
                previous.Resource.Generation == metadata.Generation;
            TargetHandle<TResource> targetHandle;
            ProviderOpaqueHandle providerHandle;
            DateTimeOffset createdAt;
            if (retainHandle)
            {
                targetHandle = previous!.TargetHandle;
                providerHandle = previous.ProviderHandle;
                createdAt = previous.CreatedAt;
            }
            else
            {
                if (previous is not null)
                    _handles.Remove(previous.ProviderHandle.Token);
                (targetHandle, providerHandle) =
                    CreateHandles(metadata, shape);
                createdAt = metadata.CreatedAt == default
                    ? DateTimeOffset.UtcNow
                    : metadata.CreatedAt;
            }

            var resource = new ResourceRef<TResource>(
                metadata.Id,
                metadata.Scope,
                metadata.Generation);
            var entry = new ProviderResourceEntry<TResource, TSpec, TStatus>(
                resource,
                spec,
                status,
                targetHandle,
                providerHandle,
                createdAt,
                metadata.UpdatedAt ?? DateTimeOffset.UtcNow,
                _providerGeneration.Current);
            _entries[key] = entry;
            _handles[providerHandle.Token] = key;
            return entry;
        }
    }

    public ProviderLedgerLookup<
        ProviderResourceEntry<TResource, TSpec, TStatus>> TryGet<
        TResource,
        TSpec,
        TStatus>(ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        lock (_gate)
        {
            ProviderResourceKey key = Key(resource.Id, resource.Scope);
            if (_entries.GetValueOrDefault(key) is not
                ProviderResourceEntry<TResource, TSpec, TStatus> entry)
            {
                return Failure<
                    ProviderResourceEntry<TResource, TSpec, TStatus>>(
                    "hpd.environment.provider-ledger.resource-unknown",
                    $"Resource '{resource.Id.Value}' is not owned by provider '{ProviderId.Value}'.");
            }

            Diagnostic? stale = ValidateCurrent(
                entry.ProviderGeneration,
                entry.Resource.Generation,
                resource.Generation,
                resource.Id.Value);
            return stale is null
                ? Success(entry)
                : new(entry, stale);
        }
    }

    public ProviderLedgerLookup<
        ProviderResourceEntry<TResource, TSpec, TStatus>> TryGet<
        TResource,
        TSpec,
        TStatus>(TargetHandle<TResource> handle)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        lock (_gate)
        {
            if (!_providerGeneration.IsCurrent(
                    handle.ProviderGeneration))
            {
                return Failure<
                    ProviderResourceEntry<TResource, TSpec, TStatus>>(
                    "hpd.environment.provider-ledger.handle-generation-stale",
                    "The target handle belongs to a stale provider generation.");
            }
            if (handle.Route.ProviderId != ProviderId ||
                handle.Route.ProviderHandle is not { } opaque ||
                opaque.ProviderId != ProviderId ||
                opaque.Generation != _providerGeneration.Current ||
                !_handles.TryGetValue(opaque.Token, out ProviderResourceKey key) ||
                key.ResourceType != typeof(TResource) ||
                _entries.GetValueOrDefault(key) is not
                    ProviderResourceEntry<TResource, TSpec, TStatus> entry)
            {
                return Failure<
                    ProviderResourceEntry<TResource, TSpec, TStatus>>(
                    "hpd.environment.provider-ledger.handle-unknown",
                    "The target handle is not owned by this provider.");
            }

            Diagnostic? stale = ValidateCurrent(
                entry.ProviderGeneration,
                entry.Resource.Generation,
                handle.Route.ProviderHandle?.Generation is > 0
                    ? entry.Resource.Generation
                    : null,
                entry.Resource.Id.Value);
            return stale is null
                ? Success(entry)
                : new(entry, stale);
        }
    }

    public IReadOnlyList<
        ProviderResourceEntry<TResource, TSpec, TStatus>> List<
        TResource,
        TSpec,
        TStatus>(ResourceScope? scope = null)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        lock (_gate)
        {
            return _entries
                .Where(pair =>
                    pair.Key.ResourceType == typeof(TResource) &&
                    (scope is null || pair.Key.Scope == scope.Value))
                .Select(static pair => pair.Value)
                .OfType<
                    ProviderResourceEntry<TResource, TSpec, TStatus>>()
                .ToArray();
        }
    }

    public bool Remove<TResource, TSpec, TStatus>(
        ResourceRef<TResource> resource)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
        where TSpec : notnull
        where TStatus : ResourceStatus
    {
        lock (_gate)
        {
            ProviderResourceKey key = Key(resource.Id, resource.Scope);
            if (_entries.GetValueOrDefault(key) is not
                ProviderResourceEntry<TResource, TSpec, TStatus> entry ||
                resource.Generation is { } generation &&
                generation != entry.Resource.Generation)
            {
                return false;
            }

            _entries.Remove(key);
            _handles.Remove(entry.ProviderHandle.Token);
            return true;
        }
    }

    private (
        TargetHandle<TResource> Target,
        ProviderOpaqueHandle Opaque) CreateHandles<TResource>(
        ResourceMetadata<TResource> metadata,
        ProviderResourceShape shape)
        where TResource : IExecutionResourceMarker, IOperationTargetMarker
    {
        string token =
            $"{ProviderId.Value}:{typeof(TResource).Name}:{++_tokenSequence:x16}";
        var opaque = new ProviderOpaqueHandle(
            ProviderId,
            token,
            shape.HandleSchema,
            _providerGeneration.Current);
        var route = new TargetRoute
        {
            Kind = shape.TargetKind,
            Scope = metadata.Scope,
            Segments =
            [
                new TargetRouteSegment(
                    shape.SegmentKind,
                    metadata.Id.Value),
            ],
            BackingResourceKind = metadata.Kind,
            BackingResourceId = metadata.Id.Value,
            ProviderId = ProviderId,
            ProviderHandle = opaque,
        };
        return (
            new TargetHandle<TResource>(
                route,
                shape.Lifetime,
                shape.Authority,
            _providerGeneration.Current),
            opaque);
    }

    private Diagnostic? ValidateCurrent(
        ulong entryProviderGeneration,
        ResourceGeneration? entryResourceGeneration,
        ResourceGeneration? requestedResourceGeneration,
        string resourceId)
    {
        if (!_providerGeneration.IsCurrent(
                entryProviderGeneration))
        {
            return Error(
                "hpd.environment.provider-ledger.provider-generation-stale",
                $"Resource '{resourceId}' belongs to provider generation '{entryProviderGeneration}', current generation is '{_providerGeneration.Current}'.");
        }
        if (requestedResourceGeneration is { } requested &&
            entryResourceGeneration != requested)
        {
            return Error(
                "hpd.environment.provider-ledger.resource-generation-stale",
                $"Resource '{resourceId}' generation '{requested.Value}' is stale.");
        }
        return null;
    }

    private ProviderResourceKey Key<TResource>(
        ResourceId<TResource> id,
        ResourceScope scope)
        where TResource : IExecutionResourceMarker =>
        new(typeof(TResource), scope, id.Value);

    private ProviderLedgerLookup<TEntry> Failure<TEntry>(
        string code,
        string message)
        where TEntry : class =>
        new(null, Error(code, message));

    private static ProviderLedgerLookup<TEntry> Success<TEntry>(TEntry entry)
        where TEntry : class =>
        new(entry, null);

    private Diagnostic Error(string code, string message) =>
        new()
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = ProviderId,
        };
}
