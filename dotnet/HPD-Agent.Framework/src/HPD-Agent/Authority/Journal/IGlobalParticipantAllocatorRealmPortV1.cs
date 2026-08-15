using System.Buffers.Binary;

namespace HPD.Agent.Authority;

internal sealed record GlobalParticipantAllocatorRealmManifestV1
{
    private static ReadOnlySpan<byte> Domain => "hpd-s1-gpa-realm-manifest-v1\0"u8;

    internal GlobalParticipantAllocatorRealmManifestV1(GlobalParticipantAllocatorJournalId journalId, ushort formatVersion, ulong fenceEpoch, Hash256 storeIdentity, UtcInstant createdAt, Hash256 manifestHash)
    {
        Span<byte> scratch = stackalloc byte[32];
        if (!journalId.IsValid || formatVersion != 1 || fenceEpoch is 0 or ulong.MaxValue || !storeIdentity.TryWriteBytes(scratch) || !manifestHash.TryWriteBytes(scratch))
            throw new ArgumentException("A valid realm manifest is required.");
        var expected = ComputeManifestHash(journalId, formatVersion, fenceEpoch, storeIdentity, createdAt);
        if (manifestHash != expected) throw new ArgumentException("The manifest hash does not authenticate its canonical fields.", nameof(manifestHash));
        JournalId = journalId; FormatVersion = formatVersion; FenceEpoch = fenceEpoch; StoreIdentity = storeIdentity; CreatedAt = createdAt; ManifestHash = manifestHash;
    }

    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal ushort FormatVersion { get; }
    internal ulong FenceEpoch { get; }
    internal Hash256 StoreIdentity { get; }
    internal UtcInstant CreatedAt { get; }
    internal Hash256 ManifestHash { get; }

    internal static Hash256 ComputeManifestHash(GlobalParticipantAllocatorJournalId journalId, ushort formatVersion, ulong fenceEpoch, Hash256 storeIdentity, UtcInstant createdAt)
    {
        Span<byte> preimage = stackalloc byte[95];
        Domain.CopyTo(preimage);
        if (!journalId.TryWriteBytes(preimage[29..45]) || !storeIdentity.TryWriteBytes(preimage[55..87])) throw new ArgumentException("Valid manifest identities are required.");
        BinaryPrimitives.WriteUInt16BigEndian(preimage[45..47], formatVersion);
        BinaryPrimitives.WriteUInt64BigEndian(preimage[47..55], fenceEpoch);
        BinaryPrimitives.WriteInt64BigEndian(preimage[87..95], createdAt.NanosecondsSinceUnixEpoch);
        return Hash256.Compute(preimage);
    }
}

internal interface IGlobalParticipantAllocatorDurableCustodyV1 : IAsyncDisposable;

internal sealed class GlobalParticipantAllocatorRealmLeaseV1 : IAsyncDisposable
{
    private readonly object _gate = new();
    private IAsyncDisposable? _custody;
    private Task? _disposeTask;
    private TaskCompletionSource<bool>? _drained;
    private int _activeUses;
    private bool _disposeStarted;

    internal GlobalParticipantAllocatorRealmLeaseV1(GlobalParticipantAllocatorRealmManifestV1 manifest, IGlobalParticipantAllocatorDurableCustodyV1 custody)
    { Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest)); Custody = custody ?? throw new ArgumentNullException(nameof(custody)); _custody = custody; }
    internal GlobalParticipantAllocatorRealmManifestV1 Manifest { get; }
    internal IGlobalParticipantAllocatorDurableCustodyV1 Custody { get; }

    internal bool TryAcquireUse(out LeaseUse use)
    {
        lock (_gate)
        {
            if (_disposeStarted) { use = null!; return false; }
            _activeUses++; use = new LeaseUse(this); return true;
        }
    }

    private void ReleaseUse()
    {
        lock (_gate)
        {
            if (--_activeUses == 0) _drained?.TrySetResult(true);
        }
    }

    internal ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposeStarted = true;
            _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_activeUses == 0) _drained.TrySetResult(true);
            _disposeTask = DisposeCoreAsync(_drained.Task);
            return new ValueTask(_disposeTask);
        }
    }

    ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();

    private async Task DisposeCoreAsync(Task drained)
    {
        await drained.ConfigureAwait(false);
        var custody = Interlocked.Exchange(ref _custody, null);
        if (custody is not null) await custody.DisposeAsync().ConfigureAwait(false);
    }

    internal sealed class LeaseUse : IDisposable, IAsyncDisposable
    {
        private GlobalParticipantAllocatorRealmLeaseV1? _owner;
        internal LeaseUse(GlobalParticipantAllocatorRealmLeaseV1 owner) => _owner = owner;
        internal void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseUse();
        internal ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        void IDisposable.Dispose() => Dispose();
        ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsync();
    }
}

internal sealed record GlobalParticipantAllocatorStoreBindingV1
{
    internal GlobalParticipantAllocatorStoreBindingV1(Hash256 storeIdentity)
    { Span<byte> bytes = stackalloc byte[32]; if (!storeIdentity.TryWriteBytes(bytes)) throw new ArgumentException("A valid store identity is required.", nameof(storeIdentity)); StoreIdentity = storeIdentity; }
    internal Hash256 StoreIdentity { get; }
}

internal sealed record GlobalParticipantAllocatorRealmCreateRequestV1
{
    internal GlobalParticipantAllocatorRealmCreateRequestV1(GlobalParticipantAllocatorJournalId journalId, GlobalParticipantAllocatorStoreBindingV1 storeBinding)
    { if (!journalId.IsValid) throw new ArgumentException("A valid journal ID is required.", nameof(journalId)); JournalId = journalId; StoreBinding = storeBinding ?? throw new ArgumentException("A store binding is required.", nameof(storeBinding)); }
    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal GlobalParticipantAllocatorStoreBindingV1 StoreBinding { get; }
}

internal sealed record GlobalParticipantAllocatorRealmOpenRequestV1
{
    internal GlobalParticipantAllocatorRealmOpenRequestV1(GlobalParticipantAllocatorJournalId journalId, GlobalParticipantAllocatorStoreBindingV1 storeBinding)
    { if (!journalId.IsValid) throw new ArgumentException("A valid journal ID is required.", nameof(journalId)); JournalId = journalId; StoreBinding = storeBinding ?? throw new ArgumentException("A store binding is required.", nameof(storeBinding)); }
    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal GlobalParticipantAllocatorStoreBindingV1 StoreBinding { get; }
}

internal abstract record GlobalParticipantAllocatorRealmCreateResultV1
{
    private GlobalParticipantAllocatorRealmCreateResultV1() { }
    internal sealed record Created(GlobalParticipantAllocatorRealmLeaseV1 RealmLease) : GlobalParticipantAllocatorRealmCreateResultV1;
    internal sealed record AlreadyExists(GlobalParticipantAllocatorRealmManifestV1 Manifest) : GlobalParticipantAllocatorRealmCreateResultV1;
    internal sealed record Incompatible(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmCreateResultV1;
    internal sealed record RootConflict(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmCreateResultV1;
    internal sealed record Unsupported(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmCreateResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmCreateResultV1;
    internal sealed record OutcomeUnknown(GlobalParticipantAllocatorJournalId JournalId, BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmCreateResultV1;
}

internal abstract record GlobalParticipantAllocatorRealmOpenResultV1
{
    private GlobalParticipantAllocatorRealmOpenResultV1() { }
    internal sealed record Opened(GlobalParticipantAllocatorRealmLeaseV1 RealmLease) : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record NotFound : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record Incompatible(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record RootConflict(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record Unsupported(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record OutcomeUnknown(GlobalParticipantAllocatorJournalId JournalId, BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmOpenResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GlobalParticipantAllocatorRealmOpenResultV1;
}

internal interface IGlobalParticipantAllocatorRealmPortV1
{
    ValueTask<GlobalParticipantAllocatorRealmCreateResultV1> CreateAsync(GlobalParticipantAllocatorRealmCreateRequestV1 request, CancellationToken cancellationToken);
    ValueTask<GlobalParticipantAllocatorRealmOpenResultV1> OpenAsync(GlobalParticipantAllocatorRealmOpenRequestV1 request, CancellationToken cancellationToken);
}
