using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore : IBaseStudioEvidenceStore
{
    /// <inheritdoc />
    public BaseStudioEvidenceCapability EvidenceCapability { get; } = BaseStudioEvidenceContract.RecordMutationCapability();
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(
        BaseStudioEvidenceRequirement request, BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(scope);
        if (!BaseStudioEvidenceContract.Valid(request) || scope.Kind != request.Scope.Kind || scope.ProtectedIndexDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(scope.ProtectedIndexDigest.AsSpan(), request.ProtectedScopeSeekChecksum.AsSpan()))
            return Failure<BaseCapturedStudioEvidenceAuthority>("base.studio.authorityMismatch", ErrorCategory.Authorization);
        if (request.Kind != BaseStudioEvidenceKind.RecordMutation || request.Limits.MaximumItems > EvidenceCapability.MaximumItems ||
            request.Limits.MaximumRowsRead > EvidenceCapability.MaximumRowsRead || request.Limits.MaximumIntervals > EvidenceCapability.MaximumIntervals ||
            request.Limits.MaximumEvidenceBytes > EvidenceCapability.MaximumEvidenceBytes || request.Limits.MaximumTransientBytes > EvidenceCapability.MaximumTransientBytes)
            return OperationResults.Unsupported<BaseCapturedStudioEvidenceAuthority>(Error("base.studio.evidence.unsupported", ErrorCategory.Unsupported));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.AcquisitionDeadline);
        await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState); long generation = state.GlobalMutationPosition;
            var receipt = new BaseStudioEvidenceCaptureReceipt
            {
                ApplicationId = new string(request.ApplicationId.AsSpan()), Kind = request.Kind, StoreIdentity = new string(_options.StoreId.AsSpan()),
                RestoreEpoch = 0, IndexGeneration = generation, LogicalAccessPathId = BaseStudioEvidenceContract.RecordMutationPath,
                ProtectedScopeSeekChecksum = [.. request.ProtectedScopeSeekChecksum],
                AuthorityChecksum = BaseStudioEvidenceContract.AuthorityChecksum(request, _options.StoreId, 0, generation, BaseStudioEvidenceContract.RecordMutationPath),
            };
            return OperationResults.Ok<BaseCapturedStudioEvidenceAuthority>(new Authority(this, BaseStudioEvidenceContract.Freeze(request), receipt));
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<IBaseStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not Authority captured || !ReferenceEquals(captured.Owner, this) || !captured.TryOpen())
            return ValueTask.FromResult(Failure<IBaseStudioEvidenceSession>("base.studio.authorityMismatch", ErrorCategory.Authorization));
        return ValueTask.FromResult(OperationResults.Ok<IBaseStudioEvidenceSession>(new Session(this, captured)));
    }

    private sealed class Authority(InMemoryRecordStore owner, BaseStudioEvidenceRequirement request, BaseStudioEvidenceCaptureReceipt receipt)
        : BaseCapturedStudioEvidenceAuthority(receipt)
    {
        private int _opened;
        internal InMemoryRecordStore Owner { get; } = owner;
        internal BaseStudioEvidenceRequirement Request { get; } = request;
        internal bool TryOpen() => Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
    }

    private sealed class Session(InMemoryRecordStore owner, Authority authority) : IBaseStudioEvidenceSession
    {
        private int _disposed;
        private readonly CancellationTokenSource _lifetime = new(authority.Request.Limits.SessionDeadline);
        public async ValueTask<OperationResult<BaseStudioEvidencePage>> ReadPageAsync(BaseStudioEvidencePageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Volatile.Read(ref _disposed) != 0 || request.Take < 1 || request.Take > authority.Request.Limits.MaximumItems ||
                !BaseStudioEvidenceContract.Position(request.After, out long after))
                return Failure<BaseStudioEvidencePage>("base.studio.evidence.authorityMismatch", ErrorCategory.Authorization);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); deadline.CancelAfter(authority.Request.Limits.PageDeadline);
            await owner._stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                InMemoryStoreState state = Volatile.Read(ref owner._publishedState);
                if (state.GlobalMutationPosition < authority.Receipt.IndexGeneration)
                    return Failure<BaseStudioEvidencePage>("base.studio.evidence.corrupt", ErrorCategory.Store);
                IEnumerable<BaseMutationJournalEntry> candidates = state.MutationJournal
                    .Where(pair => pair.Key > after && pair.Key <= authority.Receipt.IndexGeneration)
                    .Select(static pair => pair.Value);
                long rowsRead = 0;
                var selected = new List<BaseMutationJournalEntry>(checked(request.Take + 1));
                foreach (BaseMutationJournalEntry candidate in candidates)
                {
                    rowsRead++;
                    if (rowsRead > authority.Request.Limits.MaximumRowsRead) break;
                    if (Matches(candidate.RecordMutation, authority.Request)) selected.Add(candidate);
                    if (selected.Count > request.Take) break;
                }
                if (rowsRead > authority.Request.Limits.MaximumRowsRead)
                    return Failure<BaseStudioEvidencePage>("base.studio.evidence.budgetExceeded", ErrorCategory.Validation);
                BaseMutationJournalEntry[] rows = [.. selected];
                bool more = rows.Length > request.Take; if (more) rows = rows[..request.Take];
                ImmutableArray<BaseStudioEvidenceItem> items = [.. rows.Select(Item)];
                long bytes = items.Sum(BaseStudioEvidenceContract.Measure);
                if (bytes > authority.Request.Limits.MaximumEvidenceBytes || bytes > authority.Request.Limits.MaximumTransientBytes)
                    return Failure<BaseStudioEvidencePage>("base.studio.evidence.budgetExceeded", ErrorCategory.Validation);
                BaseStudioEvidenceBoundary? next = more && items.Length > 0 ? BaseStudioEvidenceContract.Boundary(authority.Request.Kind, items[^1].OrderingTuple) : null;
                ImmutableArray<byte> lower = request.After?.CanonicalTuple ?? BaseStudioEvidenceContract.Tuple(after);
                ImmutableArray<byte> upper = BaseStudioEvidenceContract.Tuple(authority.Receipt.IndexGeneration == long.MaxValue ? long.MaxValue : authority.Receipt.IndexGeneration + 1);
                ImmutableArray<BaseStudioEvidenceReadInterval> intervals = [new() { LogicalAccessPathId = BaseStudioEvidenceContract.RecordMutationPath,
                    ProtectedScopeSeekChecksum = [.. authority.Request.ProtectedScopeSeekChecksum],
                    LowerInclusive = [.. lower], UpperExclusive = upper,
                    Checksum = BaseStudioEvidenceContract.IntervalChecksum(BaseStudioEvidenceContract.RecordMutationPath, authority.Request.ProtectedScopeSeekChecksum, lower, upper) }];
                var accounting = new BaseStudioEvidenceProviderAccounting { RowsRead = rowsRead, Intervals = 1, EvidenceBytes = bytes, TransientBytes = bytes };
                var page = new BaseStudioEvidencePage { Items = items, Next = next, IndexGeneration = authority.Receipt.IndexGeneration,
                    Intervals = intervals, Accounting = accounting, PageChecksum = BaseStudioEvidenceContract.PageChecksum(items, authority.Receipt.IndexGeneration, next, intervals, accounting) };
                return OperationResults.Ok(page);
            }
            finally { owner._stateGate.Release(); }
        }
        public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _lifetime.Dispose(); return ValueTask.CompletedTask; }

        private static bool Matches(BaseRecordMutationJournalEntry? mutation, BaseStudioEvidenceRequirement request)
        {
            if (mutation is null) return false;
            bool scope = request.Scope.Kind switch
            {
                BaseSubjectScopeKind.Global => mutation.TenantId is null,
                BaseSubjectScopeKind.Tenant => StringComparer.Ordinal.Equals(mutation.TenantId, request.Scope.Value),
                _ => false,
            };
            if (!scope) return false;
            return request.Parent switch
            {
                BaseStudioCollectionEvidenceSubject collection => StringComparer.Ordinal.Equals(mutation.CollectionId, collection.CollectionId),
                BaseStudioRecordEvidenceSubject record => StringComparer.Ordinal.Equals(mutation.CollectionId, record.CollectionId) && mutation.RecordId == record.RecordId,
                _ => false,
            };
        }
    }

    private static BaseStudioEvidenceItem Item(BaseMutationJournalEntry row)
    {
        BaseRecordMutationJournalEntry value = row.RecordMutation!;
        var item = new BaseStudioRecordMutationEvidenceItem { Kind = BaseStudioEvidenceKind.RecordMutation, OrderingTuple = BaseStudioEvidenceContract.Tuple(row.Position.Value),
            ObservedAtUtc = value.OccurredAt.ToUniversalTime(), SemanticKind = Semantic(value.Operation), CollectionId = new string(value.CollectionId.AsSpan()),
            RecordId = RecordId.Create(new string(value.RecordId.Value.AsSpan())), Revision = null,
            EvidenceId = new string(value.EventId.AsSpan()), EvidenceChecksum = [] };
        return item with { EvidenceChecksum = BaseStudioEvidenceContract.ItemChecksum(item) };
    }
    private static BaseStudioEvidenceSemanticKind Semantic(BaseOperationKind value) => value switch
    { BaseOperationKind.Create => BaseStudioEvidenceSemanticKind.Created, BaseOperationKind.Patch => BaseStudioEvidenceSemanticKind.Patched,
      BaseOperationKind.Replace => BaseStudioEvidenceSemanticKind.Replaced, BaseOperationKind.Delete => BaseStudioEvidenceSemanticKind.Deleted,
      _ => BaseStudioEvidenceSemanticKind.Transition };

    private static BaseError Error(string code, ErrorCategory category) => new() { Code = code, Message = "The durable evidence operation could not be completed.", Category = category };
    private static OperationResult<T> Failure<T>(string code, ErrorCategory category) => category switch
    {
        ErrorCategory.Authorization => OperationResults.PolicyDenied<T>(Error(code, category)),
        ErrorCategory.Store => OperationResults.StoreError<T>(Error(code, category)),
        _ => OperationResults.ValidationFailed<T>(Error(code, category)),
    };
}
