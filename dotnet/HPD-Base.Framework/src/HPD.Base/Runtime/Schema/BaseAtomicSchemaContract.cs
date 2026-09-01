using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseAtomicSchemaContract
{
    private sealed class PreparedPlan(object owner, BaseSchemaAuthorityChecksum checksum) : BaseAtomicSchemaPreparedPlan
    {
        private readonly object _owner = owner;
        private int _consumed;
        internal BaseSchemaAuthorityChecksum Checksum { get; } = checksum;

        internal bool Consume(object owner, BaseSchemaAuthorityChecksum checksum) =>
            ReferenceEquals(owner, _owner) && checksum == Checksum && Interlocked.Exchange(ref _consumed, 1) == 0;
    }

    internal static BaseAtomicSchemaPreparedExtension? Prepare(
        object session,
        BaseAtomicSchemaCaptureExtension? captured,
        BaseAtomicSchemaFinalizedExtension? finalized,
        ImmutableArray<BaseAtomicMutationPlanItem> items)
    {
        if ((captured is null) != (finalized is null))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        if (captured is null) return null;
        BaseAtomicSchemaFinalizedExtension expected = Finalize(captured, items)
            ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        if (!FinalizedMatches(expected, finalized!))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        BaseSchemaAuthorityChecksum checksum = BaseSchemaAuthorityChecksum.Create(finalized!.Checksum.ToArray());
        return new BaseAtomicSchemaPreparedExtension { Plan = new PreparedPlan(session, checksum), FinalizedChecksum = checksum };
    }

    internal static bool PreparedMatches(
        BaseAtomicSchemaFinalizedExtension? finalized,
        BaseAtomicSchemaPreparedExtension? prepared) =>
        finalized is null ? prepared is null : prepared is not null && prepared.Plan is PreparedPlan &&
        prepared.FinalizedChecksum == finalized.Checksum;

    internal static bool ConsumePrepared(
        object session,
        BaseAtomicSchemaPreparedExtension? prepared,
        BaseAtomicSchemaFinalizedExtension? finalized)
    {
        if (finalized is null) return prepared is null;
        return prepared?.Plan is PreparedPlan plan && prepared.FinalizedChecksum == finalized.Checksum &&
            plan.Consume(session, finalized.Checksum);
    }

    internal static BaseAtomicSchemaProvisionalExtension? Apply(
        object session,
        BaseAtomicSchemaPreparedExtension? prepared,
        BaseAtomicSchemaFinalizedExtension? finalized)
    {
        if (!ConsumePrepared(session, prepared, finalized))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        if (finalized is null) return null;
        return ApplyEvidence(finalized);
    }

    private static BaseAtomicSchemaProvisionalExtension ApplyEvidence(BaseAtomicSchemaFinalizedExtension finalized)
    {
        ValidateAccounting(finalized.Accounting, finalized.Limits);
        Dictionary<BaseLogicalIndexChecksum, long> generations = finalized.Authority.Indexes.ToDictionary(
            static value => value.Index, static value => value.Generation);
        HashSet<BaseLogicalIndexChecksum> changedIndexes = finalized.Indexes.Where(static transition =>
            transition.WasMember != transition.IsMember || !NullableBytesEqual(transition.OldEqualityKey, transition.NewEqualityKey))
            .Select(static value => value.IndexChecksum).ToHashSet();
        foreach (BaseLogicalIndexChecksum index in changedIndexes) generations[index] = checked(generations[index] + 1);
        var applied = ImmutableArray.CreateBuilder<BaseSchemaAppliedIndexTransition>(finalized.Indexes.Length);
        foreach (BaseAtomicIndexTransitionEvidence transition in finalized.Indexes)
        {
            long generation = generations[transition.IndexChecksum];
            BaseSchemaOverlayRecord overlay = finalized.FinalOverlay.Single(value => value.MutationOrdinal == transition.MutationOrdinal);
            BaseSchemaAuthorityChecksum checksum = AppliedChecksum(finalized, transition, overlay, generation);
            applied.Add(new BaseSchemaAppliedIndexTransition
            {
                MutationOrdinal = transition.MutationOrdinal, Index = transition.IndexChecksum,
                ResultingGeneration = generation, AppliedChecksum = checksum,
            });
        }
        ImmutableArray<BaseSchemaAppliedIndexTransition> values = applied.MoveToImmutable();
        BaseSchemaWorkAccounting accounting = finalized.Accounting with { };
        ValidateAccounting(accounting, finalized.Limits);
        BaseSchemaAuthorityChecksum provisional = ProvisionalChecksum(finalized.Checksum, values, accounting);
        return new BaseAtomicSchemaProvisionalExtension { AppliedIndexes = values, Accounting = accounting, ProvisionalChecksum = provisional };
    }

    internal static bool ProvisionalMatches(
        BaseAtomicSchemaFinalizedExtension? finalized,
        BaseAtomicSchemaProvisionalExtension? actual)
    {
        if ((finalized is null) != (actual is null)) return false;
        if (finalized is null) return true;
        try
        {
            BaseAtomicSchemaProvisionalExtension expected = ApplyEvidence(finalized);
            return actual!.Accounting == expected.Accounting && AppliedTransitionsMatch(actual.AppliedIndexes, expected.AppliedIndexes) &&
                actual.ProvisionalChecksum == expected.ProvisionalChecksum;
        }
        catch { return false; }
    }

    internal static BaseAtomicSchemaCommittedEvidence? Commit(
        BaseAtomicSchemaFinalizedExtension? finalized,
        BaseAtomicSchemaProvisionalExtension? provisional)
    {
        if (!ProvisionalMatches(finalized, provisional))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        return finalized is null ? null : new BaseAtomicSchemaCommittedEvidence
        {
            AuthorityChecksum = BaseSchemaAuthorityChecksum.Create(finalized.Authority.Checksum.ToArray()),
            FinalizedChecksum = BaseSchemaAuthorityChecksum.Create(finalized.Checksum.ToArray()),
            ProvisionalChecksum = BaseSchemaAuthorityChecksum.Create(provisional!.ProvisionalChecksum.ToArray()),
            Accounting = provisional.Accounting with { },
        };
    }

    internal static bool CommittedMatches(BaseAtomicSchemaProvisionalExtension? provisional, BaseAtomicSchemaCommittedEvidence? committed) =>
        provisional is null ? committed is null : committed is not null &&
        committed.ProvisionalChecksum == provisional.ProvisionalChecksum && committed.Accounting == provisional.Accounting;

    internal static BaseAtomicMutationExecutionLimits AttachLimits(
        BaseAtomicMutationExecutionLimits limits,
        IEnumerable<CollectionDefinition> collections)
    {
        ArgumentNullException.ThrowIfNull(limits);
        bool applies = collections.Any(Applies);
        if (!applies) return limits.Schema is null ? limits : limits with { Schema = null };
        BaseSchemaExecutionLimits schema = limits.Schema ?? PlatformLimits;
        ValidateLimits(schema);
        return limits with { Schema = schema };
    }

    internal static BaseAtomicSchemaCaptureRequest? CaptureRequest(
        BaseAtomicMutationAuthorityRequirement authority,
        IEnumerable<CollectionDefinition> collections,
        BaseAtomicMutationExecutionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(authority);
        HashSet<string> authorizedCollections = authority.Collections.Select(static value => value.CollectionId).ToHashSet(StringComparer.Ordinal);
        CollectionDefinition[] relevant = collections.Where(collection => authorizedCollections.Contains(collection.Id) && Applies(collection))
            .GroupBy(static value => value.Id, StringComparer.Ordinal).Select(static group => group.Single())
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        if (relevant.Length == 0)
            return null;
        BaseSchemaExecutionLimits schemaLimits = limits.Schema ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        ValidateLimits(schemaLimits);
        ImmutableArray<BaseCollectionSchemaRequirement> requirements = [.. relevant.Select(collection => new BaseCollectionSchemaRequirement
        {
            CollectionId = new string(collection.Id.AsSpan()),
            LogicalSchemaChecksum = authority.LogicalSchemaChecksum,
            Constraints = [.. (collection.Fields ?? []).Where(static field => field.ScalarConstraintChecksum is not null)
                .Select(static field => field.ScalarConstraintChecksum!.Value).OrderBy(static value => value.ToString(), StringComparer.Ordinal)],
            Indexes = [.. (collection.Indexes ?? []).Select(static index => index.Checksum).OrderBy(static value => value.ToString(), StringComparer.Ordinal)],
        })];
        return new BaseAtomicSchemaCaptureRequest
        {
            Requirements = requirements,
            Limits = schemaLimits,
            Checksum = Checksum(authority.LogicalSchemaChecksum, requirements, schemaLimits),
        };
    }

    internal static bool Applies(CollectionDefinition collection) =>
        (collection.Fields ?? []).Any(static field => field.ScalarConstraintChecksum is not null) ||
        (collection.Indexes ?? []).Length != 0;

    internal static BaseSchemaAuthorityChecksum InitialPublication(
        BaseSchemaAuthorityChecksum logicalSchema, string collectionId, BaseLogicalIndexChecksum index, long restoreEpoch) =>
        Digest("hpd.base.logical-index-publication.v1\0", logicalSchema.ToArray(), Encoding.UTF8.GetBytes(collectionId), index.ToArray(), BigEndian(restoreEpoch));

    internal static BaseSchemaAuthorityChecksum NextPublication(
        BaseSchemaAuthorityChecksum prior, BaseLogicalIndexChecksum index, long generation, BaseSchemaAuthorityChecksum provisional) =>
        Digest("hpd.base.logical-index-publication-next.v1\0", prior.ToArray(), index.ToArray(), BigEndian(generation), provisional.ToArray());

    internal static BaseAtomicSchemaCaptureExtension Capture(
        BaseAtomicSchemaCaptureRequest request,
        BaseAtomicMutationAuthorityEvidence authority,
        IEnumerable<CollectionDefinition> collections,
        ImmutableArray<BaseCapturedMutationItem> items,
        Func<string, BaseLogicalIndexChecksum, BaseLogicalIndexCurrentAuthority>? resolveIndex = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(authority);
        HashSet<string> requestedCollections = request.Requirements.Select(static value => value.CollectionId).ToHashSet(StringComparer.Ordinal);
        CollectionDefinition[] relevant = collections.Where(collection => requestedCollections.Contains(collection.Id) && Applies(collection))
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        if (relevant.Length != requestedCollections.Count)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        ImmutableArray<BaseCollectionSchemaRequirement> expectedRequirements = [.. relevant.Select(collection => new BaseCollectionSchemaRequirement
        {
            CollectionId = collection.Id, LogicalSchemaChecksum = authority.LogicalSchemaChecksum,
            Constraints = [.. (collection.Fields ?? []).Where(static field => field.ScalarConstraintChecksum is not null).Select(static field => field.ScalarConstraintChecksum!.Value).OrderBy(static value => value.ToString(), StringComparer.Ordinal)],
            Indexes = [.. (collection.Indexes ?? []).Select(static index => index.Checksum).OrderBy(static value => value.ToString(), StringComparer.Ordinal)],
        })];
        if (request.Checksum != Checksum(authority.LogicalSchemaChecksum, expectedRequirements, request.Limits) || !RequirementsMatch(request.Requirements, expectedRequirements))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);

        Dictionary<string, CollectionDefinition> byId = relevant.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        ImmutableArray<BaseSchemaCapturedRecord> originals = [.. items.Where(item => byId.ContainsKey(item.CollectionId))
            .OrderBy(static item => item.Ordinal).GroupBy(static item => (item.CollectionId, item.RecordId)).Select(static group => group.First()).Select(item =>
            {
                byte[]? canonical = item.Current is null ? null : CanonicalRecord(byId[item.CollectionId], item.Current.Payload);
                return new BaseSchemaCapturedRecord
                {
                    MutationOrdinal = item.Ordinal, CollectionId = new string(item.CollectionId.AsSpan()), RecordId = item.RecordId,
                    Present = item.Current is not null, CanonicalBytes = canonical?.ToImmutableArray(),
                    Revision = item.Current?.Metadata.Revision?.Value is { } revision ? new RevisionToken(revision) : null,
                    CollectionGeneration = authority.Collections.Single(value => value.CollectionId == item.CollectionId).CollectionGeneration,
                };
            })];
        ImmutableArray<BaseCollectionSchemaAuthority> collectionAuthority = [.. request.Requirements.Select(requirement => new BaseCollectionSchemaAuthority
        {
            CollectionId = new string(requirement.CollectionId.AsSpan()),
            CollectionGeneration = authority.Collections.Single(value => value.CollectionId == requirement.CollectionId).CollectionGeneration,
            LogicalSchemaChecksum = requirement.LogicalSchemaChecksum, Constraints = [.. requirement.Constraints], Indexes = [.. requirement.Indexes],
        })];
        ImmutableArray<BaseLogicalIndexCurrentAuthority> indexes = [.. relevant.SelectMany(collection => (collection.Indexes ?? []).Select(index =>
            resolveIndex?.Invoke(collection.Id, index.Checksum) ?? new BaseLogicalIndexCurrentAuthority
            {
                Index = index.Checksum, State = BaseLogicalIndexGenerationState.Ready,
                Generation = authority.Collections.Single(value => value.CollectionId == collection.Id).CollectionGeneration,
                PublicationChecksum = Publication(authority, collection.Id, index.Checksum),
            })).OrderBy(static value => value.Index.ToString(), StringComparer.Ordinal)];
        BaseAtomicSchemaAuthority schemaAuthority = new()
        {
            SchemaGeneration = authority.SchemaGeneration, Collections = collectionAuthority, Indexes = indexes,
            Checksum = AuthorityChecksum(authority, collectionAuthority, indexes),
        };
        long bytes = originals.Sum(static value => (long)(value.CanonicalBytes?.Length ?? 0));
        BaseSchemaWorkAccounting accounting = new()
        {
            Records = originals.Length, CanonicalBytes = bytes, JsonNodes = 0, ConstraintEvaluations = 0,
            PredicateEvaluations = 0, Keys = 0, KeyBytes = 0, UniqueCandidates = 0, UniqueChecks = 0,
            Intervals = 0, IntervalBytes = 0, EvidenceBytes = checked(bytes + originals.Length * 64L),
            TransientBytes = checked(bytes * 2 + originals.Length * 64L),
        };
        ValidateAccounting(accounting, request.Limits);
        return new BaseAtomicSchemaCaptureExtension { Authority = schemaAuthority, Originals = originals, Limits = request.Limits, Accounting = accounting };
    }

    internal static bool CapturedMatches(
        BaseAtomicSchemaCaptureRequest? request,
        BaseAtomicSchemaCaptureExtension? actual,
        BaseAtomicMutationAuthorityEvidence authority,
        IEnumerable<CollectionDefinition> collections,
        ImmutableArray<BaseCapturedMutationItem> items)
    {
        if ((request is null) != (actual is null)) return false;
        if (request is null) return true;
        try
        {
            Dictionary<BaseLogicalIndexChecksum, BaseLogicalIndexCurrentAuthority> capturedIndexes = actual?.Authority.Indexes.ToDictionary(static value => value.Index) ?? [];
            BaseAtomicSchemaCaptureExtension expected = Capture(request, authority, collections, items,
                (_, index) => capturedIndexes.TryGetValue(index, out BaseLogicalIndexCurrentAuthority? value) ? value : throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid));
            bool ok = actual!.Limits == request.Limits && actual.Accounting == expected.Accounting &&
                actual.Authority.Checksum == expected.Authority.Checksum && actual.Authority.SchemaGeneration == expected.Authority.SchemaGeneration &&
                CollectionAuthoritiesMatch(actual.Authority.Collections, expected.Authority.Collections) &&
                IndexAuthoritiesMatch(actual.Authority.Indexes, expected.Authority.Indexes) &&
                actual.Originals.Length == expected.Originals.Length && actual.Originals.Zip(expected.Originals).All(static pair =>
                    pair.First.MutationOrdinal == pair.Second.MutationOrdinal && pair.First.CollectionId == pair.Second.CollectionId &&
                    pair.First.RecordId == pair.Second.RecordId && pair.First.Present == pair.Second.Present &&
                    pair.First.CollectionGeneration == pair.Second.CollectionGeneration && pair.First.Revision == pair.Second.Revision &&
                    NullableBytesEqual(pair.First.CanonicalBytes, pair.Second.CanonicalBytes));
            return ok;
        }
        catch { return false; }
    }

    internal static ImmutableArray<BaseCapturedMutationItem> ModuleItems(ImmutableArray<BaseCapturedModuleRecord> records) =>
        [.. records.OrderBy(static value => value.Ordinal).Select(static value => new BaseCapturedMutationItem
        {
            Ordinal = value.Ordinal, CollectionId = new string(value.CollectionId.AsSpan()), RecordId = value.RecordId,
            Disposition = value.Exists ? BaseCapturedMutationDisposition.Update : BaseCapturedMutationDisposition.Create,
            Current = value.Current, RelationTargets = [],
        })];

    internal static BaseAtomicSchemaCaptureExtension? Freeze(BaseAtomicSchemaCaptureExtension? value) => value is null ? null : new()
    {
        Authority = new BaseAtomicSchemaAuthority
        {
            Checksum = BaseSchemaAuthorityChecksum.Create(value.Authority.Checksum.ToArray()), SchemaGeneration = value.Authority.SchemaGeneration,
            Collections = [.. value.Authority.Collections.Select(static item => new BaseCollectionSchemaAuthority
            {
                CollectionId = new string(item.CollectionId.AsSpan()), CollectionGeneration = item.CollectionGeneration,
                LogicalSchemaChecksum = BaseSchemaAuthorityChecksum.Create(item.LogicalSchemaChecksum.ToArray()),
                Constraints = [.. item.Constraints.Select(static checksum => BaseScalarConstraintChecksum.Create(checksum.ToArray()))],
                Indexes = [.. item.Indexes.Select(static checksum => BaseLogicalIndexChecksum.Create(checksum.ToArray()))],
            })],
            Indexes = [.. value.Authority.Indexes.Select(static item => new BaseLogicalIndexCurrentAuthority
            {
                Index = BaseLogicalIndexChecksum.Create(item.Index.ToArray()), State = item.State, Generation = item.Generation,
                PublicationChecksum = BaseSchemaAuthorityChecksum.Create(item.PublicationChecksum.ToArray()),
            })],
        },
        Originals = [.. value.Originals.Select(static item => item with
        {
            CollectionId = new string(item.CollectionId.AsSpan()), CanonicalBytes = item.CanonicalBytes?.ToArray().ToImmutableArray(),
            Revision = item.Revision is null ? null : new RevisionToken(new string(item.Revision.Value.Value.AsSpan())),
        })],
        Limits = value.Limits with { }, Accounting = value.Accounting with { },
    };

    internal static BaseAtomicSchemaFinalizedExtension? Finalize(
        BaseAtomicSchemaCaptureExtension? captured,
        ImmutableArray<BaseAtomicMutationPlanItem> items)
    {
        if (captured is null) return null;
        var statement = ImmutableArray.CreateBuilder<BaseSchemaOverlayRecord>(items.Length);
        var constraints = ImmutableArray.CreateBuilder<BaseAtomicConstraintEvidence>();
        var transitions = ImmutableArray.CreateBuilder<BaseAtomicIndexTransitionEvidence>();
        long canonicalBytes = captured.Accounting.CanonicalBytes, evaluations = captured.Accounting.ConstraintEvaluations;
        long predicates = captured.Accounting.PredicateEvaluations, keys = captured.Accounting.Keys, keyBytes = captured.Accounting.KeyBytes, intervals = captured.Accounting.Intervals, intervalBytes = captured.Accounting.IntervalBytes;
        BaseAtomicMutationPlanItem[] orderedItems = [.. items.OrderBy(static value => value.Ordinal)];
        foreach (BaseAtomicMutationPlanItem item in orderedItems)
        {
            if (!Applies(item.Collection)) continue;
            RecordPayload? finalPayload = item.Kind == BaseCommittedRecordMutationKind.Delete ? null : item.ProposedPayload ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            byte[]? finalBytes = finalPayload is null ? null : CanonicalRecord(item.Collection, finalPayload);
            canonicalBytes = checked(canonicalBytes + (finalBytes?.LongLength ?? 0));
            BaseSchemaAuthorityChecksum overlayDigest = OverlayDigest(item, finalBytes);
            statement.Add(new BaseSchemaOverlayRecord
            {
                MutationOrdinal = item.Ordinal, StatementOrdinal = item.Ordinal, CollectionId = item.Collection.Id, RecordId = item.RecordId,
                Disposition = item.Kind switch { BaseCommittedRecordMutationKind.Create => BaseCapturedMutationDisposition.Create, BaseCommittedRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete, _ => BaseCapturedMutationDisposition.Update },
                Present = finalPayload is not null, CanonicalBytes = finalBytes?.ToImmutableArray(), OverlayDigest = overlayDigest,
            });
        }
        ImmutableArray<BaseSchemaOverlayRecord> statementLocal = statement.ToImmutable();
        ImmutableArray<BaseSchemaOverlayRecord> finalOverlay = [.. statementLocal.GroupBy(static value => (value.CollectionId, value.RecordId)).Select(static group => group.OrderBy(value => value.StatementOrdinal).Last()).OrderBy(static value => value.CollectionId, StringComparer.Ordinal).ThenBy(static value => value.RecordId.Value, StringComparer.Ordinal)];
        foreach (IGrouping<(string CollectionId, RecordId RecordId), BaseAtomicMutationPlanItem> group in orderedItems
            .Where(static item => Applies(item.Collection)).GroupBy(static item => (item.Collection.Id, item.RecordId))
            .OrderBy(static group => group.Key.Id, StringComparer.Ordinal).ThenBy(static group => group.Key.RecordId.Value, StringComparer.Ordinal))
        {
            BaseAtomicMutationPlanItem first = group.First();
            BaseAtomicMutationPlanItem item = group.Last();
            RecordPayload? originalPayload = first.Current?.Payload;
            RecordPayload? finalPayload = item.Kind == BaseCommittedRecordMutationKind.Delete ? null : item.ProposedPayload ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            FieldDefinition[] fields = (item.Collection.Fields ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
            for (int ordinal = 0; finalPayload is not null && ordinal < fields.Length; ordinal++)
            {
                FieldDefinition field = fields[ordinal]; if (field.ScalarConstraintChecksum is not { } constraint) continue;
                JsonElement value = default; bool present = finalPayload?.Fields?.TryGetValue(field.WireName, out value) == true;
                BaseFieldState state = !present
                    ? new BaseMissingFieldState()
                    : value.ValueKind == JsonValueKind.Null
                        ? new BasePresentNullFieldState()
                        : new BasePresentValueFieldState
                        {
                            CanonicalBytes = (field.ScalarKind == BaseScalarKind.FrozenArray
                                ? Compact(value)
                                : BaseScalarCanonical.Encode(field.ScalarKind!.Value, value)).ToImmutableArray(),
                        };
                if (present && value.ValueKind != JsonValueKind.Null && BaseCanonicalRecordValidator.Validate(field, value) is not null) throw new InvalidOperationException(BaseSchemaErrorCodes.ScalarConstraintViolated);
                evaluations = checked(evaluations + 1);
                constraints.Add(new BaseAtomicConstraintEvidence { MutationOrdinal = item.Ordinal, FieldOrdinal = ordinal, ConstraintChecksum = constraint, State = state, Accounting = ZeroAccounting() with { ConstraintEvaluations = 1 } });
            }
            foreach (BaseLogicalIndexDefinition index in item.Collection.Indexes ?? [])
            {
                bool wasMember = originalPayload is not null && BaseLogicalIndexEvaluator.Includes(item.Collection, index, originalPayload);
                bool isMember = finalPayload is not null && BaseLogicalIndexEvaluator.Includes(item.Collection, index, finalPayload);
                predicates = checked(predicates + (originalPayload is null ? 0 : 1) + (finalPayload is null ? 0 : 1));
                byte[]? oldKey = wasMember ? BaseLogicalIndexEvaluator.Key(item.Collection, index, originalPayload!) : null;
                byte[]? newKey = isMember ? BaseLogicalIndexEvaluator.Key(item.Collection, index, finalPayload!) : null;
                if (oldKey is not null) { keys++; keyBytes = checked(keyBytes + oldKey.LongLength); }
                if (newKey is not null) { keys++; keyBytes = checked(keyBytes + newKey.LongLength); }
                long generation = captured.Authority.Indexes.Single(value => value.Index == index.Checksum).Generation;
                ImmutableArray<BaseLogicalKeyInterval> reserved;
                if (!wasMember && !isMember) reserved = [];
                else if (wasMember && isMember && oldKey!.AsSpan().SequenceEqual(newKey)) reserved = [Interval(index.Checksum, oldKey!, generation, BaseLogicalKeyIntervalKind.ReadDependency)];
                else
                {
                    var values = new List<BaseLogicalKeyInterval>(2);
                    if (oldKey is not null) values.Add(Interval(index.Checksum, oldKey, generation, BaseLogicalKeyIntervalKind.WriteReservation));
                    if (newKey is not null) values.Add(Interval(index.Checksum, newKey, generation, BaseLogicalKeyIntervalKind.WriteReservation));
                    reserved = [.. values.OrderBy(static value => Convert.ToHexString(value.EqualityKey.AsSpan()), StringComparer.Ordinal)];
                }
                intervals = checked(intervals + reserved.Length); intervalBytes = checked(intervalBytes + reserved.Sum(static value => (long)value.EqualityKey.Length + 40));
                transitions.Add(new BaseAtomicIndexTransitionEvidence
                {
                    MutationOrdinal = item.Ordinal, IndexChecksum = index.Checksum, WasMember = wasMember,
                    OldEqualityKey = oldKey?.ToImmutableArray(), IsMember = isMember, NewEqualityKey = newKey?.ToImmutableArray(), Intervals = reserved,
                    Accounting = ZeroAccounting() with { PredicateEvaluations = (originalPayload is null ? 0 : 1) + (finalPayload is null ? 0 : 1), Keys = (oldKey is null ? 0 : 1) + (newKey is null ? 0 : 1), KeyBytes = (oldKey?.LongLength ?? 0) + (newKey?.LongLength ?? 0), Intervals = reserved.Length, IntervalBytes = reserved.Sum(static value => (long)value.EqualityKey.Length + 40) },
                });
            }
        }
        HashSet<BaseLogicalIndexChecksum> uniqueIndexes = orderedItems.SelectMany(static item => item.Collection.Indexes ?? []).Where(static index => index.Unique).Select(static index => index.Checksum).ToHashSet();
        long uniqueCandidates = transitions.Count(transition => transition.IsMember && uniqueIndexes.Contains(transition.IndexChecksum));
        long uniqueComparisons = transitions.Where(transition => transition.IsMember && transition.NewEqualityKey.HasValue && uniqueIndexes.Contains(transition.IndexChecksum))
            .GroupBy(static transition => transition.IndexChecksum).Sum(static group => Math.Max(0, group.Count() - 1));
        foreach (IGrouping<BaseLogicalIndexChecksum, BaseAtomicIndexTransitionEvidence> group in transitions.Where(transition => transition.IsMember && transition.NewEqualityKey.HasValue && uniqueIndexes.Contains(transition.IndexChecksum)).GroupBy(static transition => transition.IndexChecksum))
        {
            byte[][] sorted = [.. group.Select(static transition => transition.NewEqualityKey!.Value.ToArray()).OrderBy(static key => Convert.ToHexString(key), StringComparer.Ordinal)];
            for (int index = 1; index < sorted.Length; index++)
                if (sorted[index - 1].AsSpan().SequenceEqual(sorted[index])) throw new InvalidOperationException(BaseSchemaErrorCodes.UniqueConstraintViolated);
        }
        BaseSchemaWorkAccounting accounting = captured.Accounting with { CanonicalBytes = canonicalBytes, ConstraintEvaluations = evaluations, PredicateEvaluations = predicates, Keys = keys, KeyBytes = keyBytes, UniqueCandidates = uniqueCandidates, UniqueChecks = uniqueComparisons, Intervals = intervals, IntervalBytes = intervalBytes, EvidenceBytes = checked(captured.Accounting.EvidenceBytes + statementLocal.Length * 96L + constraints.Count * 80L + transitions.Count * 128L + keyBytes), TransientBytes = checked(captured.Accounting.TransientBytes + canonicalBytes + keyBytes + statementLocal.Length * 96L) };
        ValidateAccounting(accounting, captured.Limits);
        ImmutableArray<BaseAtomicConstraintEvidence> constraintValues = [.. constraints.OrderBy(static value => value.MutationOrdinal).ThenBy(static value => value.FieldOrdinal)];
        ImmutableArray<BaseAtomicIndexTransitionEvidence> transitionValues = [.. transitions.OrderBy(static value => value.MutationOrdinal).ThenBy(static value => value.IndexChecksum.ToString(), StringComparer.Ordinal)];
        BaseSchemaAuthorityChecksum checksum = FinalizedChecksum(captured.Authority.Checksum, captured.Limits, statementLocal, finalOverlay, constraintValues, transitionValues, accounting);
        return new BaseAtomicSchemaFinalizedExtension { Authority = captured.Authority, Limits = captured.Limits with { }, StatementLocal = statementLocal, FinalOverlay = finalOverlay, Constraints = constraintValues, Indexes = transitionValues, Accounting = accounting, Checksum = checksum };
    }

    internal static BaseAtomicSchemaFinalizedExtension? Freeze(BaseAtomicSchemaFinalizedExtension? value) => value is null ? null : new()
    {
        Authority = Freeze(new BaseAtomicSchemaCaptureExtension { Authority = value.Authority, Originals = [], Limits = value.Limits, Accounting = ZeroAccounting() })!.Authority,
        Limits = value.Limits with { },
        StatementLocal = [.. value.StatementLocal.Select(FreezeOverlay)], FinalOverlay = [.. value.FinalOverlay.Select(FreezeOverlay)],
        Constraints = [.. value.Constraints.Select(static item => item with { State = FreezeState(item.State), Accounting = item.Accounting with { } })],
        Indexes = [.. value.Indexes.Select(static item => item with
        {
            OldEqualityKey = item.OldEqualityKey?.ToArray().ToImmutableArray(), NewEqualityKey = item.NewEqualityKey?.ToArray().ToImmutableArray(),
            Intervals = [.. item.Intervals.Select(static interval => interval with { EqualityKey = interval.EqualityKey.ToArray().ToImmutableArray() })], Accounting = item.Accounting with { },
        })],
        Accounting = value.Accounting with { }, Checksum = BaseSchemaAuthorityChecksum.Create(value.Checksum.ToArray()),
    };

    internal static bool FinalizedMatches(BaseAtomicSchemaFinalizedExtension expected, BaseAtomicSchemaFinalizedExtension actual)
    {
        try
        {
            BaseSchemaAuthorityChecksum recomputed = FinalizedChecksum(
                actual.Authority.Checksum, actual.Limits, actual.StatementLocal, actual.FinalOverlay,
                actual.Constraints, actual.Indexes, actual.Accounting);
            return expected.Checksum == actual.Checksum && actual.Checksum == recomputed &&
                expected.Authority.Checksum == actual.Authority.Checksum &&
                expected.Authority.SchemaGeneration == actual.Authority.SchemaGeneration &&
                CollectionAuthoritiesMatch(expected.Authority.Collections, actual.Authority.Collections) &&
                IndexAuthoritiesMatch(expected.Authority.Indexes, actual.Authority.Indexes) &&
                expected.Limits == actual.Limits &&
                expected.Accounting == actual.Accounting;
        }
        catch { return false; }
    }

    internal static readonly BaseSchemaExecutionLimits PlatformLimits = new()
    {
        MaximumRecords = 4_096,
        MaximumCanonicalBytes = 16_777_216,
        MaximumJsonNodes = 1_048_576,
        MaximumConstraintEvaluations = 131_072,
        MaximumPredicateEvaluations = 131_072,
        MaximumKeys = 131_072,
        MaximumKeyBytes = 16_777_216,
        MaximumUniqueCandidates = 131_072,
        MaximumUniqueChecks = 131_072,
        MaximumIntervals = 131_072,
        MaximumIntervalBytes = 16_777_216,
        MaximumEvidenceBytes = 33_554_432,
        MaximumTransientBytes = 67_108_864,
    };

    private static void ValidateLimits(BaseSchemaExecutionLimits value)
    {
        if (value.MaximumRecords <= 0 || value.MaximumCanonicalBytes <= 0 || value.MaximumJsonNodes <= 0 ||
            value.MaximumConstraintEvaluations <= 0 || value.MaximumPredicateEvaluations <= 0 || value.MaximumKeys <= 0 ||
            value.MaximumKeyBytes <= 0 || value.MaximumUniqueCandidates <= 0 || value.MaximumUniqueChecks <= 0 ||
            value.MaximumIntervals <= 0 || value.MaximumIntervalBytes <= 0 || value.MaximumEvidenceBytes <= 0 ||
            value.MaximumTransientBytes <= 0)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    }

    private static BaseSchemaAuthorityChecksum Checksum(
        BaseSchemaAuthorityChecksum logicalSchema,
        ImmutableArray<BaseCollectionSchemaRequirement> requirements,
        BaseSchemaExecutionLimits limits)
    {
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, "hpd.base.atomic-schema-capture-request.v1\0"u8); Write(writer, logicalSchema.ToArray());
        U32(writer, checked((uint)requirements.Length));
        foreach (BaseCollectionSchemaRequirement item in requirements)
        {
            Bytes(writer, Encoding.UTF8.GetBytes(item.CollectionId)); Write(writer, item.LogicalSchemaChecksum.ToArray());
            U32(writer, checked((uint)item.Constraints.Length)); foreach (BaseScalarConstraintChecksum checksum in item.Constraints) Write(writer, checksum.ToArray());
            U32(writer, checked((uint)item.Indexes.Length)); foreach (BaseLogicalIndexChecksum checksum in item.Indexes) Write(writer, checksum.ToArray());
        }
        foreach (long value in new[] { limits.MaximumRecords, limits.MaximumCanonicalBytes, limits.MaximumJsonNodes,
            limits.MaximumConstraintEvaluations, limits.MaximumPredicateEvaluations, limits.MaximumKeys, limits.MaximumKeyBytes,
            limits.MaximumUniqueCandidates, limits.MaximumUniqueChecks, limits.MaximumIntervals, limits.MaximumIntervalBytes,
            limits.MaximumEvidenceBytes, limits.MaximumTransientBytes }) I64(writer, value);
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static bool RequirementsMatch(ImmutableArray<BaseCollectionSchemaRequirement> left, ImmutableArray<BaseCollectionSchemaRequirement> right) =>
        left.Length == right.Length && left.Zip(right).All(static pair =>
            pair.First.CollectionId == pair.Second.CollectionId && pair.First.LogicalSchemaChecksum == pair.Second.LogicalSchemaChecksum &&
            pair.First.Constraints.SequenceEqual(pair.Second.Constraints) && pair.First.Indexes.SequenceEqual(pair.Second.Indexes));

    private static bool CollectionAuthoritiesMatch(ImmutableArray<BaseCollectionSchemaAuthority> left, ImmutableArray<BaseCollectionSchemaAuthority> right) =>
        left.Length == right.Length && left.Zip(right).All(static pair => pair.First.CollectionId == pair.Second.CollectionId &&
            pair.First.CollectionGeneration == pair.Second.CollectionGeneration && pair.First.LogicalSchemaChecksum == pair.Second.LogicalSchemaChecksum &&
            pair.First.Constraints.SequenceEqual(pair.Second.Constraints) && pair.First.Indexes.SequenceEqual(pair.Second.Indexes));

    private static bool IndexAuthoritiesMatch(ImmutableArray<BaseLogicalIndexCurrentAuthority> left, ImmutableArray<BaseLogicalIndexCurrentAuthority> right) =>
        left.Length == right.Length && left.Zip(right).All(static pair => pair.First.Index == pair.Second.Index && pair.First.State == pair.Second.State &&
            pair.First.Generation == pair.Second.Generation && pair.First.PublicationChecksum == pair.Second.PublicationChecksum);

    private static bool NullableBytesEqual(ImmutableArray<byte>? left, ImmutableArray<byte>? right) => left is null == right is null &&
        (left is null || left.Value.AsSpan().SequenceEqual(right!.Value.AsSpan()));

    private static BaseSchemaAuthorityChecksum Publication(BaseAtomicMutationAuthorityEvidence authority, string collectionId, BaseLogicalIndexChecksum index) =>
        InitialPublication(authority.LogicalSchemaChecksum, collectionId, index, authority.RestoreEpoch);

    private static BaseSchemaAuthorityChecksum AuthorityChecksum(BaseAtomicMutationAuthorityEvidence authority, ImmutableArray<BaseCollectionSchemaAuthority> collections, ImmutableArray<BaseLogicalIndexCurrentAuthority> indexes)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "hpd.base.schema-authority.v1\0"u8); Write(writer, authority.LogicalSchemaChecksum.ToArray()); I64(writer, authority.SchemaGeneration);
        foreach (BaseCollectionSchemaAuthority value in collections) { Bytes(writer, Encoding.UTF8.GetBytes(value.CollectionId)); I64(writer, value.CollectionGeneration); Write(writer, value.LogicalSchemaChecksum.ToArray()); foreach (BaseScalarConstraintChecksum checksum in value.Constraints) Write(writer, checksum.ToArray()); foreach (BaseLogicalIndexChecksum checksum in value.Indexes) Write(writer, checksum.ToArray()); }
        foreach (BaseLogicalIndexCurrentAuthority value in indexes) { Write(writer, value.Index.ToArray()); I64(writer, (long)value.State); I64(writer, value.Generation); Write(writer, value.PublicationChecksum.ToArray()); }
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static BaseSchemaAuthorityChecksum OverlayDigest(BaseAtomicMutationPlanItem item, byte[]? finalBytes) =>
        Digest("hpd.base.schema-overlay.v1\0", Encoding.UTF8.GetBytes(item.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)), Encoding.UTF8.GetBytes(item.Collection.Id), Encoding.UTF8.GetBytes(item.RecordId.Value), [(byte)item.Kind], finalBytes ?? []);

    private static BaseLogicalKeyInterval Interval(BaseLogicalIndexChecksum index, byte[] key, long generation, BaseLogicalKeyIntervalKind kind) => new()
    { Index = index, EqualityKey = key.ToImmutableArray(), IndexGeneration = generation, Kind = kind };

    private static BaseSchemaAuthorityChecksum FinalizedChecksum(BaseSchemaAuthorityChecksum authority, BaseSchemaExecutionLimits limits, ImmutableArray<BaseSchemaOverlayRecord> statement, ImmutableArray<BaseSchemaOverlayRecord> final, ImmutableArray<BaseAtomicConstraintEvidence> constraints, ImmutableArray<BaseAtomicIndexTransitionEvidence> indexes, BaseSchemaWorkAccounting accounting)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "hpd.base.atomic-schema-finalized.v1\0"u8); Write(writer, authority.ToArray());
        foreach (BaseSchemaOverlayRecord value in statement) { I64(writer, value.MutationOrdinal); I64(writer, value.StatementOrdinal); Bytes(writer, Encoding.UTF8.GetBytes(value.CollectionId)); Bytes(writer, Encoding.UTF8.GetBytes(value.RecordId.Value)); Write(writer, [(byte)value.Disposition, value.Present ? (byte)1 : (byte)0]); Bytes(writer, value.CanonicalBytes.HasValue ? value.CanonicalBytes.Value.AsSpan() : []); Write(writer, value.OverlayDigest.ToArray()); }
        foreach (BaseSchemaOverlayRecord value in final) { I64(writer, value.MutationOrdinal); I64(writer, value.StatementOrdinal); Bytes(writer, Encoding.UTF8.GetBytes(value.CollectionId)); Bytes(writer, Encoding.UTF8.GetBytes(value.RecordId.Value)); Write(writer, [(byte)value.Disposition, value.Present ? (byte)1 : (byte)0]); Bytes(writer, value.CanonicalBytes.HasValue ? value.CanonicalBytes.Value.AsSpan() : []); Write(writer, value.OverlayDigest.ToArray()); }
        foreach (BaseAtomicConstraintEvidence value in constraints) { I64(writer, value.MutationOrdinal); I64(writer, value.FieldOrdinal); Write(writer, value.ConstraintChecksum.ToArray()); WriteFieldState(writer, value.State); foreach (long entryAccounting in AccountingValues(value.Accounting)) I64(writer, entryAccounting); }
        foreach (BaseAtomicIndexTransitionEvidence value in indexes) { I64(writer, value.MutationOrdinal); Write(writer, value.IndexChecksum.ToArray()); Write(writer, [value.WasMember ? (byte)1 : (byte)0, value.IsMember ? (byte)1 : (byte)0]); Bytes(writer, value.OldEqualityKey.HasValue ? value.OldEqualityKey.Value.AsSpan() : []); Bytes(writer, value.NewEqualityKey.HasValue ? value.NewEqualityKey.Value.AsSpan() : []); foreach (BaseLogicalKeyInterval interval in value.Intervals) { Write(writer, interval.Index.ToArray()); Bytes(writer, interval.EqualityKey.AsSpan()); I64(writer, interval.IndexGeneration); Write(writer, [(byte)interval.Kind]); } foreach (long entryAccounting in AccountingValues(value.Accounting)) I64(writer, entryAccounting); }
        foreach (long limit in LimitsValues(limits)) I64(writer, limit);
        foreach (long value in AccountingValues(accounting)) I64(writer, value);
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static BaseSchemaAuthorityChecksum ProvisionalChecksum(BaseSchemaAuthorityChecksum finalized, ImmutableArray<BaseSchemaAppliedIndexTransition> applied, BaseSchemaWorkAccounting accounting)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "hpd.base.atomic-schema-provisional.v1\0"u8); Write(writer, finalized.ToArray());
        foreach (BaseSchemaAppliedIndexTransition value in applied) { I64(writer, value.MutationOrdinal); Write(writer, value.Index.ToArray()); I64(writer, value.ResultingGeneration); Write(writer, value.AppliedChecksum.ToArray()); }
        foreach (long value in AccountingValues(accounting)) I64(writer, value);
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static BaseSchemaAuthorityChecksum AppliedChecksum(BaseAtomicSchemaFinalizedExtension finalized, BaseAtomicIndexTransitionEvidence transition, BaseSchemaOverlayRecord overlay, long generation)
    {
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "hpd.base.atomic-schema-applied-index.v1\0"u8);
        Write(writer, finalized.Authority.Checksum.ToArray()); I64(writer, transition.MutationOrdinal);
        Bytes(writer, Encoding.UTF8.GetBytes(overlay.CollectionId)); Bytes(writer, Encoding.UTF8.GetBytes(overlay.RecordId.Value));
        Write(writer, transition.IndexChecksum.ToArray()); Write(writer, [transition.WasMember ? (byte)1 : (byte)0, transition.IsMember ? (byte)1 : (byte)0]);
        Bytes(writer, transition.OldEqualityKey.HasValue ? transition.OldEqualityKey.Value.AsSpan() : []);
        Bytes(writer, transition.NewEqualityKey.HasValue ? transition.NewEqualityKey.Value.AsSpan() : []);
        foreach (BaseLogicalKeyInterval interval in transition.Intervals) { Write(writer, interval.Index.ToArray()); Bytes(writer, interval.EqualityKey.AsSpan()); I64(writer, interval.IndexGeneration); Write(writer, [(byte)interval.Kind]); }
        I64(writer, finalized.Authority.Indexes.Single(value => value.Index == transition.IndexChecksum).Generation); I64(writer, generation);
        Write(writer, overlay.OverlayDigest.ToArray());
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static bool AppliedTransitionsMatch(ImmutableArray<BaseSchemaAppliedIndexTransition> actual, ImmutableArray<BaseSchemaAppliedIndexTransition> expected) =>
        actual.Length == expected.Length && actual.Zip(expected).All(static pair => pair.First.MutationOrdinal == pair.Second.MutationOrdinal &&
            pair.First.Index == pair.Second.Index && pair.First.ResultingGeneration == pair.Second.ResultingGeneration && pair.First.AppliedChecksum == pair.Second.AppliedChecksum);

    private static void WriteFieldState(IBufferWriter<byte> writer, BaseFieldState state)
    {
        switch (state)
        {
            case BaseMissingFieldState: Write(writer, [(byte)0]); break;
            case BasePresentNullFieldState: Write(writer, [(byte)1]); break;
            case BasePresentValueFieldState present: Write(writer, [(byte)2]); Bytes(writer, present.CanonicalBytes.AsSpan()); break;
            default: throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid);
        }
    }

    private static byte[] BigEndian(long value) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes; }

    private static BaseSchemaWorkAccounting ZeroAccounting() => new() { Records = 0, CanonicalBytes = 0, JsonNodes = 0, ConstraintEvaluations = 0, PredicateEvaluations = 0, Keys = 0, KeyBytes = 0, UniqueCandidates = 0, UniqueChecks = 0, Intervals = 0, IntervalBytes = 0, EvidenceBytes = 0, TransientBytes = 0 };
    private static long[] AccountingValues(BaseSchemaWorkAccounting value) => [value.Records, value.CanonicalBytes, value.JsonNodes, value.ConstraintEvaluations, value.PredicateEvaluations, value.Keys, value.KeyBytes, value.UniqueCandidates, value.UniqueChecks, value.Intervals, value.IntervalBytes, value.EvidenceBytes, value.TransientBytes];
    private static long[] LimitsValues(BaseSchemaExecutionLimits value) => [value.MaximumRecords, value.MaximumCanonicalBytes, value.MaximumJsonNodes, value.MaximumConstraintEvaluations, value.MaximumPredicateEvaluations, value.MaximumKeys, value.MaximumKeyBytes, value.MaximumUniqueCandidates, value.MaximumUniqueChecks, value.MaximumIntervals, value.MaximumIntervalBytes, value.MaximumEvidenceBytes, value.MaximumTransientBytes];
    private static BaseSchemaOverlayRecord FreezeOverlay(BaseSchemaOverlayRecord value) => value with { CollectionId = new string(value.CollectionId.AsSpan()), CanonicalBytes = value.CanonicalBytes?.ToArray().ToImmutableArray(), OverlayDigest = BaseSchemaAuthorityChecksum.Create(value.OverlayDigest.ToArray()) };
    private static BaseFieldState FreezeState(BaseFieldState value) => value switch { BaseMissingFieldState => new BaseMissingFieldState(), BasePresentNullFieldState => new BasePresentNullFieldState(), BasePresentValueFieldState present => new BasePresentValueFieldState { CanonicalBytes = present.CanonicalBytes.ToArray().ToImmutableArray() }, _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid) };

    private static BaseSchemaAuthorityChecksum Digest(string purpose, params byte[][] values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(Encoding.ASCII.GetBytes(purpose));
        Span<byte> length = stackalloc byte[4]; foreach (byte[] value in values) { BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length)); hash.AppendData(length); hash.AppendData(value); }
        return BaseSchemaAuthorityChecksum.Create(hash.GetHashAndReset());
    }

    private static byte[] CanonicalRecord(CollectionDefinition collection, RecordPayload payload)
    {
        Dictionary<string, JsonElement> values = payload.Kind switch
        {
            RecordPayloadKind.FieldMap => payload.Fields ?? [],
            RecordPayloadKind.Json when payload.Json.ValueKind == JsonValueKind.Object => payload.Json.EnumerateObject().ToDictionary(static value => value.Name, static value => value.Value.Clone(), StringComparer.Ordinal),
            _ => throw new InvalidOperationException(BaseSchemaErrorCodes.ProviderEvidenceInvalid),
        };
        var writer = new ArrayBufferWriter<byte>(); Write(writer, "hpd.base.schema-record.v1\0"u8);
        FieldDefinition[] fields = (collection.Fields ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray(); U32(writer, checked((uint)fields.Length));
        foreach (FieldDefinition field in fields)
        {
            Bytes(writer, Encoding.UTF8.GetBytes(field.Id));
            if (!values.TryGetValue(field.WireName, out JsonElement value)) { Write(writer, [(byte)0]); continue; }
            if (value.ValueKind == JsonValueKind.Null) { Write(writer, [(byte)1]); continue; }
            Write(writer, [(byte)2]);
            byte[] bytes = field.ScalarKind is { } kind and not BaseScalarKind.FrozenArray
                ? BaseScalarCanonical.Encode(kind, value)
                : Compact(value);
            Bytes(writer, bytes);
        }
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] Compact(JsonElement value) { var buffer = new ArrayBufferWriter<byte>(); using var writer = new Utf8JsonWriter(buffer); value.WriteTo(writer); writer.Flush(); return buffer.WrittenSpan.ToArray(); }

    private static void ValidateAccounting(BaseSchemaWorkAccounting value, BaseSchemaExecutionLimits limits)
    {
        if (value.Records > limits.MaximumRecords || value.CanonicalBytes > limits.MaximumCanonicalBytes || value.JsonNodes > limits.MaximumJsonNodes || value.ConstraintEvaluations > limits.MaximumConstraintEvaluations || value.PredicateEvaluations > limits.MaximumPredicateEvaluations || value.Keys > limits.MaximumKeys || value.KeyBytes > limits.MaximumKeyBytes || value.UniqueCandidates > limits.MaximumUniqueCandidates || value.UniqueChecks > limits.MaximumUniqueChecks || value.Intervals > limits.MaximumIntervals || value.IntervalBytes > limits.MaximumIntervalBytes || value.EvidenceBytes > limits.MaximumEvidenceBytes || value.TransientBytes > limits.MaximumTransientBytes) throw new InvalidOperationException(BaseSchemaErrorCodes.BudgetExceeded);
    }

    private static void Bytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value) { U32(writer, checked((uint)value.Length)); Write(writer, value); }
    private static void U32(IBufferWriter<byte> writer, uint value) { Span<byte> span = writer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(span, value); writer.Advance(4); }
    private static void I64(IBufferWriter<byte> writer, long value) { Span<byte> span = writer.GetSpan(8); BinaryPrimitives.WriteInt64BigEndian(span, value); writer.Advance(8); }
    private static void Write(IBufferWriter<byte> writer, ReadOnlySpan<byte> value) { value.CopyTo(writer.GetSpan(value.Length)); writer.Advance(value.Length); }
}
