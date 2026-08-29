using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal struct BaseSubjectCanonicalRetainedWork
{
    private long _bytes;

    internal readonly long Bytes => _bytes;

    internal void AddContainer() => Add(8);
    internal void AddSequence(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Add(checked(8L + count * 8L));
    }
    internal void AddInteger() => Add(8);
    internal void AddBoolean() => Add(1);
    internal void AddFixed16() => Add(16);
    internal void AddFixed24() => Add(24);
    internal void AddString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Add(checked(4L + Encoding.UTF8.GetByteCount(value)));
    }
    internal void AddNullableString(string? value)
    {
        Add(1);
        if (value is not null) AddString(value);
    }
    internal void AddBytes(long length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Add(checked(4L + length));
    }
    internal void AddNullableFixed16(bool present)
    {
        Add(1);
        if (present) AddFixed16();
    }
    internal void AddNullableFixed24(bool present)
    {
        Add(1);
        if (present) AddFixed24();
    }
    internal void AddNullableBoolean(bool? value)
    {
        Add(1);
        if (value.HasValue) AddBoolean();
    }
    internal void AddNullableInteger(bool present)
    {
        Add(1);
        if (present) AddInteger();
    }
    internal void Add(long bytes) => _bytes = checked(_bytes + bytes);

    internal static long MeasureOverlay(BasePreparedSubjectOverlayEvidence value)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(value.ContractId);
        counter.AddInteger();
        counter.AddString(value.SubjectId.Value);
        counter.AddBoolean();
        counter.AddNullableFixed24(value.Incarnation is not null);
        counter.AddNullableBoolean(value.Active);
        counter.AddNullableString(value.Scope);
        counter.AddInteger();
        counter.AddBytes(value.ProtectedScope.IndexDigest.LongLength);
        counter.AddBytes(value.ProtectedScope.ProtectedCanonicalValue.LongLength);
        counter.AddNullableInteger(value.LifecycleState.HasValue);
        counter.AddNullableInteger(value.SubjectSequence.HasValue);
        return counter.Bytes;
    }

    internal static long MeasureAuthority(BaseSubjectTransactionAuthorityEvidence value)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(value.ContractId);
        counter.AddInteger();
        counter.AddString(value.ContractChecksum);
        counter.AddString(value.StoreInstanceId);
        counter.AddInteger();
        counter.AddInteger();
        counter.AddInteger();
        counter.AddFixed16();
        return counter.Bytes;
    }

    internal static long MeasureValidation(BasePreparedSubjectValidationEvidence value)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddInteger(); counter.AddInteger(); counter.AddInteger();
        counter.AddString(value.SourceFieldId);
        return counter.Bytes;
    }

    internal static long MeasureIntervals(IReadOnlyCollection<BaseAtomicReadIntervalEvidence> values)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddSequence(values.Count);
        foreach (BaseAtomicReadIntervalEvidence value in values)
        {
            counter.AddContainer();
            counter.AddString(value.LogicalAccessPathId);
            counter.AddBytes(value.CanonicalLowerBound.Length);
            counter.AddBoolean();
            counter.AddBytes(value.CanonicalUpperBound.Length);
            counter.AddBoolean();
        }
        return counter.Bytes;
    }

    internal static long MeasureLifecycleIntervals(IReadOnlyCollection<BaseReadIntervalEvidence> values)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddSequence(values.Count);
        foreach (BaseReadIntervalEvidence value in values)
        {
            counter.AddContainer();
            counter.AddString(value.LogicalAccessPathId);
            counter.AddBytes(value.LowerInclusive.LongLength);
            counter.AddBytes(value.UpperInclusive.LongLength);
        }
        return counter.Bytes;
    }

    internal static long MeasureLifecycleProviderFact(BaseSubjectLifecycleProviderFact value)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        AddLifecycleBoundary(ref counter, value.Boundary);
        AddProtectedScope(ref counter, value.Scope);
        AddLifecycleFact(ref counter, value.Fact);
        counter.AddString(value.ConsumerId);
        counter.AddInteger();
        counter.AddString(value.ConsumerChecksum);
        counter.AddInteger();
        counter.AddInteger();
        return counter.Bytes;
    }

    internal static long MeasureLifecycleProviderFacts(IReadOnlyCollection<BaseSubjectLifecycleProviderFact> values)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddSequence(values.Count);
        foreach (BaseSubjectLifecycleProviderFact value in values) counter.Add(MeasureLifecycleProviderFact(value));
        return counter.Bytes;
    }

    internal static long MeasureRetirementPreparedEvidence(BaseSubjectRetirementPreparedEvidence? value)
    {
        if (value is null) return 0;
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer(); counter.AddString(value.PlanChecksum); counter.AddSequence(value.Items.Length);
        foreach (BaseSubjectRetirementPreparedEvidenceItem item in value.Items)
        {
            counter.AddContainer(); counter.AddInteger(); counter.Add(1);
            if (item.Previous is not null) AddRetirementBarrier(ref counter, item.Previous);
            AddRetirementBarrier(ref counter, item.Resulting); AddProtectedScope(ref counter, item.ProtectedScope); counter.AddInteger();
        }
        return counter.Bytes;
    }

    internal static long MeasureRetirementPublication(BaseSubjectRetirementPublicationFact value)
    {
        var counter = new BaseSubjectCanonicalRetainedWork(); counter.AddContainer(); counter.AddInteger(); counter.AddInteger();
        if (value.Barrier is { } barrier)
        {
            counter.AddContainer(); counter.AddString(barrier.ContractId); counter.AddInteger(); counter.AddString(barrier.SubjectId.Value);
            counter.AddFixed16(); counter.AddFixed24(); counter.AddInteger(); counter.AddInteger(); counter.AddInteger(); counter.AddNullableString(barrier.ConsumerId);
        }
        else if (value.AdvisoryAcknowledgement is { } advisory)
        {
            counter.AddContainer(); counter.AddString(advisory.ContractId); counter.AddInteger(); counter.AddString(advisory.SubjectId.Value);
            counter.AddFixed16(); counter.AddFixed24(); counter.AddInteger(); counter.AddString(advisory.ConsumerId); counter.AddInteger(); counter.AddInteger();
        }
        else if (value.Purged is { } purged)
        {
            counter.AddContainer(); counter.AddString(purged.ContractId); counter.AddInteger(); counter.AddString(purged.SubjectId.Value);
            counter.AddFixed16(); counter.AddFixed24(); counter.AddInteger(); counter.AddInteger(); counter.AddString(purged.FinalBarrierChecksum);
            counter.AddString(purged.TerminalReceiptChecksum); counter.AddInteger();
        }
        else if (value.ConsumerSet is { } set)
        {
            counter.AddContainer(); counter.AddString(set.ContractId); counter.AddInteger(); counter.AddString(set.PreviousConsumerSetChecksum);
            counter.AddString(set.PublishedConsumerSetChecksum); counter.AddInteger(); counter.AddInteger(); counter.AddNullableString(set.RemovedConsumerId);
        }
        else if (value.Restore is { } restore)
        {
            counter.AddContainer(); counter.AddString(restore.ContractId); counter.AddInteger(); counter.AddInteger(); counter.AddInteger();
            counter.AddInteger(); counter.AddInteger(); counter.AddInteger(); counter.AddString(restore.TransformationChecksum);
        }
        else throw new ArgumentException("The retirement publication payload is missing.", nameof(value));
        return counter.Bytes;
    }

    private static void AddRetirementBarrier(ref BaseSubjectCanonicalRetainedWork counter, BaseSubjectRetirementBarrier value)
    {
        counter.AddContainer(); counter.AddString(value.ContractId); counter.AddInteger(); counter.AddString(value.SubjectId.Value);
        counter.AddFixed16(); counter.AddFixed24(); counter.AddInteger(); counter.AddString(value.RequiredConsumerSetChecksum);
        counter.AddInteger(); counter.AddInteger(); counter.AddInteger(); counter.AddInteger(); counter.AddString(value.BarrierChecksum);
    }

    private static void AddLifecycleFact(ref BaseSubjectCanonicalRetainedWork counter, BaseSubjectLifecycleFact value)
    {
        counter.AddContainer(); counter.AddInteger();
        counter.AddString(value.ContractId); counter.AddInteger(); counter.AddString(value.SubjectId.Value);
        counter.AddFixed16(); counter.AddFixed24();
        counter.AddInteger(); counter.AddInteger(); counter.AddInteger(); counter.AddInteger();
        counter.Add(1);
        switch (value.Kind)
        {
            case BaseSubjectLifecycleFactKind.Created:
                counter.AddContainer(); counter.AddInteger(); break;
            case BaseSubjectLifecycleFactKind.Transitioned:
                counter.AddContainer(); counter.AddInteger(); counter.AddInteger(); break;
            case BaseSubjectLifecycleFactKind.Retired:
                counter.AddContainer(); counter.AddInteger(); break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void AddLifecycleBoundary(ref BaseSubjectCanonicalRetainedWork counter, BaseSubjectLifecycleOrderingBoundary value)
    {
        counter.AddContainer(); counter.AddInteger(); counter.AddString(value.SubjectId.Value);
        counter.AddFixed16(); counter.AddFixed24(); counter.AddInteger();
    }

    private static void AddProtectedScope(ref BaseSubjectCanonicalRetainedWork counter, BaseProtectedSubjectScope value)
    {
        counter.AddContainer(); counter.AddInteger(); counter.AddBytes(value.IndexDigest.LongLength);
        counter.AddBytes(value.ProtectedCanonicalValue.LongLength);
    }

    internal static long MeasurePreparedEvidence(
        BasePreparedSubjectOverlayEvidence[] overlays,
        BaseSubjectTransactionAuthorityEvidence[] authorities,
        BasePreparedSubjectValidationEvidence[] validations)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddSequence(overlays.Length);
        foreach (BasePreparedSubjectOverlayEvidence value in overlays) counter.Add(MeasureOverlay(value));
        counter.AddSequence(authorities.Length);
        foreach (BaseSubjectTransactionAuthorityEvidence value in authorities) counter.Add(MeasureAuthority(value));
        counter.AddSequence(validations.Length);
        foreach (BasePreparedSubjectValidationEvidence value in validations) counter.Add(MeasureValidation(value));
        return counter.Bytes;
    }

    internal static long MeasureStringDictionary<T>(
        IReadOnlyDictionary<string, T> values,
        Func<T, long>? measureValue = null)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((string key, T value) in values)
        {
            counter.Add(16); counter.AddString(key);
            if (measureValue is not null) counter.Add(measureValue(value));
        }
        return counter.Bytes;
    }

    internal static long MeasureIntegerDictionary<T>(
        IReadOnlyDictionary<int, T> values,
        Func<T, long> measureValue)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        foreach ((int _, T value) in values)
        {
            counter.Add(16); counter.AddInteger(); counter.Add(measureValue(value));
        }
        return counter.Bytes;
    }

    internal static long MeasurePlan(BaseFinalizedAtomicExecutionPlan plan)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(plan.PlanDigest);
        counter.AddString(plan.IntentDigest);
        counter.AddString(plan.CaptureDigest);
        for (int index = 0; index < 8; index++) counter.AddInteger();
        counter.AddSequence(plan.Items.Length);
        foreach (BaseAtomicMutationPlanItem item in plan.Items)
        {
            counter.AddContainer();
            counter.AddInteger();
            counter.AddInteger();
            counter.AddInteger();
            counter.AddString(item.Collection.Id);
            counter.AddString(item.RecordId.Value);
            counter.AddString(item.EventId);
            counter.Add(1);
            if (item.ProposedPayload is not null)
                counter.AddBytes(JsonSerializer.SerializeToUtf8Bytes(
                    item.ProposedPayload, HPDBaseJsonSerializerContext.Default.RecordPayload).LongLength);
        }
        counter.AddSequence(plan.SubjectValidations.Length);
        foreach (BaseSubjectReferenceValidationPlanItem validation in plan.SubjectValidations)
        {
            counter.AddContainer();
            counter.AddInteger();
            counter.AddInteger();
            counter.AddInteger();
            counter.AddInteger();
            counter.AddString(validation.SourceFieldId);
            counter.AddString(validation.ValidationPlanId);
            counter.AddString(validation.Reference.SubjectId.Value);
            counter.AddNullableString(validation.Scope.Value);
            counter.AddBytes(32);
        }
        counter.Add(1);
        if (plan.Text is not null)
        {
            counter.AddContainer();
            counter.AddBytes(plan.Text.ProjectionDigest.Length);
            counter.AddSequence(plan.Text.Facts.Length);
            foreach (BaseTextProjectionFact fact in plan.Text.Facts)
            {
                counter.AddContainer(); counter.AddInteger(); counter.AddInteger(); counter.AddInteger();
                counter.AddString(fact.CollectionId); counter.AddString(fact.TextIndexId);
                counter.AddBytes(fact.TextIndexChecksum.Length); counter.AddString(fact.RecordId.Value);
                AddTextState(ref counter, fact.Before); AddTextState(ref counter, fact.After);
                counter.AddBytes(fact.FactChecksum.Length);
            }
        }
        return counter.Bytes;
    }

    private static void AddTextState(ref BaseSubjectCanonicalRetainedWork counter, BaseTextProjectionRecordState? state)
    {
        counter.Add(1);
        if (state is null) return;
        counter.AddContainer(); counter.AddNullableString(state.Revision?.Value);
        counter.AddNullableString(state.TenantId); counter.AddNullableString(state.ProjectId);
        counter.AddSequence(state.Fields.Length);
        foreach (BaseTextProjectionFieldValue field in state.Fields)
        {
            counter.AddContainer(); counter.AddString(field.StableFieldId); counter.AddBoolean();
            counter.AddBytes(field.CanonicalJsonUtf8.Length);
        }
        counter.AddBytes(state.StateChecksum.Length);
    }

    internal static long MeasureCapture(
        BaseAtomicMutationIntent intent,
        IReadOnlyCollection<BaseCapturedMutationItem> items,
        IReadOnlyCollection<BaseAtomicReadIntervalEvidence> intervals,
        IReadOnlyCollection<BaseCapturedSubjectLifecycleConsumerProjection>? lifecycleProjections = null)
    {
        var counter = new BaseSubjectCanonicalRetainedWork();
        counter.AddContainer();
        counter.AddString(intent.IntentDigest);
        counter.AddString(intent.Authority.ApplicationId);
        counter.AddString(intent.Authority.StoreInstanceId);
        counter.AddInteger(); counter.AddInteger(); counter.AddInteger(); counter.AddInteger();
        counter.AddSequence(items.Count);
        var retainedRecords = new Dictionary<string, RecordEnvelope?>(StringComparer.Ordinal);
        foreach (BaseCapturedMutationItem item in items)
        {
            counter.AddContainer(); counter.AddInteger(); counter.AddInteger(); counter.AddBoolean();
            counter.AddString(item.CollectionId); counter.AddString(item.RecordId.Value);
            AddNullableRecord(ref counter, item.Current);
            retainedRecords.TryAdd(item.CollectionId + "\n" + item.RecordId.Value, item.Current);
            counter.AddSequence(item.RelationTargets.Length);
            foreach (BaseCapturedRelationTarget relation in item.RelationTargets)
            {
                counter.AddContainer(); counter.AddString(relation.SourceFieldId);
                counter.AddString(relation.TargetCollectionId); counter.AddString(relation.TargetRecordId.Value);
                AddNullableRecord(ref counter, relation.Current);
                retainedRecords.TryAdd(relation.TargetCollectionId + "\n" + relation.TargetRecordId.Value, relation.Current);
            }
            counter.Add(1);
            if (item.SubjectLifecycleTransition is { } lifecycle)
            {
                counter.AddContainer(); counter.AddString(lifecycle.ContractId); counter.AddInteger();
                counter.AddString(lifecycle.ContractChecksum); counter.AddString(lifecycle.SubjectId.Value);
                counter.AddBytes(lifecycle.AuthorityEpoch.ToArray().Length);
                counter.AddBytes(lifecycle.Incarnation.ToArray().Length);
                counter.AddInteger(); counter.AddInteger();
            }
        }
        counter.AddSequence(lifecycleProjections?.Count ?? 0);
        if (lifecycleProjections is not null)
            foreach (BaseCapturedSubjectLifecycleConsumerProjection projection in lifecycleProjections)
            {
                counter.AddContainer(); counter.AddString(projection.ConsumerId); counter.AddInteger();
                counter.AddString(projection.ConsumerChecksum); counter.AddString(projection.ContractId);
                counter.AddInteger(); counter.AddInteger(); counter.AddInteger();
            }
        counter.AddContainer();
        foreach ((string key, RecordEnvelope? record) in retainedRecords)
        {
            counter.Add(16); counter.AddString(key); AddNullableRecord(ref counter, record);
        }
        counter.Add(MeasureIntervals(intervals));
        return counter.Bytes;
    }

    private static void AddNullableRecord(ref BaseSubjectCanonicalRetainedWork counter, RecordEnvelope? record)
    {
        counter.Add(1);
        if (record is null) return;
        counter.AddBytes(JsonSerializer.SerializeToUtf8Bytes(
            record, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength);
    }
}
