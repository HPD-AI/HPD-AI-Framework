using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore : IStudioEvidenceStore
{
    /// <inheritdoc />
    public StudioEvidenceCapability EvidenceCapability { get; } = StudioEvidenceContract.RecordMutationCapability();
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseCapturedStudioEvidenceAuthority>> CaptureAuthorityAsync(
        StudioEvidenceRequirement request, BaseOwnedScopeSeekAuthority scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(scope);
        if (!StudioEvidenceContract.Valid(request) || scope.Kind != request.Scope.Kind || scope.ProtectedIndexDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(scope.ProtectedIndexDigest.AsSpan(), request.ProtectedScopeSeekChecksum.AsSpan()))
            return Failure<BaseCapturedStudioEvidenceAuthority>("base.studio.authorityMismatch", ErrorCategory.Authorization);
        if (request.Kind != StudioEvidenceKind.RecordMutation || request.Limits.MaximumItems > EvidenceCapability.MaximumItems ||
            request.Limits.MaximumRowsRead > EvidenceCapability.MaximumRowsRead || request.Limits.MaximumIntervals > EvidenceCapability.MaximumIntervals ||
            request.Limits.MaximumEvidenceBytes > EvidenceCapability.MaximumEvidenceBytes || request.Limits.MaximumTransientBytes > EvidenceCapability.MaximumTransientBytes)
            return OperationResults.Unsupported<BaseCapturedStudioEvidenceAuthority>(Error("base.studio.evidence.unsupported", ErrorCategory.Unsupported));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.AcquisitionDeadline);
        await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState); long generation = state.GlobalMutationPosition;
            var receipt = new StudioEvidenceCaptureReceipt
            {
                ApplicationId = new string(request.ApplicationId.AsSpan()), Kind = request.Kind, StoreIdentity = new string(_options.StoreId.AsSpan()),
                RestoreEpoch = 0, IndexGeneration = generation, LogicalAccessPathId = StudioEvidenceContract.RecordMutationPath,
                ProtectedScopeSeekChecksum = [.. request.ProtectedScopeSeekChecksum],
                AuthorityChecksum = StudioEvidenceContract.AuthorityChecksum(request, _options.StoreId, 0, generation, StudioEvidenceContract.RecordMutationPath),
            };
            return OperationResults.Ok<BaseCapturedStudioEvidenceAuthority>(new Authority(this, StudioEvidenceContract.Freeze(request), receipt));
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<IStudioEvidenceSession>> OpenSessionAsync(BaseCapturedStudioEvidenceAuthority authority,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not Authority captured || !ReferenceEquals(captured.Owner, this) || !captured.TryOpen())
            return ValueTask.FromResult(Failure<IStudioEvidenceSession>("base.studio.authorityMismatch", ErrorCategory.Authorization));
        return ValueTask.FromResult(OperationResults.Ok<IStudioEvidenceSession>(new Session(this, captured)));
    }

    private sealed class Authority(InMemoryRecordStore owner, StudioEvidenceRequirement request, StudioEvidenceCaptureReceipt receipt)
        : BaseCapturedStudioEvidenceAuthority(receipt)
    {
        private int _opened;
        internal InMemoryRecordStore Owner { get; } = owner;
        internal StudioEvidenceRequirement Request { get; } = request;
        internal bool TryOpen() => Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
    }

    private sealed class Session(InMemoryRecordStore owner, Authority authority) : IStudioEvidenceSession
    {
        private int _disposed;
        private readonly CancellationTokenSource _lifetime = new(authority.Request.Limits.SessionDeadline);
        public async ValueTask<OperationResult<StudioEvidencePage>> ReadPageAsync(StudioEvidencePageRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (Volatile.Read(ref _disposed) != 0 || request.Take < 1 || request.Take > authority.Request.Limits.MaximumItems ||
                !StudioEvidenceContract.Position(request.After, out long after))
                return Failure<StudioEvidencePage>("base.studio.evidence.authorityMismatch", ErrorCategory.Authorization);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token); deadline.CancelAfter(authority.Request.Limits.PageDeadline);
            await owner._stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                InMemoryStoreState state = Volatile.Read(ref owner._publishedState);
                if (state.GlobalMutationPosition < authority.Receipt.IndexGeneration)
                    return Failure<StudioEvidencePage>("base.studio.evidence.corrupt", ErrorCategory.Store);
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
                    return Failure<StudioEvidencePage>("base.studio.evidence.budgetExceeded", ErrorCategory.Validation);
                BaseMutationJournalEntry[] rows = [.. selected];
                bool more = rows.Length > request.Take; if (more) rows = rows[..request.Take];
                ImmutableArray<StudioEvidenceItem> items = [.. rows.Select(Item)];
                long bytes = items.Sum(StudioEvidenceContract.Measure);
                if (bytes > authority.Request.Limits.MaximumEvidenceBytes || bytes > authority.Request.Limits.MaximumTransientBytes)
                    return Failure<StudioEvidencePage>("base.studio.evidence.budgetExceeded", ErrorCategory.Validation);
                StudioEvidenceBoundary? next = more && items.Length > 0 ? StudioEvidenceContract.Boundary(authority.Request.Kind, items[^1].OrderingTuple) : null;
                ImmutableArray<byte> lower = request.After?.CanonicalTuple ?? StudioEvidenceContract.Tuple(after);
                ImmutableArray<byte> upper = StudioEvidenceContract.Tuple(authority.Receipt.IndexGeneration == long.MaxValue ? long.MaxValue : authority.Receipt.IndexGeneration + 1);
                ImmutableArray<StudioEvidenceReadInterval> intervals = [new() { LogicalAccessPathId = StudioEvidenceContract.RecordMutationPath,
                    ProtectedScopeSeekChecksum = [.. authority.Request.ProtectedScopeSeekChecksum],
                    LowerInclusive = [.. lower], UpperExclusive = upper,
                    Checksum = StudioEvidenceContract.IntervalChecksum(StudioEvidenceContract.RecordMutationPath, authority.Request.ProtectedScopeSeekChecksum, lower, upper) }];
                var accounting = new StudioEvidenceProviderAccounting { RowsRead = rowsRead, Intervals = 1, EvidenceBytes = bytes, TransientBytes = bytes };
                var page = new StudioEvidencePage { Items = items, Next = next, IndexGeneration = authority.Receipt.IndexGeneration,
                    Intervals = intervals, Accounting = accounting, PageChecksum = StudioEvidenceContract.PageChecksum(items, authority.Receipt.IndexGeneration, next, intervals, accounting) };
                return OperationResults.Ok(page);
            }
            finally { owner._stateGate.Release(); }
        }
        public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _lifetime.Dispose(); return ValueTask.CompletedTask; }

        private static bool Matches(BaseRecordMutationJournalEntry? mutation, StudioEvidenceRequirement request)
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
                StudioCollectionEvidenceSubject collection => StringComparer.Ordinal.Equals(mutation.CollectionId, collection.CollectionId),
                StudioRecordEvidenceSubject record => StringComparer.Ordinal.Equals(mutation.CollectionId, record.CollectionId) && mutation.RecordId == record.RecordId,
                _ => false,
            };
        }
    }

    private static StudioEvidenceItem Item(BaseMutationJournalEntry row)
    {
        BaseRecordMutationJournalEntry value = row.RecordMutation!;
        var item = new StudioRecordMutationEvidenceItem { Kind = StudioEvidenceKind.RecordMutation, OrderingTuple = StudioEvidenceContract.Tuple(row.Position.Value),
            ObservedAtUtc = value.OccurredAt.ToUniversalTime(), SemanticKind = Semantic(value.Operation), CollectionId = new string(value.CollectionId.AsSpan()),
            RecordId = RecordId.Create(new string(value.RecordId.Value.AsSpan())), Revision = null,
            EvidenceId = new string(value.EventId.AsSpan()), EvidenceChecksum = [] };
        return item with { EvidenceChecksum = StudioEvidenceContract.ItemChecksum(item) };
    }
    private static StudioEvidenceSemanticKind Semantic(BaseOperationKind value) => value switch
    { BaseOperationKind.Create => StudioEvidenceSemanticKind.Created, BaseOperationKind.Patch => StudioEvidenceSemanticKind.Patched,
      BaseOperationKind.Replace => StudioEvidenceSemanticKind.Replaced, BaseOperationKind.Delete => StudioEvidenceSemanticKind.Deleted,
      _ => StudioEvidenceSemanticKind.Transition };

    private static BaseError Error(string code, ErrorCategory category) => new() { Code = code, Message = "The durable evidence operation could not be completed.", Category = category };
    private static OperationResult<T> Failure<T>(string code, ErrorCategory category) => category switch
    {
        ErrorCategory.Authorization => OperationResults.PolicyDenied<T>(Error(code, category)),
        ErrorCategory.Store => OperationResults.StoreError<T>(Error(code, category)),
        _ => OperationResults.ValidationFailed<T>(Error(code, category)),
    };
}
