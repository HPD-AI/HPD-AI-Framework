using System.Collections.ObjectModel;
using System.Text;

namespace HPD.Base.Testing;

internal static class BaseVectorCertificationValidation
{
    internal static string Id(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length is < 1 or > 128 || value.Any(static character => character is < '!' or > '~'))
            throw new ArgumentException("The certification identifier is invalid.", parameter);
        return new string(value.AsSpan());
    }

    internal static string Text(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (Encoding.UTF8.GetByteCount(value) > 1_024 || value.Any(static character => char.IsControl(character) && character is not '\t' and not '\n'))
            throw new ArgumentException("The certification text is invalid.", parameter);
        return new string(value.AsSpan());
    }

    internal static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> values, int minimum, int maximum, string parameter, Func<T, T> clone)
    {
        ArgumentNullException.ThrowIfNull(values, parameter);
        if (values.Count < minimum || values.Count > maximum) throw new ArgumentOutOfRangeException(parameter);
        return Array.AsReadOnly(values.Select(clone).ToArray());
    }
}

/** <summary>Requests fixture seeding.</summary> */
public sealed class BaseVectorCertificationSeedRequest
{
    private BaseVectorCertificationSeedRequest(IReadOnlyList<BaseVectorCertificationRecord> records) => Records = BaseVectorCertificationValidation.Copy(records, 0, 1_024, nameof(records), static value => value.Copy());
    /** <summary>Gets deeply owned records.</summary> */
    public IReadOnlyList<BaseVectorCertificationRecord> Records { get; }
    /** <summary>Creates a bounded immutable seed request.</summary> */
    public static BaseVectorCertificationSeedRequest Create(IReadOnlyList<BaseVectorCertificationRecord> records) => new(records);
}

/** <summary>Requests canonical mutations.</summary> */
public sealed class BaseVectorCertificationMutationRequest
{
    private BaseVectorCertificationMutationRequest(IReadOnlyList<BaseVectorCertificationMutation> mutations) => Mutations = BaseVectorCertificationValidation.Copy(mutations, 1, 256, nameof(mutations), static value => value.Copy());
    /** <summary>Gets deeply owned mutations.</summary> */
    public IReadOnlyList<BaseVectorCertificationMutation> Mutations { get; }
    /** <summary>Creates a bounded immutable mutation request.</summary> */
    public static BaseVectorCertificationMutationRequest Create(IReadOnlyList<BaseVectorCertificationMutation> mutations) => new(mutations);
}

/** <summary>Requests a generation transition.</summary> */
public sealed class BaseVectorCertificationTransitionRequest
{
    private BaseVectorCertificationTransitionRequest(BaseVectorCertificationTransitionKind kind, string? collectionId, string? indexId) { Kind = kind; CollectionId = collectionId; IndexId = indexId; }
    /** <summary>Gets the transition kind.</summary> */
    public BaseVectorCertificationTransitionKind Kind { get; }
    /** <summary>Gets the optional collection identifier.</summary> */
    public string? CollectionId { get; }
    /** <summary>Gets the optional index identifier.</summary> */
    public string? IndexId { get; }
    /** <summary>Creates a validated immutable transition request.</summary> */
    public static BaseVectorCertificationTransitionRequest Create(BaseVectorCertificationTransitionKind kind, string? collectionId = null, string? indexId = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        bool collectionRequired = kind is BaseVectorCertificationTransitionKind.AdvancePurgeGeneration or BaseVectorCertificationTransitionKind.AdvanceIndexGeneration or BaseVectorCertificationTransitionKind.AdvanceVectorSpaceGeneration;
        bool indexRequired = kind is BaseVectorCertificationTransitionKind.AdvanceIndexGeneration or BaseVectorCertificationTransitionKind.AdvanceVectorSpaceGeneration;
        if (collectionRequired != (collectionId is not null) || indexRequired != (indexId is not null)) throw new ArgumentException("The generation transition target is invalid.");
        return new(kind, collectionId is null ? null : BaseVectorCertificationValidation.Id(collectionId, nameof(collectionId)), indexId is null ? null : BaseVectorCertificationValidation.Id(indexId, nameof(indexId)));
    }
}

/** <summary>Requests history pruning.</summary> */
public sealed class BaseVectorCertificationPruneRequest
{
    private BaseVectorCertificationPruneRequest(long value) => RetainFromPositionInclusive = value;
    /** <summary>Gets the earliest position to retain.</summary> */
    public long RetainFromPositionInclusive { get; }
    /** <summary>Creates a validated immutable prune request.</summary> */
    public static BaseVectorCertificationPruneRequest Create(long retainFromPositionInclusive) => retainFromPositionInclusive < 0 ? throw new ArgumentOutOfRangeException(nameof(retainFromPositionInclusive)) : new(retainFromPositionInclusive);
}

/** <summary>Requests derived advancement.</summary> */
public sealed class BaseVectorCertificationAdvanceRequest
{
    private BaseVectorCertificationAdvanceRequest(long through, int maximum) { ThroughPositionInclusive = through; MaximumMutations = maximum; }
    /** <summary>Gets the finite target.</summary> */
    public long ThroughPositionInclusive { get; }
    /** <summary>Gets the page bound.</summary> */
    public int MaximumMutations { get; }
    /** <summary>Creates a validated immutable advance request.</summary> */
    public static BaseVectorCertificationAdvanceRequest Create(long throughPositionInclusive, int maximumMutations = 256) => throughPositionInclusive < 0 || maximumMutations is < 1 or > 256 ? throw new ArgumentOutOfRangeException(nameof(throughPositionInclusive)) : new(throughPositionInclusive, maximumMutations);
}

/** <summary>Requests searchable visibility publication.</summary> */
public sealed class BaseVectorCertificationVisibilityRequest
{
    private BaseVectorCertificationVisibilityRequest(long value) => ThroughPositionInclusive = value;
    /** <summary>Gets the finite visibility target.</summary> */
    public long ThroughPositionInclusive { get; }
    /** <summary>Creates a validated immutable visibility request.</summary> */
    public static BaseVectorCertificationVisibilityRequest Create(long throughPositionInclusive) => throughPositionInclusive < 0 ? throw new ArgumentOutOfRangeException(nameof(throughPositionInclusive)) : new(throughPositionInclusive);
}

/** <summary>Requests one index rebuild.</summary> */
public sealed class BaseVectorCertificationRebuildRequest
{
    private BaseVectorCertificationRebuildRequest(string collectionId, string indexId) { CollectionId = collectionId; IndexId = indexId; }
    /** <summary>Gets the collection identifier.</summary> */
    public string CollectionId { get; }
    /** <summary>Gets the index identifier.</summary> */
    public string IndexId { get; }
    /** <summary>Creates a validated immutable rebuild request.</summary> */
    public static BaseVectorCertificationRebuildRequest Create(string collectionId, string indexId) => new(BaseVectorCertificationValidation.Id(collectionId, nameof(collectionId)), BaseVectorCertificationValidation.Id(indexId, nameof(indexId)));
}

/** <summary>Requests one observation page.</summary> */
public sealed class BaseVectorCertificationObservationRequest
{
    private BaseVectorCertificationObservationRequest(long after, int take) { AfterSequenceExclusive = after; Take = take; }
    /** <summary>Gets the predecessor sequence.</summary> */
    public long AfterSequenceExclusive { get; }
    /** <summary>Gets the maximum entries.</summary> */
    public int Take { get; }
    /** <summary>Creates a bounded immutable observation request.</summary> */
    public static BaseVectorCertificationObservationRequest Create(long afterSequenceExclusive = 0, int take = 256) => afterSequenceExclusive < 0 || take is < 1 or > 256 ? throw new ArgumentOutOfRangeException(nameof(afterSequenceExclusive)) : new(afterSequenceExclusive, take);
}

/** <summary>Requests one closed certification vector query.</summary> */
public sealed class BaseVectorCertificationQueryRequest
{
    private BaseVectorCertificationQueryRequest(BaseVectorCertificationQueryScenario scenario) => Scenario = scenario;
    /** <summary>Gets the behavior scenario.</summary> */
    public BaseVectorCertificationQueryScenario Scenario { get; }
    /** <summary>Creates a validated immutable query request.</summary> */
    public static BaseVectorCertificationQueryRequest Create(BaseVectorCertificationQueryScenario scenario) =>
        !Enum.IsDefined(scenario) ? throw new ArgumentOutOfRangeException(nameof(scenario)) : new(scenario);
}

/** <summary>Contains one authoritative hydrated certification match.</summary> */
public sealed class BaseVectorCertificationQueryMatch
{
    private BaseVectorCertificationQueryMatch(string recordId, string revision, double measure, long indexedPosition)
    { RecordId = recordId; Revision = revision; Measure = measure; IndexedPosition = indexedPosition; }
    /** <summary>Gets the canonical record identifier.</summary> */
    public string RecordId { get; }
    /** <summary>Gets the exact hydrated revision.</summary> */
    public string Revision { get; }
    /** <summary>Gets the finite disclosed measure.</summary> */
    public double Measure { get; }
    /** <summary>Gets the record-local authoritative mutation position.</summary> */
    public long IndexedPosition { get; }
    /** <summary>Creates validated immutable query-match evidence.</summary> */
    public static BaseVectorCertificationQueryMatch Create(string recordId, string revision, double measure, long indexedPosition) =>
        !double.IsFinite(measure) || indexedPosition < 1
            ? throw new ArgumentOutOfRangeException(nameof(measure))
            : new(BaseVectorCertificationValidation.Id(recordId, nameof(recordId)), BaseVectorCertificationValidation.Id(revision, nameof(revision)), measure, indexedPosition);
    internal BaseVectorCertificationQueryMatch Copy() => Create(RecordId, Revision, Measure, IndexedPosition);
}

/** <summary>Contains bounded evidence from one real certification search.</summary> */
public sealed class BaseVectorCertificationQueryResult
{
    private BaseVectorCertificationQueryResult(BaseVectorCertificationQueryScenario scenario, IReadOnlyList<BaseVectorCertificationQueryMatch> matches, int authorizedCandidates, int hydratedRecords)
    { Scenario = scenario; Matches = Array.AsReadOnly(matches.Select(static value => value.Copy()).ToArray()); AuthorizedCandidates = authorizedCandidates; HydratedRecords = hydratedRecords; }
    /** <summary>Gets the executed scenario.</summary> */
    public BaseVectorCertificationQueryScenario Scenario { get; }
    /** <summary>Gets ordered authoritative hydrated matches.</summary> */
    public IReadOnlyList<BaseVectorCertificationQueryMatch> Matches { get; }
    /** <summary>Gets candidates remaining after policy and candidate-filter enforcement.</summary> */
    public int AuthorizedCandidates { get; }
    /** <summary>Gets records hydrated at exact indexed revisions.</summary> */
    public int HydratedRecords { get; }
    /** <summary>Creates validated, deeply owned query evidence.</summary> */
    public static BaseVectorCertificationQueryResult Create(BaseVectorCertificationQueryScenario scenario, IReadOnlyList<BaseVectorCertificationQueryMatch> matches, int authorizedCandidates, int hydratedRecords)
    {
        if (!Enum.IsDefined(scenario)) throw new ArgumentOutOfRangeException(nameof(scenario));
        ReadOnlyCollection<BaseVectorCertificationQueryMatch> copy = BaseVectorCertificationValidation.Copy(matches, 1, 64, nameof(matches), static value => value.Copy());
        if (authorizedCandidates < copy.Count || hydratedRecords != copy.Count || copy.Select(static value => value.RecordId).Distinct(StringComparer.Ordinal).Count() != copy.Count)
            throw new ArgumentException("The certification query evidence is invalid.");
        return new(scenario, copy, authorizedCandidates, hydratedRecords);
    }
}

/** <summary>Contains one closed certification record.</summary> */
public sealed class BaseVectorCertificationRecord
{
    private BaseVectorCertificationRecord(string id, IReadOnlyList<BaseVectorCertificationField> fields) { RecordId = id; Fields = fields; }
    /** <summary>Gets the record identifier.</summary> */
    public string RecordId { get; }
    /** <summary>Gets deeply owned fields.</summary> */
    public IReadOnlyList<BaseVectorCertificationField> Fields { get; }
    /** <summary>Creates a validated immutable record.</summary> */
    public static BaseVectorCertificationRecord Create(string recordId, IReadOnlyList<BaseVectorCertificationField> fields)
    {
        ReadOnlyCollection<BaseVectorCertificationField> copy = BaseVectorCertificationValidation.Copy(fields, 0, 32, nameof(fields), static value => value.Copy());
        if (copy.Select(static value => value.FieldId).Distinct(StringComparer.Ordinal).Count() != copy.Count) throw new ArgumentException("Certification field identifiers must be unique.", nameof(fields));
        return new(BaseVectorCertificationValidation.Id(recordId, nameof(recordId)), copy);
    }
    internal BaseVectorCertificationRecord Copy() => Create(RecordId, Fields);
}

/** <summary>Contains one closed certification field.</summary> */
public sealed class BaseVectorCertificationField
{
    private BaseVectorCertificationField(string id, BaseVectorCertificationValue value) { FieldId = id; Value = value; }
    /** <summary>Gets the field identifier.</summary> */
    public string FieldId { get; }
    /** <summary>Gets the deeply owned value.</summary> */
    public BaseVectorCertificationValue Value { get; }
    /** <summary>Creates a validated immutable field.</summary> */
    public static BaseVectorCertificationField Create(string fieldId, BaseVectorCertificationValue value) => new(BaseVectorCertificationValidation.Id(fieldId, nameof(fieldId)), (value ?? throw new ArgumentNullException(nameof(value))).Copy());
    internal BaseVectorCertificationField Copy() => Create(FieldId, Value);
}

/** <summary>Contains one closed certification value.</summary> */
public sealed class BaseVectorCertificationValue
{
    private BaseVectorCertificationValue(BaseVectorCertificationValueKind kind, bool? boolean, long? integer, string? text, BaseVector? vector) { Kind = kind; BooleanValue = boolean; IntegerValue = integer; TextValue = text; VectorValue = vector; }
    /** <summary>Gets the value kind.</summary> */
    public BaseVectorCertificationValueKind Kind { get; }
    /** <summary>Gets the Boolean value.</summary> */
    public bool? BooleanValue { get; }
    /** <summary>Gets the integer value.</summary> */
    public long? IntegerValue { get; }
    /** <summary>Gets the string or identifier value.</summary> */
    public string? TextValue { get; }
    /** <summary>Gets the immutable vector value.</summary> */
    public BaseVector? VectorValue { get; }
    /** <summary>Creates a missing value.</summary> */
    public static BaseVectorCertificationValue Missing() => new(BaseVectorCertificationValueKind.Missing, null, null, null, null);
    /** <summary>Creates an explicit null value.</summary> */
    public static BaseVectorCertificationValue Null() => new(BaseVectorCertificationValueKind.Null, null, null, null, null);
    /** <summary>Creates a Boolean value.</summary> */
    public static BaseVectorCertificationValue Boolean(bool value) => new(BaseVectorCertificationValueKind.Boolean, value, null, null, null);
    /** <summary>Creates an integer value.</summary> */
    public static BaseVectorCertificationValue Integer(long value) => new(BaseVectorCertificationValueKind.Integer, null, value, null, null);
    /** <summary>Creates a bounded string value.</summary> */
    public static BaseVectorCertificationValue String(string value) => new(BaseVectorCertificationValueKind.String, null, null, BaseVectorCertificationValidation.Text(value, nameof(value)), null);
    /** <summary>Creates a bounded identifier value.</summary> */
    public static BaseVectorCertificationValue Id(string value) => new(BaseVectorCertificationValueKind.Id, null, null, BaseVectorCertificationValidation.Id(value, nameof(value)), null);
    /** <summary>Creates an owned vector value.</summary> */
    public static BaseVectorCertificationValue Vector(BaseVector value) => new(BaseVectorCertificationValueKind.Vector, null, null, null, BaseVector.Create(value.ToArray()));
    internal BaseVectorCertificationValue Copy() => Kind switch { BaseVectorCertificationValueKind.Missing => Missing(), BaseVectorCertificationValueKind.Null => Null(), BaseVectorCertificationValueKind.Boolean => Boolean(BooleanValue!.Value), BaseVectorCertificationValueKind.Integer => Integer(IntegerValue!.Value), BaseVectorCertificationValueKind.String => String(TextValue!), BaseVectorCertificationValueKind.Id => Id(TextValue!), _ => Vector(VectorValue!.Value) };
}

/** <summary>Contains one canonical certification mutation.</summary> */
public sealed class BaseVectorCertificationMutation
{
    private BaseVectorCertificationMutation(BaseVectorCertificationMutationKind kind, string recordId, BaseVectorCertificationRecord? after, string? expectedRevision) { Kind = kind; RecordId = recordId; After = after; ExpectedRevision = expectedRevision; }
    /** <summary>Gets the mutation kind.</summary> */
    public BaseVectorCertificationMutationKind Kind { get; }
    /** <summary>Gets the record identifier.</summary> */
    public string RecordId { get; }
    /** <summary>Gets the complete after-record when required.</summary> */
    public BaseVectorCertificationRecord? After { get; }
    /** <summary>Gets the optional expected revision.</summary> */
    public string? ExpectedRevision { get; }
    /** <summary>Creates a validated immutable mutation.</summary> */
    public static BaseVectorCertificationMutation Create(BaseVectorCertificationMutationKind kind, string recordId, BaseVectorCertificationRecord? after = null, string? expectedRevision = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        bool needsAfter = kind is BaseVectorCertificationMutationKind.Create or BaseVectorCertificationMutationKind.Replace;
        if (needsAfter != (after is not null) || kind is BaseVectorCertificationMutationKind.Create or BaseVectorCertificationMutationKind.Purge && expectedRevision is not null) throw new ArgumentException("The certification mutation shape is invalid.");
        string id = BaseVectorCertificationValidation.Id(recordId, nameof(recordId));
        if (after is not null && !string.Equals(after.RecordId, id, StringComparison.Ordinal)) throw new ArgumentException("The after-record identity does not match.", nameof(after));
        return new(kind, id, after?.Copy(), expectedRevision is null ? null : BaseVectorCertificationValidation.Id(expectedRevision, nameof(expectedRevision)));
    }
    internal BaseVectorCertificationMutation Copy() => Create(Kind, RecordId, After, ExpectedRevision);
}

// Results are provider-authored DTOs; the certification runner validates and deep-copies them into its report.
/** <summary>Contains a finite authority head.</summary> */
public sealed class BaseVectorCertificationAuthorityHead { private BaseVectorCertificationAuthorityHead(string digest,long restore,long schema,long earliest,long high,DateTimeOffset captured){StoreIdentityDigest=digest;RestoreEpoch=restore;SchemaGeneration=schema;EarliestRetainedPosition=earliest;HighWaterPosition=high;CapturedAt=captured;} /** <summary>Gets identity digest.</summary> */ public string StoreIdentityDigest { get; } /** <summary>Gets restore epoch.</summary> */ public long RestoreEpoch { get; } /** <summary>Gets schema generation.</summary> */ public long SchemaGeneration { get; } /** <summary>Gets earliest retained position.</summary> */ public long EarliestRetainedPosition { get; } /** <summary>Gets high-water.</summary> */ public long HighWaterPosition { get; } /** <summary>Gets capture time.</summary> */ public DateTimeOffset CapturedAt { get; } /** <summary>Creates validated authority-head evidence.</summary> */ public static BaseVectorCertificationAuthorityHead Create(string storeIdentityDigest,long restoreEpoch,long schemaGeneration,long earliestRetainedPosition,long highWaterPosition,DateTimeOffset capturedAt){if(restoreEpoch<0||schemaGeneration<0||earliestRetainedPosition<0||highWaterPosition<earliestRetainedPosition||capturedAt.Offset!=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(highWaterPosition));return new(BaseVectorCertificationValidation.Id(storeIdentityDigest,nameof(storeIdentityDigest)),restoreEpoch,schemaGeneration,earliestRetainedPosition,highWaterPosition,capturedAt);} internal BaseVectorCertificationAuthorityHead Copy()=>Create(StoreIdentityDigest,RestoreEpoch,SchemaGeneration,EarliestRetainedPosition,HighWaterPosition,CapturedAt); }
/** <summary>Contains seed outcome.</summary> */
public sealed class BaseVectorCertificationSeedResult { private BaseVectorCertificationSeedResult(int count,BaseVectorCertificationAuthorityHead head){SeededRecords=count;Head=head.Copy();} /** <summary>Gets count.</summary> */ public int SeededRecords{get;} /** <summary>Gets head.</summary> */ public BaseVectorCertificationAuthorityHead Head{get;} /** <summary>Creates validated seed evidence.</summary> */ public static BaseVectorCertificationSeedResult Create(int seededRecords,BaseVectorCertificationAuthorityHead head)=>seededRecords is <0 or >1024?throw new ArgumentOutOfRangeException(nameof(seededRecords)):new(seededRecords,head??throw new ArgumentNullException(nameof(head))); }
/** <summary>Contains mutation outcome.</summary> */
public sealed class BaseVectorCertificationMutationResult { private BaseVectorCertificationMutationResult(int count,long first,long last,BaseVectorCertificationAuthorityHead head,IReadOnlyList<BaseVectorCertificationRecordFact> records){CommittedMutations=count;FirstPosition=first;LastPosition=last;Head=head.Copy();Records=Array.AsReadOnly(records.Select(static value=>value.Copy()).ToArray());} /** <summary>Gets count.</summary> */ public int CommittedMutations{get;} /** <summary>Gets first position.</summary> */ public long FirstPosition{get;} /** <summary>Gets last position.</summary> */ public long LastPosition{get;} /** <summary>Gets head.</summary> */ public BaseVectorCertificationAuthorityHead Head{get;} /** <summary>Gets records.</summary> */ public IReadOnlyList<BaseVectorCertificationRecordFact> Records{get;} /** <summary>Creates validated mutation evidence.</summary> */ public static BaseVectorCertificationMutationResult Create(int committedMutations,long firstPosition,long lastPosition,BaseVectorCertificationAuthorityHead head,IReadOnlyList<BaseVectorCertificationRecordFact> records){ArgumentNullException.ThrowIfNull(head);if(committedMutations is <1 or >256||firstPosition<1||lastPosition<firstPosition||checked(lastPosition-firstPosition+1)!=committedMutations||head.HighWaterPosition<lastPosition)throw new ArgumentOutOfRangeException(nameof(committedMutations));return new(committedMutations,firstPosition,lastPosition,head,BaseVectorCertificationValidation.Copy(records,0,256,nameof(records),static value=>value.Copy()));} }
/** <summary>Contains transition outcome.</summary> */
public sealed class BaseVectorCertificationTransitionResult { private BaseVectorCertificationTransitionResult(BaseVectorCertificationTransitionKind kind,string? collection,string? index,long previous,long current){Kind=kind;CollectionId=collection;IndexId=index;PreviousGeneration=previous;CurrentGeneration=current;} /** <summary>Gets kind.</summary> */ public BaseVectorCertificationTransitionKind Kind{get;} /** <summary>Gets collection.</summary> */ public string? CollectionId{get;} /** <summary>Gets index.</summary> */ public string? IndexId{get;} /** <summary>Gets previous value.</summary> */ public long PreviousGeneration{get;} /** <summary>Gets current value.</summary> */ public long CurrentGeneration{get;} /** <summary>Creates validated transition evidence.</summary> */ public static BaseVectorCertificationTransitionResult Create(BaseVectorCertificationTransitionKind kind,string? collectionId,string? indexId,long previousGeneration,long currentGeneration){if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));bool collectionRequired=kind is BaseVectorCertificationTransitionKind.AdvancePurgeGeneration or BaseVectorCertificationTransitionKind.AdvanceIndexGeneration or BaseVectorCertificationTransitionKind.AdvanceVectorSpaceGeneration;bool indexRequired=kind is BaseVectorCertificationTransitionKind.AdvanceIndexGeneration or BaseVectorCertificationTransitionKind.AdvanceVectorSpaceGeneration;if(collectionRequired!=(collectionId is not null)||indexRequired!=(indexId is not null)||previousGeneration<0||currentGeneration!=checked(previousGeneration+1))throw new ArgumentException("The certification transition evidence is invalid.");return new(kind,collectionId is null?null:BaseVectorCertificationValidation.Id(collectionId,nameof(collectionId)),indexId is null?null:BaseVectorCertificationValidation.Id(indexId,nameof(indexId)),previousGeneration,currentGeneration);} }
/** <summary>Contains prune outcome.</summary> */
public sealed class BaseVectorCertificationPruneResult { private BaseVectorCertificationPruneResult(long previous,long current,long high){PreviousEarliestPosition=previous;CurrentEarliestPosition=current;HighWaterPosition=high;} /** <summary>Gets previous earliest.</summary> */ public long PreviousEarliestPosition{get;} /** <summary>Gets current earliest.</summary> */ public long CurrentEarliestPosition{get;} /** <summary>Gets high-water.</summary> */ public long HighWaterPosition{get;} /** <summary>Creates validated pruning evidence.</summary> */ public static BaseVectorCertificationPruneResult Create(long previousEarliestPosition,long currentEarliestPosition,long highWaterPosition)=>previousEarliestPosition<0||currentEarliestPosition<previousEarliestPosition||highWaterPosition<currentEarliestPosition?throw new ArgumentOutOfRangeException(nameof(currentEarliestPosition)):new(previousEarliestPosition,currentEarliestPosition,highWaterPosition); }
/** <summary>Contains authority state.</summary> */
public sealed class BaseVectorCertificationAuthorityState { private BaseVectorCertificationAuthorityState(BaseVectorCertificationAuthorityHead head,IReadOnlyList<BaseVectorCertificationGenerationFact> generations,IReadOnlyList<BaseVectorCertificationRecordFact> records){Head=head.Copy();Generations=Array.AsReadOnly(generations.Select(static value=>value.Copy()).ToArray());Records=Array.AsReadOnly(records.Select(static value=>value.Copy()).ToArray());} /** <summary>Gets head.</summary> */ public BaseVectorCertificationAuthorityHead Head{get;} /** <summary>Gets generations.</summary> */ public IReadOnlyList<BaseVectorCertificationGenerationFact> Generations{get;} /** <summary>Gets records.</summary> */ public IReadOnlyList<BaseVectorCertificationRecordFact> Records{get;} /** <summary>Creates deeply owned authority evidence.</summary> */ public static BaseVectorCertificationAuthorityState Create(BaseVectorCertificationAuthorityHead head,IReadOnlyList<BaseVectorCertificationGenerationFact> generations,IReadOnlyList<BaseVectorCertificationRecordFact> records)=>new(head??throw new ArgumentNullException(nameof(head)),BaseVectorCertificationValidation.Copy(generations,0,1024,nameof(generations),static value=>value.Copy()),BaseVectorCertificationValidation.Copy(records,0,1024,nameof(records),static value=>value.Copy())); }
/** <summary>Identifies a generation fact kind.</summary> */
public enum BaseVectorCertificationGenerationKind { /** <summary>Purge generation.</summary> */ Purge, /** <summary>Index generation.</summary> */ Index, /** <summary>Vector-space generation.</summary> */ VectorSpace }
/** <summary>Contains a generation fact.</summary> */
public sealed class BaseVectorCertificationGenerationFact { private BaseVectorCertificationGenerationFact(string collection,string? index,BaseVectorCertificationGenerationKind kind,long value){CollectionId=collection;IndexId=index;Kind=kind;Value=value;} /** <summary>Gets collection.</summary> */ public string CollectionId{get;} /** <summary>Gets index.</summary> */ public string? IndexId{get;} /** <summary>Gets generation kind.</summary> */ public BaseVectorCertificationGenerationKind Kind{get;} /** <summary>Gets value.</summary> */ public long Value{get;} /** <summary>Creates validated generation evidence.</summary> */ public static BaseVectorCertificationGenerationFact Create(string collectionId,string? indexId,BaseVectorCertificationGenerationKind kind,long value){if(!Enum.IsDefined(kind)||value<0||kind==BaseVectorCertificationGenerationKind.Purge!=(indexId is null))throw new ArgumentException("The certification generation evidence is invalid.");return new(BaseVectorCertificationValidation.Id(collectionId,nameof(collectionId)),indexId is null?null:BaseVectorCertificationValidation.Id(indexId,nameof(indexId)),kind,value);} internal BaseVectorCertificationGenerationFact Copy()=>Create(CollectionId,IndexId,Kind,Value); }
/** <summary>Contains a record fact.</summary> */
public sealed class BaseVectorCertificationRecordFact { private BaseVectorCertificationRecordFact(string id,string revision,long position,bool exists){RecordId=id;Revision=revision;LatestMutationPosition=position;Exists=exists;} /** <summary>Gets ID.</summary> */ public string RecordId{get;} /** <summary>Gets revision.</summary> */ public string Revision{get;} /** <summary>Gets position.</summary> */ public long LatestMutationPosition{get;} /** <summary>Gets existence.</summary> */ public bool Exists{get;} /** <summary>Creates validated record evidence.</summary> */ public static BaseVectorCertificationRecordFact Create(string recordId,string revision,long latestMutationPosition,bool exists)=>latestMutationPosition<0?throw new ArgumentOutOfRangeException(nameof(latestMutationPosition)):new(BaseVectorCertificationValidation.Id(recordId,nameof(recordId)),BaseVectorCertificationValidation.Id(revision,nameof(revision)),latestMutationPosition,exists); internal BaseVectorCertificationRecordFact Copy()=>Create(RecordId,Revision,LatestMutationPosition,Exists); }
/** <summary>Contains derived advancement outcome.</summary> */
public sealed class BaseVectorCertificationAdvanceResult { private BaseVectorCertificationAdvanceResult(int count,long previous,long current,long visible){ExaminedMutations=count;PreviousCheckpoint=previous;CurrentCheckpoint=current;SearchVisibleThrough=visible;} /** <summary>Gets examined count.</summary> */ public int ExaminedMutations{get;} /** <summary>Gets previous checkpoint.</summary> */ public long PreviousCheckpoint{get;} /** <summary>Gets current checkpoint.</summary> */ public long CurrentCheckpoint{get;} /** <summary>Gets visibility.</summary> */ public long SearchVisibleThrough{get;} /** <summary>Creates validated advancement evidence.</summary> */ public static BaseVectorCertificationAdvanceResult Create(int examinedMutations,long previousCheckpoint,long currentCheckpoint,long searchVisibleThrough)=>examinedMutations is <0 or >256||previousCheckpoint<0||currentCheckpoint<previousCheckpoint||searchVisibleThrough<0||searchVisibleThrough>currentCheckpoint?throw new ArgumentOutOfRangeException(nameof(currentCheckpoint)):new(examinedMutations,previousCheckpoint,currentCheckpoint,searchVisibleThrough); }
/** <summary>Contains visibility outcome.</summary> */
public sealed class BaseVectorCertificationVisibilityResult { private BaseVectorCertificationVisibilityResult(long previous,long current,long checkpoint){PreviousSearchVisibleThrough=previous;CurrentSearchVisibleThrough=current;DurableCheckpoint=checkpoint;} /** <summary>Gets previous visibility.</summary> */ public long PreviousSearchVisibleThrough{get;} /** <summary>Gets current visibility.</summary> */ public long CurrentSearchVisibleThrough{get;} /** <summary>Gets checkpoint.</summary> */ public long DurableCheckpoint{get;} /** <summary>Creates validated visibility evidence.</summary> */ public static BaseVectorCertificationVisibilityResult Create(long previousSearchVisibleThrough,long currentSearchVisibleThrough,long durableCheckpoint)=>previousSearchVisibleThrough<0||currentSearchVisibleThrough<previousSearchVisibleThrough||durableCheckpoint<currentSearchVisibleThrough?throw new ArgumentOutOfRangeException(nameof(currentSearchVisibleThrough)):new(previousSearchVisibleThrough,currentSearchVisibleThrough,durableCheckpoint); }
/** <summary>Contains rebuild outcome.</summary> */
public sealed class BaseVectorCertificationRebuildResult { private BaseVectorCertificationRebuildResult(string collection,string index,long previous,long current,BaseVectorCertificationAuthorityHead head){CollectionId=collection;IndexId=index;PreviousGeneration=previous;CurrentGeneration=current;SourceHead=head.Copy();} /** <summary>Gets collection.</summary> */ public string CollectionId{get;} /** <summary>Gets index.</summary> */ public string IndexId{get;} /** <summary>Gets previous generation.</summary> */ public long PreviousGeneration{get;} /** <summary>Gets current generation.</summary> */ public long CurrentGeneration{get;} /** <summary>Gets source head.</summary> */ public BaseVectorCertificationAuthorityHead SourceHead{get;} /** <summary>Creates validated rebuild evidence.</summary> */ public static BaseVectorCertificationRebuildResult Create(string collectionId,string indexId,long previousGeneration,long currentGeneration,BaseVectorCertificationAuthorityHead sourceHead)=>previousGeneration<0||currentGeneration<=previousGeneration?throw new ArgumentOutOfRangeException(nameof(currentGeneration)):new(BaseVectorCertificationValidation.Id(collectionId,nameof(collectionId)),BaseVectorCertificationValidation.Id(indexId,nameof(indexId)),previousGeneration,currentGeneration,sourceHead??throw new ArgumentNullException(nameof(sourceHead))); }
/** <summary>Contains provider state.</summary> */
public sealed class BaseVectorCertificationProviderState { private BaseVectorCertificationProviderState(IReadOnlyList<BaseVectorCertificationIndexState> indexes)=>Indexes=Array.AsReadOnly(indexes.Select(static value=>value.Copy()).ToArray()); /** <summary>Gets indexes.</summary> */ public IReadOnlyList<BaseVectorCertificationIndexState> Indexes{get;} /** <summary>Creates deeply owned provider evidence.</summary> */ public static BaseVectorCertificationProviderState Create(IReadOnlyList<BaseVectorCertificationIndexState> indexes){ReadOnlyCollection<BaseVectorCertificationIndexState> copy=BaseVectorCertificationValidation.Copy(indexes,0,256,nameof(indexes),static value=>value.Copy());if(!copy.SequenceEqual(copy.OrderBy(static value=>value.CollectionId,StringComparer.Ordinal).ThenBy(static value=>value.IndexId,StringComparer.Ordinal)))throw new ArgumentException("Certification index evidence must use ordinal order.",nameof(indexes));return new(copy);} }
/** <summary>Contains index state.</summary> */
public sealed class BaseVectorCertificationIndexState { private BaseVectorCertificationIndexState(string collection,string index,long generation,long purge,long checkpoint,long visible,int carriers,BaseVectorIndexState state){CollectionId=collection;IndexId=index;IndexGeneration=generation;PurgeGeneration=purge;DurableCheckpoint=checkpoint;SearchVisibleThrough=visible;CarrierCount=carriers;State=state;} /** <summary>Gets collection.</summary> */ public string CollectionId{get;} /** <summary>Gets index.</summary> */ public string IndexId{get;} /** <summary>Gets generation.</summary> */ public long IndexGeneration{get;} /** <summary>Gets purge generation.</summary> */ public long PurgeGeneration{get;} /** <summary>Gets checkpoint.</summary> */ public long DurableCheckpoint{get;} /** <summary>Gets visibility.</summary> */ public long SearchVisibleThrough{get;} /** <summary>Gets carriers.</summary> */ public int CarrierCount{get;} /** <summary>Gets state.</summary> */ public BaseVectorIndexState State{get;} /** <summary>Creates validated index evidence.</summary> */ public static BaseVectorCertificationIndexState Create(string collectionId,string indexId,long indexGeneration,long purgeGeneration,long durableCheckpoint,long searchVisibleThrough,int carrierCount,BaseVectorIndexState state){if(!Enum.IsDefined(state)||indexGeneration<0||purgeGeneration<0||durableCheckpoint<0||searchVisibleThrough<0||searchVisibleThrough>durableCheckpoint||carrierCount<0)throw new ArgumentOutOfRangeException(nameof(durableCheckpoint));return new(BaseVectorCertificationValidation.Id(collectionId,nameof(collectionId)),BaseVectorCertificationValidation.Id(indexId,nameof(indexId)),indexGeneration,purgeGeneration,durableCheckpoint,searchVisibleThrough,carrierCount,state);} internal BaseVectorCertificationIndexState Copy()=>Create(CollectionId,IndexId,IndexGeneration,PurgeGeneration,DurableCheckpoint,SearchVisibleThrough,CarrierCount,State); }
/** <summary>Contains fault state.</summary> */
public sealed class BaseVectorCertificationFaultState { private BaseVectorCertificationFaultState(BaseVectorCertificationFaultKind kind,int target,int observed,bool consumed,bool terminal){Kind=kind;TargetOccurrence=target;ObservedOccurrences=observed;Consumed=consumed;Terminal=terminal;} /** <summary>Gets kind.</summary> */ public BaseVectorCertificationFaultKind Kind{get;} /** <summary>Gets target.</summary> */ public int TargetOccurrence{get;} /** <summary>Gets observations.</summary> */ public int ObservedOccurrences{get;} /** <summary>Gets consumption.</summary> */ public bool Consumed{get;} /** <summary>Gets terminal state.</summary> */ public bool Terminal{get;} /** <summary>Creates validated fault evidence.</summary> */ public static BaseVectorCertificationFaultState Create(BaseVectorCertificationFaultKind kind,int targetOccurrence,int observedOccurrences,bool consumed,bool terminal){if(!Enum.IsDefined(kind)||targetOccurrence is <1 or >16||observedOccurrences<0||kind==BaseVectorCertificationFaultKind.None&&(observedOccurrences!=0||consumed||terminal)||kind!=BaseVectorCertificationFaultKind.None&&consumed!=(observedOccurrences>=targetOccurrence))throw new ArgumentException("The certification fault evidence is invalid.");return new(kind,targetOccurrence,observedOccurrences,consumed,terminal);} }
/** <summary>Contains one observation page.</summary> */
public sealed class BaseVectorCertificationObservationPage { private BaseVectorCertificationObservationPage(long earliest,long next,bool more,IReadOnlyList<BaseVectorCertificationObservation> entries){EarliestRetainedSequence=earliest;NextSequence=next;HasMore=more;Entries=Array.AsReadOnly(entries.Select(static value=>value.Copy()).ToArray());} /** <summary>Gets earliest retained sequence.</summary> */ public long EarliestRetainedSequence{get;} /** <summary>Gets successor sequence.</summary> */ public long NextSequence{get;} /** <summary>Gets continuation state.</summary> */ public bool HasMore{get;} /** <summary>Gets entries.</summary> */ public IReadOnlyList<BaseVectorCertificationObservation> Entries{get;} /** <summary>Creates a bounded deeply owned observation page.</summary> */ public static BaseVectorCertificationObservationPage Create(long earliestRetainedSequence,long nextSequence,bool hasMore,IReadOnlyList<BaseVectorCertificationObservation> entries){ReadOnlyCollection<BaseVectorCertificationObservation> copy=BaseVectorCertificationValidation.Copy(entries,0,256,nameof(entries),static value=>value.Copy());if(earliestRetainedSequence<0||nextSequence<earliestRetainedSequence||copy.Where((value,index)=>index>0&&value.Sequence<=copy[index-1].Sequence).Any()||copy.Count>0&&nextSequence<copy[^1].Sequence)throw new ArgumentOutOfRangeException(nameof(nextSequence));return new(earliestRetainedSequence,nextSequence,hasMore,copy);} }
/** <summary>Contains one safe copied observation.</summary> */
public sealed class BaseVectorCertificationObservation { private BaseVectorCertificationObservation(long sequence,BaseVectorCertificationObservationKind kind,DateTimeOffset timestamp,string name,string? code,string? status,double? numeric,TimeSpan? duration,string? payload,IReadOnlyList<BaseVectorCertificationObservationFact> facts){Sequence=sequence;Kind=kind;Timestamp=timestamp;Name=name;Code=code;Status=status;NumericValue=numeric;Duration=duration;RenderedTestPayload=payload;Facts=Array.AsReadOnly(facts.Select(static value=>value.Copy()).ToArray());} /** <summary>Gets sequence.</summary> */ public long Sequence{get;} /** <summary>Gets kind.</summary> */ public BaseVectorCertificationObservationKind Kind{get;} /** <summary>Gets time.</summary> */ public DateTimeOffset Timestamp{get;} /** <summary>Gets name.</summary> */ public string Name{get;} /** <summary>Gets code.</summary> */ public string? Code{get;} /** <summary>Gets status.</summary> */ public string? Status{get;} /** <summary>Gets numeric value.</summary> */ public double? NumericValue{get;} /** <summary>Gets duration.</summary> */ public TimeSpan? Duration{get;} /** <summary>Gets rendered test payload.</summary> */ public string? RenderedTestPayload{get;} /** <summary>Gets facts.</summary> */ public IReadOnlyList<BaseVectorCertificationObservationFact> Facts{get;} /** <summary>Creates a bounded deeply owned observation.</summary> */ public static BaseVectorCertificationObservation Create(long sequence,BaseVectorCertificationObservationKind kind,DateTimeOffset timestamp,string name,IReadOnlyList<BaseVectorCertificationObservationFact> facts,string? code=null,string? status=null,double? numericValue=null,TimeSpan? duration=null,string? renderedTestPayload=null){ReadOnlyCollection<BaseVectorCertificationObservationFact> copy=BaseVectorCertificationValidation.Copy(facts,0,32,nameof(facts),static value=>value.Copy());if(!Enum.IsDefined(kind)||sequence<1||timestamp.Offset!=TimeSpan.Zero||duration is { } elapsed&&elapsed<TimeSpan.Zero||numericValue is { } number&&!double.IsFinite(number)||copy.Select(static value=>value.Key).Distinct(StringComparer.Ordinal).Count()!=copy.Count)throw new ArgumentException("The certification observation is invalid.");return new(sequence,kind,timestamp,BaseVectorCertificationValidation.Id(name,nameof(name)),code is null?null:BaseVectorCertificationValidation.Id(code,nameof(code)),status is null?null:BaseVectorCertificationValidation.Id(status,nameof(status)),numericValue,duration,renderedTestPayload is null?null:BaseVectorCertificationValidation.Text(renderedTestPayload,nameof(renderedTestPayload)),copy);} internal BaseVectorCertificationObservation Copy()=>Create(Sequence,Kind,Timestamp,Name,Facts,Code,Status,NumericValue,Duration,RenderedTestPayload); }
/** <summary>Contains one observation fact.</summary> */
public sealed class BaseVectorCertificationObservationFact { private BaseVectorCertificationObservationFact(string key,string value){Key=key;Value=value;} /** <summary>Gets key.</summary> */ public string Key{get;} /** <summary>Gets value.</summary> */ public string Value{get;} /** <summary>Creates a bounded immutable observation fact.</summary> */ public static BaseVectorCertificationObservationFact Create(string key,string value)=>new(BaseVectorCertificationValidation.Id(key,nameof(key)),BaseVectorCertificationValidation.Text(value,nameof(value))); internal BaseVectorCertificationObservationFact Copy()=>Create(Key,Value); }
