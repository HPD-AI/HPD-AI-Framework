using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore : IBaseStudioInfrastructureInventoryStore
{
    private readonly List<BaseStudioInfrastructureItem> _studioInfrastructureInventory = [];
    private int _studioInfrastructureInitialized;

    /// <inheritdoc />
    public BaseStudioInfrastructureInventoryCapability InfrastructureInventoryCapability { get; } =
        BaseStudioInfrastructureInventoryContract.Capability(durable: false);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseCapturedStudioInfrastructureAuthority>> CaptureInfrastructureAuthorityAsync(
        BaseStudioInfrastructureInventoryRequirement request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BaseStudioInfrastructureInventoryContract.Valid(request, InfrastructureInventoryCapability) ||
            !StringComparer.Ordinal.Equals(request.StoreId, _options.StoreId) ||
            !StringComparer.Ordinal.Equals(request.StoreInstanceId, _options.StoreId) || request.RestoreEpoch != 0 || request.SchemaGeneration != 1)
            return InfrastructureFailure<BaseCapturedStudioInfrastructureAuthority>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.AcquisitionDeadline);
        await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            EnsureInfrastructureSeed(); long generation = _studioInfrastructureInventory.Count == 0 ? 0 : _studioInfrastructureInventory.Max(static value => value.Sequence);
            string path = BaseStudioInfrastructureInventoryContract.Path(request.Kind);
            var accounting = new BaseStudioInfrastructureProviderAccounting { RowsRead = 1, EvidenceBytes = 64, TransientBytes = 64 };
            var receipt = new BaseStudioInfrastructureCaptureReceipt { ApplicationId = new(request.ApplicationId.AsSpan()), StoreId = new(_options.StoreId.AsSpan()),
                Kind = request.Kind, StoreInstanceId = new(_options.StoreId.AsSpan()), RestoreEpoch = 0, SchemaGeneration = 1,
                InventoryGeneration = generation, LogicalAccessPathId = path, Accounting = accounting,
                AuthorityChecksum = BaseStudioInfrastructureInventoryContract.AuthorityChecksum(request, generation, path) };
            return OperationResults.Ok<BaseCapturedStudioInfrastructureAuthority>(new InfrastructureAuthority(this, request with
            { ApplicationId = new(request.ApplicationId.AsSpan()), StoreId = new(request.StoreId.AsSpan()), StoreInstanceId = new(request.StoreInstanceId.AsSpan()), Limits = request.Limits with { } }, receipt));
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<IBaseStudioInfrastructureInventorySession>> OpenInfrastructureSessionAsync(
        BaseCapturedStudioInfrastructureAuthority authority, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not InfrastructureAuthority captured || !ReferenceEquals(captured.Owner, this) || !captured.TryOpen())
            return ValueTask.FromResult(InfrastructureFailure<IBaseStudioInfrastructureInventorySession>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization));
        return ValueTask.FromResult(OperationResults.Ok<IBaseStudioInfrastructureInventorySession>(new InfrastructureSession(this, captured)));
    }

    private void EnsureInfrastructureSeed()
    {
        if (Interlocked.Exchange(ref _studioInfrastructureInitialized, 1) != 0) return;
        var value = new BaseStudioSchemaGenerationItem { Kind = BaseStudioInfrastructureInventoryKind.SchemaGeneration, Sequence = 1,
            StoreId = new(_options.StoreId.AsSpan()), RestoreEpoch = 0, SchemaGeneration = 1, ObservedAtUtc = DateTimeOffset.UnixEpoch,
            State = BaseStudioInfrastructureState.Completed, BaselineId = "inmemory.v1", SchemaChecksum = BaseStudioInfrastructureInventoryContract.Hash(static writer => writer.Write("inmemory.v1")),
            DriftDetected = false, Checksum = [] };
        _studioInfrastructureInventory.Add(value with { Checksum = BaseStudioInfrastructureInventoryContract.ItemChecksum(value) });
    }

    private sealed class InfrastructureAuthority(InMemoryRecordStore owner, BaseStudioInfrastructureInventoryRequirement requirement,
        BaseStudioInfrastructureCaptureReceipt receipt) : BaseCapturedStudioInfrastructureAuthority(receipt)
    {
        private int _opened;
        internal InMemoryRecordStore Owner { get; } = owner;
        internal BaseStudioInfrastructureInventoryRequirement Requirement { get; } = requirement;
        internal bool TryOpen() => Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
    }

    private sealed class InfrastructureSession(InMemoryRecordStore owner, InfrastructureAuthority authority) : IBaseStudioInfrastructureInventorySession
    {
        private int _disposed; private readonly CancellationTokenSource _lifetime = new(authority.Requirement.Limits.SessionDeadline);
        public async ValueTask<OperationResult<BaseStudioInfrastructurePage>> ReadPageAsync(BaseStudioInfrastructurePageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Volatile.Read(ref _disposed) != 0 || request.Take < 1 || request.Take > authority.Requirement.Limits.MaximumItems ||
                !BaseStudioInfrastructureInventoryContract.Position(authority.Requirement.Kind, request.After, out long after))
                return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.authorityMismatch", ErrorCategory.Authorization);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); deadline.CancelAfter(authority.Requirement.Limits.PageDeadline);
            await owner._stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                owner.EnsureInfrastructureSeed(); BaseStudioInfrastructureItem[] rows = owner._studioInfrastructureInventory
                    .Where(value => value.Kind == authority.Requirement.Kind && value.Sequence > after && value.Sequence <= authority.Receipt.InventoryGeneration)
                    .OrderBy(static value => value.Sequence).Take(checked(request.Take + 1)).ToArray();
                long rowsRead = rows.LongLength; if (rowsRead > authority.Requirement.Limits.MaximumRowsRead)
                    return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.budgetExceeded", ErrorCategory.Validation);
                bool more = rows.Length > request.Take; if (more) rows = rows[..request.Take]; ImmutableArray<BaseStudioInfrastructureItem> items = [.. rows];
                long bytes = items.Sum(BaseStudioInfrastructureInventoryContract.Measure);
                if (bytes > authority.Requirement.Limits.MaximumEvidenceBytes || bytes > authority.Requirement.Limits.MaximumTransientBytes)
                    return InfrastructureFailure<BaseStudioInfrastructurePage>("base.studio.infrastructure.budgetExceeded", ErrorCategory.Validation);
                BaseStudioInfrastructureBoundary? next = more && items.Length > 0 ? BaseStudioInfrastructureInventoryContract.Boundary(authority.Requirement.Kind, items[^1].Sequence) : null;
                var accounting = new BaseStudioInfrastructureProviderAccounting { RowsRead = rowsRead, EvidenceBytes = bytes, TransientBytes = bytes };
                var page = new BaseStudioInfrastructurePage { Items = items, Next = next, InventoryGeneration = authority.Receipt.InventoryGeneration,
                    Accounting = accounting, PageChecksum = BaseStudioInfrastructureInventoryContract.PageChecksum(items, authority.Receipt.InventoryGeneration, next, accounting) };
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
