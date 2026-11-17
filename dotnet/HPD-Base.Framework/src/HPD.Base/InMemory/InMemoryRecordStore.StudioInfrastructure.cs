using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore : IStudioInfrastructureInventoryStore
{
    private readonly List<StudioInfrastructureItem> _studioInfrastructureInventory = [];
    private int _studioInfrastructureInitialized;

    /// <inheritdoc />
    public StudioInfrastructureInventoryCapability InfrastructureInventoryCapability { get; } =
        StudioInfrastructureInventoryContract.Capability(durable: false);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseCapturedStudioInfrastructureAuthority>> CaptureInfrastructureAuthorityAsync(
        StudioInfrastructureInventoryRequirement request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StudioInfrastructureInventoryContract.Valid(request, InfrastructureInventoryCapability) ||
            !StringComparer.Ordinal.Equals(request.StoreId, _options.StoreId) ||
            !StringComparer.Ordinal.Equals(request.StoreInstanceId, _options.StoreId) || request.RestoreEpoch != 0 || request.SchemaGeneration != 1)
            return InfrastructureFailure<BaseCapturedStudioInfrastructureAuthority>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.AcquisitionDeadline);
        await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            EnsureInfrastructureSeed(); long generation = _studioInfrastructureInventory.Count == 0 ? 0 : _studioInfrastructureInventory.Max(static value => value.Sequence);
            string path = StudioInfrastructureInventoryContract.Path(request.Kind);
            var accounting = new StudioInfrastructureProviderAccounting { RowsRead = 1, EvidenceBytes = 64, TransientBytes = 64 };
            var receipt = new StudioInfrastructureCaptureReceipt { ApplicationId = new(request.ApplicationId.AsSpan()), StoreId = new(_options.StoreId.AsSpan()),
                Kind = request.Kind, StoreInstanceId = new(_options.StoreId.AsSpan()), RestoreEpoch = 0, SchemaGeneration = 1,
                InventoryGeneration = generation, LogicalAccessPathId = path, Accounting = accounting,
                AuthorityChecksum = StudioInfrastructureInventoryContract.AuthorityChecksum(request, generation, path) };
            return OperationResults.Ok<BaseCapturedStudioInfrastructureAuthority>(new InfrastructureAuthority(this, request with
            { ApplicationId = new(request.ApplicationId.AsSpan()), StoreId = new(request.StoreId.AsSpan()), StoreInstanceId = new(request.StoreInstanceId.AsSpan()), Limits = request.Limits with { } }, receipt));
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<IStudioInfrastructureInventorySession>> OpenInfrastructureSessionAsync(
        BaseCapturedStudioInfrastructureAuthority authority, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not InfrastructureAuthority captured || !ReferenceEquals(captured.Owner, this) || !captured.TryOpen())
            return ValueTask.FromResult(InfrastructureFailure<IStudioInfrastructureInventorySession>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization));
        return ValueTask.FromResult(OperationResults.Ok<IStudioInfrastructureInventorySession>(new InfrastructureSession(this, captured)));
    }

    private void EnsureInfrastructureSeed()
    {
        if (Interlocked.Exchange(ref _studioInfrastructureInitialized, 1) != 0) return;
        var value = new StudioSchemaGenerationItem { Kind = StudioInfrastructureInventoryKind.SchemaGeneration, Sequence = 1,
            StoreId = new(_options.StoreId.AsSpan()), RestoreEpoch = 0, SchemaGeneration = 1, ObservedAtUtc = DateTimeOffset.UnixEpoch,
            State = StudioInfrastructureState.Completed, BaselineId = "inmemory.v1", SchemaChecksum = StudioInfrastructureInventoryContract.Hash(static writer => writer.Write("inmemory.v1")),
            DriftDetected = false, Checksum = [] };
        _studioInfrastructureInventory.Add(value with { Checksum = StudioInfrastructureInventoryContract.ItemChecksum(value) });
    }

    private sealed class InfrastructureAuthority(InMemoryRecordStore owner, StudioInfrastructureInventoryRequirement requirement,
        StudioInfrastructureCaptureReceipt receipt) : BaseCapturedStudioInfrastructureAuthority(receipt)
    {
        private int _opened;
        internal InMemoryRecordStore Owner { get; } = owner;
        internal StudioInfrastructureInventoryRequirement Requirement { get; } = requirement;
        internal bool TryOpen() => Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
    }

    private sealed class InfrastructureSession(InMemoryRecordStore owner, InfrastructureAuthority authority) : IStudioInfrastructureInventorySession
    {
        private int _disposed; private readonly CancellationTokenSource _lifetime = new(authority.Requirement.Limits.SessionDeadline);
        public async ValueTask<OperationResult<StudioInfrastructurePage>> ReadPageAsync(StudioInfrastructurePageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Volatile.Read(ref _disposed) != 0 || request.Take < 1 || request.Take > authority.Requirement.Limits.MaximumItems ||
                !StudioInfrastructureInventoryContract.Position(authority.Requirement.Kind, request.After, out long after))
                return InfrastructureFailure<StudioInfrastructurePage>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); deadline.CancelAfter(authority.Requirement.Limits.PageDeadline);
            await owner._stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                owner.EnsureInfrastructureSeed(); StudioInfrastructureItem[] rows = owner._studioInfrastructureInventory
                    .Where(value => value.Kind == authority.Requirement.Kind && value.Sequence > after && value.Sequence <= authority.Receipt.InventoryGeneration)
                    .OrderBy(static value => value.Sequence).Take(checked(request.Take + 1)).ToArray();
                long rowsRead = rows.LongLength; if (rowsRead > authority.Requirement.Limits.MaximumRowsRead)
                    return InfrastructureFailure<StudioInfrastructurePage>("base.studio.infrastructure.budgetExceeded", ErrorCategory.Validation);
                bool more = rows.Length > request.Take; if (more) rows = rows[..request.Take]; ImmutableArray<StudioInfrastructureItem> items = [.. rows];
                long bytes = items.Sum(StudioInfrastructureInventoryContract.Measure);
                if (bytes > authority.Requirement.Limits.MaximumEvidenceBytes || bytes > authority.Requirement.Limits.MaximumTransientBytes)
                    return InfrastructureFailure<StudioInfrastructurePage>("base.studio.infrastructure.budgetExceeded", ErrorCategory.Validation);
                StudioInfrastructureBoundary? next = more && items.Length > 0 ? StudioInfrastructureInventoryContract.Boundary(authority.Requirement.Kind, items[^1].Sequence) : null;
                var accounting = new StudioInfrastructureProviderAccounting { RowsRead = rowsRead, EvidenceBytes = bytes, TransientBytes = bytes };
                var page = new StudioInfrastructurePage { Items = items, Next = next, InventoryGeneration = authority.Receipt.InventoryGeneration,
                    Accounting = accounting, PageChecksum = StudioInfrastructureInventoryContract.PageChecksum(items, authority.Receipt.InventoryGeneration, next, accounting) };
                return OperationResults.Ok(page);
            }
            finally { owner._stateGate.Release(); }
        }
        public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _lifetime.Dispose(); return ValueTask.CompletedTask; }
    }

    private static BaseError InfrastructureError(string code, ErrorCategory category) => new()
    { Code = code, Message = "The infrastructure inventory operation could not be completed.", Category = category };
    private static OperationResult<T> InfrastructureFailure<T>(string code, ErrorCategory category) => category switch
    { ErrorCategory.Authorization => OperationResults.PolicyDenied<T>(InfrastructureError(code, category)), _ => OperationResults.ValidationFailed<T>(InfrastructureError(code, category)) };
}
