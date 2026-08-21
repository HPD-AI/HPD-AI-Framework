using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed class DefaultBaseTextRuntime(
    IEnumerable<IBaseTextProvider> providers,
    IBasePolicyOrchestrator policy,
    IBaseRecordRedactor redactor,
    BaseTextCursorCodec cursors,
    BaseTextConsistencyTokenCodec consistencyTokens,
    TimeProvider timeProvider,
    BaseTextOperationalState operationalState) : IBaseTextRuntime
{
    public async ValueTask<OperationResult<BaseTextRuntimeResult>> ExecuteAsync(BaseTextRuntimeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        BaseTextIndexDefinition? installedIndex = request.Collection.TextIndexes?.SingleOrDefault(value => value.Id == request.Index.Id && value.Version == request.Index.Version);
        if (installedIndex is null || !installedIndex.DefinitionChecksum.AsSpan().SequenceEqual(request.Index.DefinitionChecksum.AsSpan())) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.ContractInvalid, ErrorCategory.Validation);
        BaseTextIndexDefinition sealedIndex;
        try { sealedIndex = BaseTextIndexContract.Seal(request.Index); }
        catch { return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.ContractInvalid, ErrorCategory.Validation); }
        if (!sealedIndex.DefinitionChecksum.AsSpan().SequenceEqual(installedIndex.DefinitionChecksum.AsSpan())) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.ContractInvalid, ErrorCategory.Validation);
        if (request.Take is < 1 || request.Take > request.Index.Limits.MaximumResults) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        if (request.Index.Audience != request.Operation.Audience || request.Index.Fields.Any(field => !field.StaticInfluenceAudiences.Contains(request.Operation.Audience))) return Fail(OperationStatus.PolicyDenied, BaseTextErrorCodes.Unauthorized, ErrorCategory.Authorization);
        BaseTextCandidateConstraint callerConstraint;
        ImmutableArray<byte> queryBytes;
        try { BaseTextQueryContract.Validate(request.Query); queryBytes = BaseTextQueryContract.Encode(request.Query); callerConstraint = BaseTextConstraintContract.Normalize(request.Constraint, request.Index); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException) { return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.QueryInvalid, ErrorCategory.Validation); }
        (int queryNodes, int queryDepth, int phraseTerms) = QueryShape(request.Query);
        if (queryNodes > request.Index.Limits.MaximumQueryNodes || queryDepth > request.Index.Limits.MaximumQueryDepth || phraseTerms > request.Index.Limits.MaximumPhraseTerms || queryBytes.Length > request.Index.Limits.MaximumQueryBytes) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        if (!QueryFields(request.Query).All(id => request.Index.Fields.Any(field => field.StableFieldId == id))) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.QueryInvalid, ErrorCategory.Validation);
        OperationResult<BasePolicyEvaluation> influence = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = request.Principal, Operation = request.Operation, Collection = request.Collection, ResourceKind = PolicyResourceKind.TextIndex, TextIndexId = request.Index.Id }, cancellationToken).ConfigureAwait(false);
        if (!influence.Status.IsSuccess() || influence.Value is null || !BaseSystemCollectionGate.HasExactTextGrant(influence, BaseTextGrants.Query, request.Principal, request.Operation, request.Collection.Id, request.Index.Id)) return Fail(OperationStatus.PolicyDenied, BaseTextErrorCodes.Unauthorized, ErrorCategory.Authorization);
        BaseTextCandidateConstraint effective;
        ImmutableArray<BaseTextFieldInfluenceConstraint> fieldInfluences;
        try
        {
            effective = BaseTextConstraintContract.Normalize(Combine(callerConstraint, LowerPolicy(influence.Value.EffectiveRecordFilter, request.Index)), request.Index);
            fieldInfluences = InfluenceFields(request.Query, request.Index).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(fieldId =>
            {
                BaseTextIndexFieldDefinition field = request.Index.Fields.Single(value => value.StableFieldId == fieldId);
                bool supplied = influence.Value.EffectiveTextSearchInfluenceFilters.TryGetValue(fieldId, out FilterExpression? filter);
                if (field.RequiresDynamicInfluenceConstraint && !supplied) throw new NotSupportedException();
                BaseTextCandidateConstraint constraint = supplied ? LowerPolicy(filter, request.Index) : new BaseTextCandidateConstraint.True();
                return new BaseTextFieldInfluenceConstraint { StableFieldId = fieldId, Constraint = constraint, ConstraintDigest = BaseTextSemanticEvaluator.ConstraintDigest(constraint) };
            }).ToImmutableArray();
        }
        catch (NotSupportedException) { return Fail(OperationStatus.Unsupported, BaseTextErrorCodes.PolicyConstraintUnsupported, ErrorCategory.Unsupported); }
        (int filterNodes, int filterDepth, int filterLiterals, int maximumIn) = ConstraintShape(effective);
        if (filterNodes > request.Index.Limits.MaximumFilterNodes || filterDepth > request.Index.Limits.MaximumFilterDepth || filterLiterals > request.Index.Limits.MaximumFilterLiterals || maximumIn > request.Index.Limits.MaximumInValues) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        ImmutableArray<byte> queryDigest = BaseTextQueryContract.Digest(request.Query); ImmutableArray<byte> constraintDigest = BaseTextSemanticEvaluator.ConstraintDigest(effective);
        ImmutableArray<byte> cursorAuthorityDigest = CursorAuthorityDigest(request, influence.Value, fieldInfluences);
        IBaseTextProvider[] installed = providers.ToArray(); if (installed.Length != 1) return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);
        IBaseTextProvider provider = installed[0]; if (!ValidProviderDescriptor(provider.Descriptor)) return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.CapabilityUnavailable, ErrorCategory.Capability); IBaseTextAuthority authority = provider.Authority; DateTimeOffset deadline = checked(timeProvider.GetUtcNow() + request.Index.Limits.QueryTimeout);
        if (request.Consistency is BaseTextConsistencyRequirement.BoundedStaleness bounded && (bounded.MaximumAge <= TimeSpan.Zero || bounded.MaximumAge > TimeSpan.FromDays(30))) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.QueryInvalid, ErrorCategory.Validation);
        OperationResult<IBaseTextHydrationSession> opened;
        try { opened = await operationalState.InvokeAsync(token => authority.OpenAsync(new BaseTextAuthorityOpenRequest { CollectionId = request.Collection.Id, TextIndexId = request.Index.Id, TextIndexVersion = request.Index.Version, Consistency = request.Consistency, DeadlineUtc = deadline, CorrelationId = request.Operation.CorrelationId ?? string.Empty }, token), request.Index.Limits.ConsistencyWaitTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch { return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.IndexUnavailable, ErrorCategory.Capability); }
        if (!opened.Status.IsSuccess() || opened.Value is null) return Copy(opened);
        await using IBaseTextHydrationSession session = opened.Value;
        if (request.Consistency is BaseTextConsistencyRequirement.AtLeast atLeast && !consistencyTokens.Satisfied(atLeast.Token, session.Snapshot)) return Fail(OperationStatus.Conflict, BaseTextErrorCodes.ConsistencyUnavailable, ErrorCategory.Conflict);
        ImmutableArray<byte>? afterBoundary = null;
        if (request.After is { } supplied)
        {
            BaseTextCursorReadStatus cursorStatus = cursors.Read(supplied, session.Snapshot, queryDigest.AsSpan(), constraintDigest.AsSpan(), cursorAuthorityDigest.AsSpan(), out ImmutableArray<byte> decoded);
            if (cursorStatus != BaseTextCursorReadStatus.Valid) return cursorStatus switch
            {
                BaseTextCursorReadStatus.Expired => Fail(OperationStatus.Conflict, BaseTextErrorCodes.CursorExpired, ErrorCategory.Conflict),
                BaseTextCursorReadStatus.ScopeMismatch => Fail(OperationStatus.PolicyDenied, BaseTextErrorCodes.CursorScopeMismatch, ErrorCategory.Authorization),
                _ => Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.CursorInvalid, ErrorCategory.Validation),
            };
            afterBoundary = decoded;
        }
        OperationResult<BaseTextConstraintPreparation> prepared;
        try { prepared = await operationalState.InvokeAsync(token => session.PrepareAsync(new BaseTextProviderPreparationRequest { Snapshot = session.Snapshot, Index = request.Index, NormalizedQuery = request.Query, QueryDigest = queryDigest, Constraint = effective, ConstraintDigest = constraintDigest, InfluenceConstraints = fieldInfluences, Limits = request.Index.Limits }, token), request.Index.Limits.QueryTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch { return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.IndexUnavailable, ErrorCategory.Capability); }
        BaseTextLoweringReceipt expectedLowering = BaseTextProviderEvidence.CreateLoweringReceipt(provider.Descriptor, session.Snapshot, request.Index, queryDigest, constraintDigest, fieldInfluences, request.Index.Limits);
        if (!prepared.Status.IsSuccess()) return Copy(prepared);
        if (prepared.Value is null || prepared.Value.Enforcement != BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking || !prepared.Value.QueryDigest.AsSpan().SequenceEqual(queryDigest.AsSpan()) || !prepared.Value.ConstraintDigest.AsSpan().SequenceEqual(constraintDigest.AsSpan()) || !BaseTextProviderEvidence.LoweringEquals(prepared.Value.Receipt, expectedLowering)) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        OperationResult<BaseTextProviderResult> searched;
        try { searched = await operationalState.InvokeAsync(token => session.SearchAsync(new BaseTextExecutionRequest { Snapshot = session.Snapshot, Plan = prepared.Value.Plan, TakePlusOne = checked(request.Take + 1), AfterBoundary = afterBoundary, Limits = request.Index.Limits, DeadlineUtc = deadline, CorrelationId = request.Operation.CorrelationId ?? string.Empty }, token), request.Index.Limits.QueryTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch { return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.IndexUnavailable, ErrorCategory.Capability); }
        if (!searched.Status.IsSuccess()) return Copy(searched);
        if (searched.Value is null || !ValidProviderResult(searched.Value, provider.Descriptor, prepared.Value.Receipt, request.Query, effective, session.Snapshot, request.Take + 1, afterBoundary, request.Index.Limits)) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
        BaseTextCandidate[] candidates = searched.Value.Candidates.ToArray();
        OperationResult<RecordEnvelope[]> hydrated;
        try { hydrated = await operationalState.InvokeAsync(token => session.GetExactAsync(request.Collection, candidates.Select(static item => new BaseTextCandidateIdentity(item.RecordId, item.Revision, item.IndexedPosition)).ToArray(), request.Operation, token), request.Index.Limits.QueryTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Fail(OperationStatus.StoreError, BaseTextErrorCodes.Timeout, ErrorCategory.Store); }
        catch { return Fail(OperationStatus.CapabilityUnavailable, BaseTextErrorCodes.IndexUnavailable, ErrorCategory.Capability); }
        if (!hydrated.Status.IsSuccess()) return Copy(hydrated);
        if (hydrated.Value is null || hydrated.Value.Length != candidates.Length) return Fail(OperationStatus.Conflict, BaseTextErrorCodes.SnapshotChanged, ErrorCategory.Conflict);
        var matches = ImmutableArray.CreateBuilder<BaseTextRuntimeMatch>(Math.Min(request.Take, candidates.Length));
        long resultBytes = 0;
        for (int index = 0; index < candidates.Length; index++)
        {
            RecordEnvelope record = hydrated.Value[index]; BaseTextCandidate candidate = candidates[index];
            if (record.Id != candidate.RecordId || record.Metadata.Revision != candidate.Revision || !BaseRecordFilterMatcher.Matches(record, influence.Value.EffectiveRecordFilter) || !BaseTextSemanticEvaluator.ConstraintMatches(record.Payload, request.Index, callerConstraint)) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
            BaseTextEvaluatedCandidate? verified = BaseTextSemanticEvaluator.Evaluate(record.Payload, request.Index, request.Query, queryDigest, fieldInfluences);
            if (verified is null || verified.Score != candidate.Score || !verified.Proof.ProofDigest.AsSpan().SequenceEqual(candidate.ScoreProof.ProofDigest.AsSpan())) return Fail(OperationStatus.StoreError, BaseTextErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
            OperationResult<BasePolicyEvaluation> disclosure = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = request.Principal, Operation = request.Operation, Collection = request.Collection, ResourceKind = PolicyResourceKind.Record, ExistingRecord = record, RecordId = record.Id }, cancellationToken).ConfigureAwait(false);
            if (!disclosure.Status.IsSuccess() || disclosure.Value is null || !BaseRecordFilterMatcher.Matches(record, disclosure.Value.EffectiveRecordFilter)) return Fail(OperationStatus.PolicyDenied, BaseTextErrorCodes.Unauthorized, ErrorCategory.Authorization);
            if (index < request.Take)
            {
                RecordEnvelope projected = redactor.RedactRecord(record, request.Collection, disclosure.Value, VisibilityLevel.Authenticated);
                resultBytes = checked(resultBytes + JsonSerializer.SerializeToUtf8Bytes(projected.Payload, HPDBaseJsonSerializerContext.Default.RecordPayload).LongLength + candidate.CanonicalOrderingBoundary.Length + sizeof(ulong));
                if (resultBytes > request.Index.Limits.MaximumResultBytes) return Fail(OperationStatus.ValidationFailed, BaseTextErrorCodes.BudgetExceeded, ErrorCategory.Validation);
                matches.Add(new BaseTextRuntimeMatch { Record = projected, Score = candidate.Score, Revision = candidate.Revision });
            }
        }
        BaseTextCursor? next = candidates.Length > request.Take ? cursors.Issue(session.Snapshot, queryDigest.AsSpan(), constraintDigest.AsSpan(), cursorAuthorityDigest.AsSpan(), candidates[request.Take - 1].CanonicalOrderingBoundary.AsSpan()) : null;
        return OperationResults.Ok(new BaseTextRuntimeResult { Matches = matches.ToImmutable(), Next = next, Consistency = consistencyTokens.Issue(session.Snapshot) });
    }

    private static IEnumerable<string> QueryFields(BaseTextQuery query) => query switch { BaseTextQuery.Field value => [value.StableFieldId, .. QueryFields(value.Child)], BaseTextQuery.And value => value.Children.SelectMany(QueryFields), BaseTextQuery.Or value => value.Children.SelectMany(QueryFields), BaseTextQuery.Not value => QueryFields(value.Child), _ => [] };
    private static ImmutableArray<byte> CursorAuthorityDigest(BaseTextRuntimeRequest request, BasePolicyEvaluation evaluation, ImmutableArray<BaseTextFieldInfluenceConstraint> influences)
    {
        using var stream = new MemoryStream();
        static void Write(Stream target, string? value) { byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty); Span<byte> count = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(count, checked((uint)bytes.Length)); target.Write(count); target.Write(bytes); }
        stream.Write(Encoding.ASCII.GetBytes("HPDB-TEXT-CURSOR-AUTHORITY-1\0")); Write(stream, request.Operation.ApplicationId); Write(stream, request.Operation.TenantId); Write(stream, request.Operation.ProjectId); Write(stream, request.Operation.Audience.ToString()); Write(stream, request.Principal.SubjectKind.ToString()); Write(stream, request.Principal.SubjectId); Write(stream, request.Principal.CurrentTenantId);
        if (evaluation.Authority is null) throw new InvalidOperationException("Text search requires installed policy authority.");
        stream.Write(evaluation.Authority.Checksum.ToArray());
        foreach (BaseTextFieldInfluenceConstraint influence in influences) { Write(stream, influence.StableFieldId); stream.Write(influence.ConstraintDigest.AsSpan()); }
        return ImmutableArray.Create(SHA256.HashData(stream.ToArray()));
    }
    private static bool ValidProviderDescriptor(BaseTextProviderDescriptor value) => !string.IsNullOrWhiteSpace(value.Id) && value.Version > 0 && Enum.IsDefined(value.ProviderClass) && value.Capability.ProviderClass == value.ProviderClass && value.CertificationReceipt.Length == 32 && !value.NativeDependencyReceipts.IsDefault && value.NativeDependencyReceipts.All(static receipt => !string.IsNullOrWhiteSpace(receipt)) && value.NativeDependencyReceipts.SequenceEqual(value.NativeDependencyReceipts.Order(StringComparer.Ordinal), StringComparer.Ordinal) && value.NativeDependencyReceipts.Distinct(StringComparer.Ordinal).Count() == value.NativeDependencyReceipts.Length;
    private static IEnumerable<string> InfluenceFields(BaseTextQuery query, BaseTextIndexDefinition index) => query switch
    {
        BaseTextQuery.Field value => [value.StableFieldId],
        BaseTextQuery.And value => value.Children.SelectMany(child => InfluenceFields(child, index)),
        BaseTextQuery.Or value => value.Children.SelectMany(child => InfluenceFields(child, index)),
        BaseTextQuery.Not value => InfluenceFields(value.Child, index),
        _ => index.Fields.Select(static field => field.StableFieldId),
    };
    private static bool ValidProviderResult(BaseTextProviderResult result, BaseTextProviderDescriptor provider, BaseTextLoweringReceipt lowering, BaseTextQuery query, BaseTextCandidateConstraint constraint, BaseTextAuthoritySnapshot snapshot, int takePlusOne, ImmutableArray<byte>? after, BaseTextExecutionLimits limits)
    {
        BaseTextCompletenessEvidence expectedCompleteness = BaseTextProviderEvidence.CreateCompleteness(provider, snapshot, lowering, result.Candidates, takePlusOne);
        long queryBytes = BaseTextQueryContract.Encode(query).Length;
        long constraintBytes = BaseTextSemanticEvaluator.ConstraintEncoding(constraint).Length;
        long parameters = BaseTextProviderEvidence.StatementParameterCount(query, constraint);
        if (result.Snapshot != snapshot || !snapshot.AnalyzerReceipt.AsSpan().SequenceEqual(BaseTextContractReceipts.AnalyzerReceipt.AsSpan()) || !snapshot.ScoringReceipt.AsSpan().SequenceEqual(BaseTextContractReceipts.ScoringReceipt.AsSpan())
            || result.Candidates.Length > takePlusOne || !BaseTextProviderEvidence.CompletenessEquals(result.Completeness, expectedCompleteness)
            || result.Accounting.InputBytes != checked(queryBytes + constraintBytes) || result.Accounting.QueryBytes != queryBytes || result.Accounting.ConstraintBytes != constraintBytes || result.Accounting.StatementParameters != parameters || parameters > limits.MaximumStatementParameters
            || result.Accounting.ExactHydrationBytes != 0 || result.Accounting.ResultBytes != 0 || result.Accounting.CursorBytes != 0 || result.Accounting.CandidateCount != result.Candidates.Length
            || result.Accounting.AuthorizedRecordsExamined < result.Candidates.Length || result.Accounting.PostingsExamined < 0 || result.Accounting.PrefixExpansionCount < 0 || result.Accounting.PrefixExpansionCount > limits.MaximumPrefixExpansions || result.Accounting.PrefixExpansionBytes < 0 || result.Accounting.PrefixExpansionBytes > limits.MaximumPrefixExpansionBytes || result.Accounting.ScoreProofBytes < 0 || result.Accounting.ScoreProofBytes > limits.MaximumScoreProofBytes || result.Accounting.OrderingBytes < 0 || result.Accounting.OrderingBytes > limits.MaximumOrderingBytes || result.Accounting.RetainedTransientBytes < checked(result.Accounting.InputBytes + result.Accounting.ScoreProofBytes + result.Accounting.OrderingBytes + result.Accounting.PrefixExpansionBytes) || result.Accounting.RetainedTransientBytes > limits.MaximumTransientBytes || result.Candidates.Length > limits.MaximumCandidates || result.Accounting.Elapsed < TimeSpan.Zero || result.Accounting.Elapsed > limits.QueryTimeout) return false;
        var ids = new HashSet<RecordId>(); ImmutableArray<byte>? prior = after;
        long exactProofBytes = 0, exactOrderingBytes = 0, exactPrefixCount = 0, exactPrefixBytes = 0;
        foreach (BaseTextCandidate candidate in result.Candidates)
        {
            ImmutableArray<byte> expected = BaseTextSemanticEvaluator.OrderingBoundary(candidate.Score, candidate.RecordId);
            if (string.IsNullOrWhiteSpace(candidate.RecordId.Value) || string.IsNullOrWhiteSpace(candidate.Revision.Value) || !ids.Add(candidate.RecordId)
                || candidate.IndexedPosition.Value < 0 || candidate.IndexedPosition.Value > snapshot.SearchVisibleThrough.Value
                || !expected.AsSpan().SequenceEqual(candidate.CanonicalOrderingBoundary.AsSpan()) || prior is { } boundary && boundary.AsSpan().SequenceCompareTo(expected.AsSpan()) >= 0
                || candidate.ScoreProof.ProofDigest.Length != 32) return false;
            try
            {
                exactProofBytes = checked(exactProofBytes + BaseTextSemanticEvaluator.ProofRetainedBytes(candidate.ScoreProof));
                exactOrderingBytes = checked(exactOrderingBytes + candidate.CanonicalOrderingBoundary.Length);
                exactPrefixCount = checked(exactPrefixCount + BaseTextSemanticEvaluator.PrefixExpansionCount(candidate.ScoreProof));
                exactPrefixBytes = checked(exactPrefixBytes + BaseTextSemanticEvaluator.PrefixExpansionBytes(candidate.ScoreProof));
            }
            catch (OverflowException) { return false; }
            prior = expected;
        }
        return result.Accounting.ScoreProofBytes == exactProofBytes
            && result.Accounting.OrderingBytes == exactOrderingBytes
            && result.Accounting.PrefixExpansionCount == exactPrefixCount
            && result.Accounting.PrefixExpansionBytes == exactPrefixBytes;
    }
    private static (int Nodes, int Depth, int PhraseTerms) QueryShape(BaseTextQuery query)
    {
        static (int Nodes, int Depth, int PhraseTerms) Visit(BaseTextQuery node) => node switch
        {
            BaseTextQuery.Term or BaseTextQuery.Prefix => (1, 1, 0), BaseTextQuery.Phrase phrase => (1, 1, phrase.Terms.Length),
            BaseTextQuery.Field field => Add(Visit(field.Child)), BaseTextQuery.Not not => Add(Visit(not.Child)),
            BaseTextQuery.And and => Combine(and.Children.Select(Visit)), BaseTextQuery.Or or => Combine(or.Children.Select(Visit)), _ => (int.MaxValue, int.MaxValue, int.MaxValue),
        };
        static (int, int, int) Add((int Nodes, int Depth, int PhraseTerms) value) => (checked(value.Nodes + 1), checked(value.Depth + 1), value.PhraseTerms);
        static (int, int, int) Combine(IEnumerable<(int Nodes, int Depth, int PhraseTerms)> values) { var array = values.ToArray(); return array.Length == 0 ? (int.MaxValue, int.MaxValue, int.MaxValue) : (checked(1 + array.Sum(static value => value.Nodes)), checked(1 + array.Max(static value => value.Depth)), array.Max(static value => value.PhraseTerms)); }
        return Visit(query);
    }
    private static (int Nodes, int Depth, int Literals, int MaximumIn) ConstraintShape(BaseTextCandidateConstraint constraint)
    {
        static (int Nodes, int Depth, int Literals, int MaximumIn) Visit(BaseTextCandidateConstraint node) => node switch
        {
            BaseTextCandidateConstraint.True or BaseTextCandidateConstraint.False or BaseTextCandidateConstraint.IsMissing or BaseTextCandidateConstraint.IsNull => (1, 1, 0, 0),
            BaseTextCandidateConstraint.Equal => (1, 1, 1, 0), BaseTextCandidateConstraint.In inside => (1, 1, inside.Values.Length, inside.Values.Length),
            BaseTextCandidateConstraint.And and => Combine(and.Children.Select(Visit)), BaseTextCandidateConstraint.Or or => Combine(or.Children.Select(Visit)), _ => (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue),
        };
        static (int, int, int, int) Combine(IEnumerable<(int Nodes, int Depth, int Literals, int MaximumIn)> values) { var array = values.ToArray(); return array.Length == 0 ? (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue) : (checked(1 + array.Sum(static value => value.Nodes)), checked(1 + array.Max(static value => value.Depth)), checked(array.Sum(static value => value.Literals)), array.Max(static value => value.MaximumIn)); }
        return Visit(constraint);
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
