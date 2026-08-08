using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class DefaultBaseVectorRuntime(
    IEnumerable<IBaseVectorProvider> providers,
    IBaseVectorAuthority authority,
    IBasePolicyOrchestrator policy,
    IBaseRecordRedactor redactor,
    BaseOpaqueTokenProtector tokens,
    HPDBaseVectorSnapshot options,
    TimeProvider timeProvider) : IBaseVectorRuntime
{
    private readonly SemaphoreSlim _providerSlots = new(options.MaxActiveAndQuarantinedOperations, options.MaxActiveAndQuarantinedOperations);
    public async ValueTask<OperationResult<BaseVectorRuntimeResult>> ExecuteAsync(BaseVectorRuntimeRequest request, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            OperationResult<BaseVectorRuntimeResult> result = await ExecuteOnceAsync(request, cancellationToken).ConfigureAwait(false);
            if (attempt >= 2 || !string.Equals(result.Error?.Code, BaseVectorErrorCodes.SnapshotChanged, StringComparison.Ordinal)) return result;
        }
    }

    private async ValueTask<OperationResult<BaseVectorRuntimeResult>> ExecuteOnceAsync(BaseVectorRuntimeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Take is < 1 || request.Take > options.MaxTopK) return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.LimitExceeded, "The requested vector result bound is invalid.", ErrorCategory.Validation);
        if (request.Vector.Dimensions != request.Index.Dimensions) return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.DimensionMismatch, "The vector dimensions do not match the index.", ErrorCategory.Validation);
        if (request.Vector.Dimensions > options.MaxDimensions) return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.LimitExceeded, "The vector dimensions exceed the configured limit.", ErrorCategory.Validation);
        if (request.Index.Function == BaseVectorFunction.CosineSimilarity && request.Vector.IsZeroNorm) return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.ZeroNorm, "Cosine vector queries require a non-zero vector.", ErrorCategory.Validation);

        OperationResult<BasePolicyEvaluation> influence = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = request.Principal, Operation = request.Operation, Collection = request.Collection, ResourceKind = PolicyResourceKind.VectorIndex, VectorIndexId = request.Index.Id, VectorSpaceId = request.Index.VectorSpaceId }, cancellationToken).ConfigureAwait(false);
        if (!influence.Status.IsSuccess()) return CopyFailure<BaseVectorRuntimeResult, BasePolicyEvaluation>(influence);

        IBaseVectorProvider[] installed = providers.ToArray();
        if (installed.Length != 1) return Failure<BaseVectorRuntimeResult>(OperationStatus.CapabilityUnavailable, BaseVectorErrorCodes.ProviderUnavailable, "The vector provider is unavailable.", ErrorCategory.Capability);
        IBaseVectorProvider provider = installed[0];
        if (request.Take > provider.Descriptor.MaximumTopK) return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.LimitExceeded, "The requested vector result bound is invalid.", ErrorCategory.Validation);

        BaseVectorConsistencyRequirement requirement = request.Consistency ?? (provider.Descriptor.Consistency == BaseVectorProviderConsistency.TransactionalCurrent ? new BaseVectorConsistencyRequirement.Current() : new BaseVectorConsistencyRequirement.Available());
        OperationResult<IBaseVectorHydrationSession> open = await Open(request, requirement, cancellationToken).ConfigureAwait(false);
        if (!open.Status.IsSuccess() || open.Value is null) return CopyFailure<BaseVectorRuntimeResult, IBaseVectorHydrationSession>(open);
        await using IBaseVectorHydrationSession session = open.Value;
        OperationResult<BaseVectorRuntimeResult>? tokenFailure = ValidateConsistency(requirement, session.Snapshot);
        if (tokenFailure is not null) return tokenFailure;

        BaseVectorCandidateConstraint effective;
        try { effective = Combine(request.Constraint, LowerPolicy(influence.Value!.EffectiveRecordFilter, request.Index)); }
        catch (NotSupportedException) { return Failure<BaseVectorRuntimeResult>(OperationStatus.Unsupported, BaseVectorErrorCodes.PolicyConstraintUnsupported, "The effective policy cannot be enforced by this vector index.", ErrorCategory.Unsupported); }
        (BaseVectorCandidateConstraint normalized, BaseVectorConstraintDigest digest) = BaseVectorConstraintNormalizer.Normalize(effective);

        BaseVectorConstraintPreparation preparation;
        BaseVectorProviderResult ranked;
        try
        {
            preparation = await InvokeBoundedAsync(token => provider.PrepareAsync(new BaseVectorProviderPreparationRequest { Index = request.Index, Constraint = normalized, ConstraintDigest = digest, Snapshot = session.Snapshot }, token), cancellationToken).ConfigureAwait(false);
            if (preparation.Enforcement != BaseVectorConstraintEnforcement.PreRankingExact || !preparation.ConstraintDigest.Equals(digest)) return Failure<BaseVectorRuntimeResult>(OperationStatus.Unsupported, BaseVectorErrorCodes.PolicyConstraintUnsupported, "The vector provider cannot prove exact candidate enforcement.", ErrorCategory.Unsupported);
            ranked = await InvokeBoundedAsync(token => provider.SearchAsync(new BaseVectorExecutionRequest { Index = request.Index, Vector = request.Vector, Take = request.Take, Plan = preparation.Plan, Snapshot = session.Snapshot, Consistency = requirement, CorrelationId = request.Operation.CorrelationId }, token), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { return Failure<BaseVectorRuntimeResult>(OperationStatus.StoreError, BaseVectorErrorCodes.Timeout, "The vector operation exceeded its deadline.", ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Failure<BaseVectorRuntimeResult>(OperationStatus.StoreError, BaseVectorErrorCodes.Cancelled, "The vector operation was cancelled.", ErrorCategory.Store); }
        catch (Exception) { return Failure<BaseVectorRuntimeResult>(OperationStatus.CapabilityUnavailable, BaseVectorErrorCodes.ProviderUnavailable, "The vector provider is unavailable.", ErrorCategory.Capability); }

        if (!ValidProviderResult(ranked, session.Snapshot, request, provider)) return Failure<BaseVectorRuntimeResult>(OperationStatus.StoreError, BaseVectorErrorCodes.ProviderResultInvalid, "The vector provider returned invalid result evidence.", ErrorCategory.Store);
        BaseVectorCandidateIdentity[] identities = ranked.Candidates.Select(static candidate => new BaseVectorCandidateIdentity(candidate.RecordId, candidate.IndexedRevision, candidate.IndexedPosition)).ToArray();
        OperationResult<RecordEnvelope[]> hydrated;
        try { hydrated = await InvokeBoundedAsync(token => session.GetExactAsync(request.Collection, identities, request.Operation, token), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return Failure<BaseVectorRuntimeResult>(OperationStatus.StoreError, BaseVectorErrorCodes.Timeout, "The vector hydration exceeded its deadline.", ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Failure<BaseVectorRuntimeResult>(OperationStatus.StoreError, BaseVectorErrorCodes.Cancelled, "The vector operation was cancelled.", ErrorCategory.Store); }
        catch (Exception) { return Failure<BaseVectorRuntimeResult>(OperationStatus.CapabilityUnavailable, BaseVectorErrorCodes.ProviderUnavailable, "The vector provider is unavailable.", ErrorCategory.Capability); }
        if (!hydrated.Status.IsSuccess() || hydrated.Value is null) return Failure<BaseVectorRuntimeResult>(OperationStatus.Conflict, BaseVectorErrorCodes.SnapshotChanged, "The authoritative vector snapshot changed.", ErrorCategory.Conflict);

        var matches = new List<BaseVectorRuntimeMatch>(hydrated.Value.Length);
        for (int i = 0; i < hydrated.Value.Length; i++)
        {
            RecordEnvelope envelope = hydrated.Value[i];
            BaseVectorCandidate candidate = ranked.Candidates[i];
            if (envelope.Id != candidate.RecordId || envelope.Metadata.Revision != candidate.IndexedRevision) return Failure<BaseVectorRuntimeResult>(OperationStatus.Conflict, BaseVectorErrorCodes.SnapshotChanged, "The authoritative vector snapshot changed.", ErrorCategory.Conflict);
            OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = request.Principal, Operation = request.Operation, Collection = request.Collection, ResourceKind = PolicyResourceKind.Record, ExistingRecord = envelope, RecordId = envelope.Id }, cancellationToken).ConfigureAwait(false);
            if (!disclosure.Status.IsSuccess()) return CopyFailure<BaseVectorRuntimeResult, BasePolicyEvaluation>(disclosure);
            matches.Add(new BaseVectorRuntimeMatch { Record = redactor.RedactRecord(envelope, request.Collection, disclosure.Value!, VisibilityLevel.Authenticated), Rank = i + 1, Measure = candidate.Measure });
        }

        return OperationResults.Ok(new BaseVectorRuntimeResult { Matches = matches.ToArray(), VectorIndexId = request.Index.Id, VectorIndexGeneration = session.Snapshot.VectorIndexGeneration, ProviderId = provider.Descriptor.Id, Accuracy = ranked.Accuracy, ConsistencyToken = Issue(session.Snapshot) });
    }

    public async ValueTask<OperationResult<BaseVectorConsistencyToken>> CaptureAsync(CollectionDefinition collection, VectorIndexDefinition index, PrincipalContext principal, OperationContext operation, CancellationToken cancellationToken)
    {
        OperationResult<BasePolicyEvaluation> allowed = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = principal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.VectorIndex, VectorIndexId = index.Id, VectorSpaceId = index.VectorSpaceId }, cancellationToken).ConfigureAwait(false);
        if (!allowed.Status.IsSuccess()) return CopyFailure<BaseVectorConsistencyToken, BasePolicyEvaluation>(allowed);
        OperationResult<IBaseVectorHydrationSession> opened = await authority.OpenAsync(collection, index, new BaseVectorConsistencyRequirement.Current(), operation, cancellationToken).ConfigureAwait(false);
        if (!opened.Status.IsSuccess() || opened.Value is null) return CopyFailure<BaseVectorConsistencyToken, IBaseVectorHydrationSession>(opened);
        await using (opened.Value.ConfigureAwait(false)) return OperationResults.Ok(Issue(opened.Value.Snapshot));
    }

    private async ValueTask<OperationResult<IBaseVectorHydrationSession>> Open(BaseVectorRuntimeRequest request, BaseVectorConsistencyRequirement requirement, CancellationToken cancellationToken)
    {
        try { return await authority.OpenAsync(request.Collection, request.Index, requirement, request.Operation, cancellationToken).AsTask().WaitAsync(options.ConsistencyWaitTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return Failure<IBaseVectorHydrationSession>(OperationStatus.StoreError, BaseVectorErrorCodes.Timeout, "The vector consistency wait exceeded its deadline.", ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Failure<IBaseVectorHydrationSession>(OperationStatus.StoreError, BaseVectorErrorCodes.Cancelled, "The vector operation was cancelled.", ErrorCategory.Store); }
        catch (Exception) { return Failure<IBaseVectorHydrationSession>(OperationStatus.CapabilityUnavailable, BaseVectorErrorCodes.ProviderUnavailable, "The vector provider is unavailable.", ErrorCategory.Capability); }
    }

    private OperationResult<BaseVectorRuntimeResult>? ValidateConsistency(BaseVectorConsistencyRequirement requirement, BaseVectorAuthoritySnapshot snapshot)
    {
        if (requirement is not BaseVectorConsistencyRequirement.AtLeast atLeast) return null;
        BaseOpaqueTokenResult decoded = tokens.Unprotect(BaseVectorConsistencyTokenIssuer.Purpose, 1, atLeast.Token.Encode(), 48, 4096, BaseVectorConsistencyTokenIssuer.Scope);
        if (decoded.Status != BaseOpaqueTokenStatus.Valid || decoded.Plaintext is null) return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.ConsistencyInvalid, "The vector consistency token is invalid.", ErrorCategory.Validation);
        try
        {
            using var stream = new MemoryStream(decoded.Plaintext, writable: false); using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            long expiresTicks = reader.ReadInt64(); long issuedTicks = reader.ReadInt64(); string store = reader.ReadString(); long epoch = reader.ReadInt64(); long schema = reader.ReadInt64(); string collection = reader.ReadString(); long purge = reader.ReadInt64(); string index = reader.ReadString(); long generation = reader.ReadInt64(); string space = reader.ReadString(); long position = reader.ReadInt64();
            if (stream.Position != stream.Length) throw new InvalidDataException();
            if (issuedTicks > expiresTicks || TimeSpan.FromTicks(expiresTicks - issuedTicks) is { } lifetime && (lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromDays(30))) throw new InvalidDataException();
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (new DateTimeOffset(issuedTicks, TimeSpan.Zero) > now) throw new InvalidDataException();
            if (now >= new DateTimeOffset(expiresTicks, TimeSpan.Zero)) return Failure<BaseVectorRuntimeResult>(OperationStatus.NotFound, BaseVectorErrorCodes.ConsistencyExpired, "The vector consistency token has expired.", ErrorCategory.NotFound);
            if (store != snapshot.StoreIdentityDigest || epoch != snapshot.RestoreEpoch || schema != snapshot.SchemaGeneration || collection != snapshot.CollectionId || purge != snapshot.PurgeGeneration || index != snapshot.VectorIndexId || generation != snapshot.VectorIndexGeneration || space != snapshot.VectorSpaceId) return Failure<BaseVectorRuntimeResult>(OperationStatus.Conflict, BaseVectorErrorCodes.ConsistencyScopeMismatch, "The vector consistency token belongs to another authority scope.", ErrorCategory.Conflict);
            if (snapshot.HighWatermark.Value < position) return Failure<BaseVectorRuntimeResult>(OperationStatus.CapabilityUnavailable, BaseVectorErrorCodes.ConsistencyUnavailable, "The requested vector consistency is unavailable.", ErrorCategory.Capability);
            return null;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentOutOfRangeException) { return Failure<BaseVectorRuntimeResult>(OperationStatus.ValidationFailed, BaseVectorErrorCodes.ConsistencyInvalid, "The vector consistency token is invalid.", ErrorCategory.Validation); }
    }

    private BaseVectorConsistencyToken Issue(BaseVectorAuthoritySnapshot snapshot)
    {
        DateTimeOffset issuedAt = timeProvider.GetUtcNow();
        return BaseVectorConsistencyTokenIssuer.Issue(snapshot, tokens, issuedAt, checked(issuedAt + options.ConsistencyTokenLifetime));
    }

    private async ValueTask<T> InvokeBoundedAsync<T>(Func<CancellationToken, ValueTask<T>> invoke, CancellationToken cancellationToken)
    {
        if (!await _providerSlots.WaitAsync(options.ProviderTimeout, cancellationToken).ConfigureAwait(false)) throw new TimeoutException();
        bool release = true;
        Task<T>? work = null;
        try
        {
            work = invoke(cancellationToken).AsTask();
            return await work.WaitAsync(options.ProviderTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch when (work is { IsCompleted: false })
        {
            release = false;
            _ = work.ContinueWith(static (_, state) => ((SemaphoreSlim)state!).Release(), _providerSlots, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        finally
        {
            if (release) _providerSlots.Release();
        }
    }

    private static bool ValidProviderResult(BaseVectorProviderResult result, BaseVectorAuthoritySnapshot snapshot, BaseVectorRuntimeRequest request, IBaseVectorProvider provider)
    {
        if (result is null || result.Snapshot != snapshot || result.Candidates is null || result.Candidates.Length > request.Take || result.Accuracy == BaseVectorResultAccuracy.Approximate && provider.Descriptor.Exact) return false;
        var ids = new HashSet<RecordId>();
        for (int i = 0; i < result.Candidates.Length; i++) { BaseVectorCandidate item = result.Candidates[i]; if (item.Rank != i + 1 || !ids.Add(item.RecordId) || !double.IsFinite(item.Measure.Value) || item.Measure.Function != request.Index.Function || item.IndexedPosition.Value > snapshot.HighWatermark.Value) return false; }
        return true;
    }

    private static BaseVectorCandidateConstraint Combine(BaseVectorCandidateConstraint left, BaseVectorCandidateConstraint right) => left is BaseVectorCandidateConstraint.True ? right : right is BaseVectorCandidateConstraint.True ? left : new BaseVectorCandidateConstraint.And([left, right]);
    private static BaseVectorCandidateConstraint LowerPolicy(FilterExpression? filter, VectorIndexDefinition index)
    {
        if (filter is null) return new BaseVectorCandidateConstraint.True();
        if (filter.Kind is FilterNodeKind.And or FilterNodeKind.Or && filter.Children is { Length: > 0 }) { BaseVectorCandidateConstraint[] children = filter.Children.Select(child => LowerPolicy(child, index)).ToArray(); return filter.Kind == FilterNodeKind.And ? new BaseVectorCandidateConstraint.And(children) : new BaseVectorCandidateConstraint.Or(children); }
        if (filter.Kind != FilterNodeKind.Compare || filter.Operator != FilterOperator.Equal || filter.Field is null || filter.Value is null || !(index.FilterFieldIds ?? []).Contains(filter.Field, StringComparer.Ordinal)) throw new NotSupportedException();
        BaseVectorFilterValue value = filter.Value.Kind switch { QueryValueKind.Null => BaseVectorFilterValue.Null(), QueryValueKind.String when filter.Value.String is not null => BaseVectorFilterValue.FromString(filter.Value.String), QueryValueKind.Boolean when filter.Value.Boolean is not null => BaseVectorFilterValue.FromBoolean(filter.Value.Boolean.Value), QueryValueKind.Integer when filter.Value.Integer is not null => BaseVectorFilterValue.FromInteger(filter.Value.Integer.Value), QueryValueKind.Id when filter.Value.Id is not null => BaseVectorFilterValue.FromId(filter.Value.Id), _ => throw new NotSupportedException() };
        return new BaseVectorCandidateConstraint.Equal(new BaseVectorFilterField(filter.Field, value.Kind), value);
    }

    private static OperationResult<T> CopyFailure<T, TInput>(OperationResult<TInput> source) => new() { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };
    private static OperationResult<T> Failure<T>(OperationStatus status, string code, string message, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Message = message, Category = category } };

}
