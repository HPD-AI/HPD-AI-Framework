using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal sealed record SkillDescriptor
{
    public required CapabilityId Id { get; init; }
    public required string ModelName { get; init; }
    public required string Description { get; init; }
    public required SkillInstructionProvider Instructions { get; init; }
    public SkillInstructionProvider? Reinforcement { get; init; }
    public required IReadOnlyList<CapabilityId> Children { get; init; }
    public SkillActivationLifetime Lifetime { get; init; } = SkillActivationLifetime.MessageTurn;
}

internal sealed record SkillCatalogSnapshot
{
    public required long Epoch { get; init; }
    public required CapabilityGraph Graph { get; init; }
    public required ImmutableArray<AIFunction> Functions { get; init; }
    public required ImmutableDictionary<CapabilityId, SkillDescriptor> Skills { get; init; }
}

internal interface ISkillCatalog
{
    SkillCatalogLease Acquire();
    ValueTask<SkillReloadResult> ReloadAsync(SkillReloadRequest request, CancellationToken cancellationToken = default);
}

internal sealed record SkillReloadRequest(string Reason = "manual");

/// <summary>Reports whether a complete replacement skill catalog was published.</summary>
public sealed record SkillReloadResult(
    bool Published,
    long Epoch,
    string? Error = null,
    IReadOnlyList<string>? ChangedSkillIds = null);

internal sealed class SkillCatalogLease : IDisposable
{
    private SkillCatalog.SnapshotOwner? _owner;

    internal SkillCatalogLease(SkillCatalog.SnapshotOwner owner)
    {
        _owner = owner;
        Snapshot = owner.Snapshot;
    }

    public SkillCatalogSnapshot Snapshot { get; }

    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
}

internal sealed class SkillCatalog : ISkillCatalog, IDisposable
{
    private readonly Func<long, CancellationToken, ValueTask<SkillCatalogSnapshot>> _rebuild;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private SnapshotOwner _current;
    private int _disposed;

    public SkillCatalog(
        SkillCatalogSnapshot initial,
        Func<long, CancellationToken, ValueTask<SkillCatalogSnapshot>> rebuild)
    {
        Validate(initial);
        _current = new SnapshotOwner(initial);
        _rebuild = rebuild ?? throw new ArgumentNullException(nameof(rebuild));
    }

    public SkillCatalogLease Acquire()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        while (true)
        {
            var owner = Volatile.Read(ref _current);
            if (owner.TryAcquire())
                return new SkillCatalogLease(owner);
        }
    }

    public async ValueTask<SkillReloadResult> ReloadAsync(
        SkillReloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var epoch = Volatile.Read(ref _current).Snapshot.Epoch + 1;
            SkillCatalogSnapshot candidate;
            try
            {
                candidate = await _rebuild(epoch, cancellationToken).ConfigureAwait(false);
                Validate(candidate);
                if (candidate.Epoch != epoch)
                    throw new InvalidOperationException($"Reload produced epoch {candidate.Epoch}; expected {epoch}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var error = exception.Message.Replace('\r', ' ').Replace('\n', ' ');
                if (error.Length > 512)
                    error = error[..512];
                return new SkillReloadResult(false, epoch - 1, error);
            }

            var previous = Volatile.Read(ref _current).Snapshot;
            var changedSkillIds = GetChangedSkillIds(previous, candidate);
            var replacement = new SnapshotOwner(candidate);
            var retired = Interlocked.Exchange(ref _current, replacement);
            retired.Release();
            return new SkillReloadResult(true, epoch, ChangedSkillIds: changedSkillIds);
        }
        finally { _reloadLock.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            Volatile.Read(ref _current).Release();
        _reloadLock.Dispose();
    }

    private static void Validate(SkillCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Epoch < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Catalog epoch cannot be negative.");
        if (snapshot.Functions.Length != snapshot.Graph.Nodes.Count)
            throw new InvalidOperationException("Catalog functions and graph nodes must describe the same snapshot.");
        foreach (var descriptor in snapshot.Skills.Values)
        {
            if (!snapshot.Graph.Nodes.ContainsKey(descriptor.Id))
                throw new InvalidOperationException($"Skill descriptor '{descriptor.Id}' has no activation node.");
        }
    }

    private static IReadOnlyList<string> GetChangedSkillIds(
        SkillCatalogSnapshot previous,
        SkillCatalogSnapshot candidate)
    {
        var ids = previous.Skills.Keys.Union(candidate.Skills.Keys).Distinct();
        return ids.Where(id =>
            !previous.Skills.TryGetValue(id, out var before) ||
            !candidate.Skills.TryGetValue(id, out var after) ||
            before.ModelName != after.ModelName ||
            before.Description != after.Description ||
            before.Lifetime != after.Lifetime ||
            !before.Children.SequenceEqual(after.Children))
            .Select(id => id.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    internal sealed class SnapshotOwner
    {
        private int _references = 1;

        public SnapshotOwner(SkillCatalogSnapshot snapshot) => Snapshot = snapshot;

        public SkillCatalogSnapshot Snapshot { get; }

        public bool TryAcquire()
        {
            while (true)
            {
                var observed = Volatile.Read(ref _references);
                if (observed == 0)
                    return false;
                if (Interlocked.CompareExchange(ref _references, observed + 1, observed) == observed)
                    return true;
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _references) < 0)
                throw new InvalidOperationException("Catalog snapshot lease was released more than once.");
        }
    }
}
