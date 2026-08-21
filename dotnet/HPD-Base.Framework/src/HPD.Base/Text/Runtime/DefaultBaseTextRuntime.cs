using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class DefaultBaseTextRuntime(
    IEnumerable<IBaseTextAuthority> authorities,
    IBasePolicyOrchestrator policy,
    IBaseRecordRedactor redactor,
    BaseTextCursorCodec cursors,
    BaseTextConsistencyTokenCodec consistencyTokens,
    TimeProvider timeProvider) : IBaseTextRuntime
{
    public async ValueTask<OperationResult<BaseTextRuntimeResult>> ExecuteAsync(BaseTextRuntimeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Take is < 1 || request.Take > request.Index.Limits.MaximumResults) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        BaseTextQueryContract.Validate(request.Query);
        if (!QueryFields(request.Query).All(id => request.Index.Fields.Any(field => field.StableFieldId == id))) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.QueryInvalid, ErrorCategory.Validation);
        OperationResult<BasePolicyEvaluation> influence = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = request.Principal, Operation = request.Operation, Collection = request.Collection, ResourceKind = PolicyResourceKind.TextIndex, TextIndexId = request.Index.Id }, cancellationToken).ConfigureAwait(false);
        if (!influence.Status.IsSuccess() || influence.Value is null || !BaseSystemCollectionGate.HasExactTextGrant(influence, BaseTextGrants.Query, request.Principal, request.Operation, request.Collection.Id, request.Index.Id)) return Fail(OperationStatus.PolicyDenied, BaseTextErrorCodes.Unauthorized, ErrorCategory.Authorization);
        BaseTextCandidateConstraint effective;
        try { effective = Combine(request.Constraint, LowerPolicy(influence.Value.EffectiveRecordFilter, request.Index)); }
        catch (NotSupportedException) { return Fail(OperationStatus.Unsupported, BaseTextErrorCodes.PolicyConstraintUnsupported, ErrorCategory.Unsupported); }
        ImmutableArray<byte> queryDigest = BaseTextQueryContract.Digest(request.Query); ImmutableArray<byte> constraintDigest = BaseTextSemanticEvaluator.ConstraintDigest(effective);
        IBaseTextAuthority[] installed = authorities.ToArray(); if (installed.Length != 1) return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        IBaseTextAuthority authority = installed[0]; DateTimeOffset deadline = checked(timeProvider.GetUtcNow() + request.Index.Limits.QueryTimeout);
        if (request.Consistency is BaseTextConsistencyRequirement.BoundedStaleness bounded && (bounded.MaximumAge <= TimeSpan.Zero || bounded.MaximumAge > TimeSpan.FromDays(30))) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.ConsistencyInvalid, ErrorCategory.Validation);
        OperationResult<IBaseTextHydrationSession> opened = await authority.OpenAsync(new BaseTextAuthorityOpenRequest { CollectionId = request.Collection.Id, TextIndexId = request.Index.Id, TextIndexVersion = request.Index.Version, Consistency = request.Consistency, DeadlineUtc = deadline, CorrelationId = request.Operation.CorrelationId ?? string.Empty }, cancellationToken).ConfigureAwait(false);
        if (!opened.Status.IsSuccess() || opened.Value is null) return Copy(opened);
        await using IBaseTextHydrationSession session = opened.Value;
        if (request.Consistency is BaseTextConsistencyRequirement.AtLeast atLeast && !consistencyTokens.Satisfied(atLeast.Token, session.Snapshot)) return Fail(OperationStatus.Conflict, BaseTextErrorCodes.ConsistencyUnavailable, ErrorCategory.Conflict);
        ImmutableArray<byte>? afterBoundary = null;
        if (request.After is { } supplied)
        {
            if (!cursors.TryRead(supplied, session.Snapshot, queryDigest.AsSpan(), constraintDigest.AsSpan(), out ImmutableArray<byte> decoded)) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.CursorInvalid, ErrorCategory.Validation);
            afterBoundary = decoded;
        }
        OperationResult<BaseTextConstraintPreparation> prepared = await session.PrepareAsync(new BaseTextProviderPreparationRequest { Snapshot = session.Snapshot, Index = request.Index, NormalizedQuery = request.Query, QueryDigest = queryDigest, Constraint = effective, ConstraintDigest = constraintDigest, InfluenceConstraints = [], Limits = request.Index.Limits }, cancellationToken).ConfigureAwait(false);
        if (!prepared.Status.IsSuccess() || prepared.Value is null || prepared.Value.Enforcement != BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking || !prepared.Value.QueryDigest.AsSpan().SequenceEqual(queryDigest.AsSpan()) || !prepared.Value.ConstraintDigest.AsSpan().SequenceEqual(constraintDigest.AsSpan())) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        OperationResult<BaseTextProviderResult> searched = await session.SearchAsync(new BaseTextExecutionRequest { Snapshot = session.Snapshot, Plan = prepared.Value.Plan, TakePlusOne = checked(request.Take + 1), AfterBoundary = afterBoundary, Limits = request.Index.Limits, DeadlineUtc = deadline, CorrelationId = request.Operation.CorrelationId ?? string.Empty }, cancellationToken).ConfigureAwait(false);
        if (!searched.Status.IsSuccess() || searched.Value is null || !ValidProviderResult(searched.Value, session.Snapshot, request.Take + 1, afterBoundary)) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        BaseTextCandidate[] candidates = searched.Value.Candidates.ToArray();
        OperationResult<RecordEnvelope[]> hydrated = await session.GetExactAsync(request.Collection, candidates.Select(static item => new BaseTextCandidateIdentity(item.RecordId, item.Revision, item.IndexedPosition)).ToArray(), request.Operation, cancellationToken).ConfigureAwait(false);
        if (!hydrated.Status.IsSuccess() || hydrated.Value is null || hydrated.Value.Length != candidates.Length) return Fail(OperationStatus.Conflict, BaseTextErrorCodes.HydrationSnapshotConflict, ErrorCategory.Conflict);
        var matches = ImmutableArray.CreateBuilder<BaseTextRuntimeMatch>(Math.Min(request.Take, candidates.Length));
        for (int index = 0; index < candidates.Length; index++)
        {
            RecordEnvelope record = hydrated.Value[index]; BaseTextCandidate candidate = candidates[index];
            if (record.Id != candidate.RecordId || record.Metadata.Revision != candidate.Revision || !BaseRecordFilterMatcher.Matches(record, influence.Value.EffectiveRecordFilter) || !BaseTextSemanticEvaluator.ConstraintMatches(record.Payload, request.Index, request.Constraint)) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
            BaseTextEvaluatedCandidate? verified = BaseTextSemanticEvaluator.Evaluate(record.Payload, request.Index, request.Query, queryDigest);
            if (verified is null || verified.Score != candidate.Score || !verified.Proof.ProofDigest.AsSpan().SequenceEqual(candidate.ScoreProof.ProofDigest.AsSpan())) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
            OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = request.Principal, Operation = request.Operation, Collection = request.Collection, ResourceKind = PolicyResourceKind.Record, ExistingRecord = record, RecordId = record.Id }, cancellationToken).ConfigureAwait(false);
            if (!disclosure.Status.IsSuccess() || disclosure.Value is null || !BaseRecordFilterMatcher.Matches(record, disclosure.Value.EffectiveRecordFilter)) return Fail(OperationStatus.PolicyDenied, BaseTextErrorCodes.Unauthorized, ErrorCategory.Authorization);
            if (index < request.Take) matches.Add(new BaseTextRuntimeMatch { Record = redactor.RedactRecord(record, request.Collection, disclosure.Value, VisibilityLevel.Authenticated), Score = candidate.Score, Revision = candidate.Revision });
        }
        BaseTextCursor? next = candidates.Length > request.Take ? cursors.Issue(session.Snapshot, queryDigest.AsSpan(), constraintDigest.AsSpan(), candidates[request.Take - 1].CanonicalOrderingBoundary.AsSpan()) : null;
        return OperationResults.Ok(new BaseTextRuntimeResult { Matches = matches.ToImmutable(), Next = next, Consistency = consistencyTokens.Issue(session.Snapshot) });
    }

    private static IEnumerable<string> QueryFields(BaseTextQuery query) => query switch { BaseTextQuery.Field value => [value.StableFieldId, .. QueryFields(value.Child)], BaseTextQuery.And value => value.Children.SelectMany(QueryFields), BaseTextQuery.Or value => value.Children.SelectMany(QueryFields), BaseTextQuery.Not value => QueryFields(value.Child), _ => [] };
    private static bool ValidProviderResult(BaseTextProviderResult result, BaseTextAuthoritySnapshot snapshot, int takePlusOne, ImmutableArray<byte>? after)
    {
        if (result.Snapshot != snapshot || !snapshot.AnalyzerReceipt.AsSpan().SequenceEqual(BaseTextContractReceipts.AnalyzerReceipt.AsSpan()) || !snapshot.ScoringReceipt.AsSpan().SequenceEqual(BaseTextContractReceipts.ScoringReceipt.AsSpan())
            || result.Candidates.Length > takePlusOne || result.Completeness.RequestedTakePlusOne != takePlusOne || result.Completeness.ReturnedCandidateCount != result.Candidates.Length
            || result.Completeness.HasMore != (result.Candidates.Length == takePlusOne) || result.Accounting.CandidateCount != result.Candidates.Length
            || result.Accounting.AuthorizedRecordsExamined < result.Candidates.Length || result.Accounting.PostingsExamined < 0 || result.Accounting.ScoreProofBytes < 0 || result.Accounting.OrderingBytes < 0 || result.Accounting.RetainedTransientBytes < 0) return false;
        var ids = new HashSet<RecordId>(); ImmutableArray<byte>? prior = after;
        foreach (BaseTextCandidate candidate in result.Candidates)
        {
            ImmutableArray<byte> expected = BaseTextSemanticEvaluator.OrderingBoundary(candidate.Score, candidate.RecordId);
            if (string.IsNullOrWhiteSpace(candidate.RecordId.Value) || string.IsNullOrWhiteSpace(candidate.Revision.Value) || !ids.Add(candidate.RecordId)
                || candidate.IndexedPosition.Value < 0 || candidate.IndexedPosition.Value > snapshot.SearchVisibleThrough.Value
                || !expected.AsSpan().SequenceEqual(candidate.CanonicalOrderingBoundary.AsSpan()) || prior is { } boundary && boundary.AsSpan().SequenceCompareTo(expected.AsSpan()) >= 0
                || candidate.ScoreProof.ProofDigest.Length != 32) return false;
            prior = expected;
        }
        return true;
    }
    private static BaseTextCandidateConstraint Combine(BaseTextCandidateConstraint left, BaseTextCandidateConstraint right) => left is BaseTextCandidateConstraint.True ? right : right is BaseTextCandidateConstraint.True ? left : new BaseTextCandidateConstraint.And([left, right]);
    private static BaseTextCandidateConstraint LowerPolicy(FilterExpression? filter, BaseTextIndexDefinition index)
    {
        if (filter is null || filter.Kind == FilterNodeKind.True) return new BaseTextCandidateConstraint.True();
        if (filter.Kind == FilterNodeKind.False) return new BaseTextCandidateConstraint.False();
        if (filter.Kind is FilterNodeKind.And or FilterNodeKind.Or && filter.Children is { Length: > 0 }) { BaseTextCandidateConstraint[] children = filter.Children.Select(child => LowerPolicy(child, index)).ToArray(); return filter.Kind == FilterNodeKind.And ? new BaseTextCandidateConstraint.And([.. children]) : new BaseTextCandidateConstraint.Or([.. children]); }
        BaseTextIndexFilterFieldDefinition? field = index.FilterFields.SingleOrDefault(value => value.StableFieldId == filter.Field); if (field is null) throw new NotSupportedException(); BaseTextFilterField handle = new(field.StableFieldId, field.ValueKind);
        if (filter.Kind == FilterNodeKind.IsNull) return new BaseTextCandidateConstraint.IsNull(handle);
        if (filter.Kind == FilterNodeKind.IsDefined) throw new NotSupportedException();
        if (filter.Kind == FilterNodeKind.Compare && filter.Operator == FilterOperator.Equal && filter.Value is not null) return new BaseTextCandidateConstraint.Equal(handle, Value(filter.Value, field.ValueKind));
        if (filter.Kind == FilterNodeKind.In && filter.Values is { Length: > 0 } values) return new BaseTextCandidateConstraint.In(handle, values.Select(value => Value(value, field.ValueKind)).ToImmutableArray());
        throw new NotSupportedException();
    }
    private static BaseTextFilterValue Value(QueryValue value, BaseTextFilterValueKind kind) => kind switch { BaseTextFilterValueKind.String when value.String is not null => BaseTextFilterValue.FromString(value.String), BaseTextFilterValueKind.Id when value.Id is not null => BaseTextFilterValue.FromId(value.Id), BaseTextFilterValueKind.Boolean when value.Boolean is not null => BaseTextFilterValue.FromBoolean(value.Boolean.Value), BaseTextFilterValueKind.Integer when value.Integer is not null => BaseTextFilterValue.FromInteger(value.Integer.Value), _ => throw new NotSupportedException() };
    private static OperationResult<BaseTextRuntimeResult> Copy<T>(OperationResult<T> value) => new() { Status = value.Status, Error = value.Error, Warnings = value.Warnings, Diagnostics = value.Diagnostics };
    private static OperationResult<BaseTextRuntimeResult> Fail(OperationStatus status, string code, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Message = "The text search could not be completed.", Category = category } };
}
