namespace HPD.Base.Testing;

/// <summary>Classifies the vector provider topology under certification.</summary>
public enum BaseVectorCertificationProviderClass { /// <summary>Shares the authoritative commit boundary.</summary>
    CoLocatedTransactional, /// <summary>Consumes an ordered retained journal.</summary>
    DerivedJournal }

/// <summary>Identifies one closed certification fault.</summary>
public enum BaseVectorCertificationFaultKind
{
    /// <summary>No fault.</summary>
    None,
    /// <summary>Fails before sending work.</summary>
    FailBeforeSend,
    /// <summary>Accepts work and loses its response.</summary>
    AcceptThenLoseResponse,
    /// <summary>Accepts only a bounded prefix.</summary>
    PartialBatchSuccess,
    /// <summary>Replays accepted work.</summary>
    DuplicateReplay,
    /// <summary>Delays searchable visibility.</summary>
    DelaySearchVisibility,
    /// <summary>Loses a checkpoint comparison.</summary>
    CheckpointCompareExchangeLoss,
    /// <summary>Publishes a checkpoint ahead of carriers.</summary>
    CheckpointAheadOfCarrier,
    /// <summary>Publishes a checkpoint behind carriers.</summary>
    CheckpointBehindCarrier,
    /// <summary>Returns an empty page below a finite head.</summary>
    EmptyPageBelowCapturedHead,
    /// <summary>Introduces a journal gap.</summary>
    JournalGap,
    /// <summary>Overtakes retained history.</summary>
    RetentionOvertake,
    /// <summary>Expires a lease.</summary>
    LeaseExpiry,
    /// <summary>Rejects a fenced writer.</summary>
    FencingLoss,
    /// <summary>Loses a rebuild publication response.</summary>
    RebuildPublishResponseLoss,
    /// <summary>Ignores operation cancellation.</summary>
    NonCooperativeOperation,
    /// <summary>Returns malformed candidates.</summary>
    MalformedCandidates,
    /// <summary>Returns duplicate candidates.</summary>
    DuplicateCandidates,
    /// <summary>Returns too many candidates.</summary>
    OversizedCandidates,
    /// <summary>Rejects credentials.</summary>
    CredentialFailure,
    /// <summary>Reports terminal schema incompatibility.</summary>
    TerminalSchemaFailure,
}

/// <summary>Defines the immutable closed fault injected into one certification case.</summary>
public sealed class BaseVectorCertificationFaultPlan
{
    private BaseVectorCertificationFaultPlan(BaseVectorCertificationFaultKind kind, int occurrence, TimeSpan delay, int partialSuccessCount)
    { Kind = kind; Occurrence = occurrence; Delay = delay; PartialSuccessCount = partialSuccessCount; }
    /// <summary>Gets the fault kind.</summary>
    public BaseVectorCertificationFaultKind Kind { get; }
    /// <summary>Gets the targeted one-based occurrence.</summary>
    public int Occurrence { get; }
    /// <summary>Gets the bounded injected delay.</summary>
    public TimeSpan Delay { get; }
    /// <summary>Gets the accepted prefix for partial success.</summary>
    public int PartialSuccessCount { get; }
    /// <summary>Creates and validates one immutable fault plan.</summary>
    public static BaseVectorCertificationFaultPlan Create(BaseVectorCertificationFaultKind kind, int occurrence = 1, TimeSpan delay = default, int partialSuccessCount = 0)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == BaseVectorCertificationFaultKind.None && (occurrence != 1 || delay != TimeSpan.Zero || partialSuccessCount != 0))
            throw new ArgumentException("The no-fault plan cannot carry fault parameters.");
        if (occurrence is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(occurrence));
        bool delayKind = kind is BaseVectorCertificationFaultKind.DelaySearchVisibility or BaseVectorCertificationFaultKind.NonCooperativeOperation;
        if (delayKind ? delay < TimeSpan.FromMilliseconds(1) || delay > TimeSpan.FromMinutes(5) : delay != TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        if (kind == BaseVectorCertificationFaultKind.PartialBatchSuccess ? partialSuccessCount is < 1 or > 255 : partialSuccessCount != 0) throw new ArgumentOutOfRangeException(nameof(partialSuccessCount));
        return new(kind, occurrence, delay, partialSuccessCount);
    }
}

/// <summary>Identifies one certification generation transition.</summary>
public enum BaseVectorCertificationTransitionKind
{
    /// <summary>Advances purge generation.</summary>
    AdvancePurgeGeneration,
    /// <summary>Advances restore epoch.</summary>
    AdvanceRestoreEpoch,
    /// <summary>Advances schema generation.</summary>
    AdvanceSchemaGeneration,
    /// <summary>Advances index generation.</summary>
    AdvanceIndexGeneration,
    /// <summary>Advances vector-space generation.</summary>
    AdvanceVectorSpaceGeneration,
}
/// <summary>Identifies one canonical certification mutation.</summary>
public enum BaseVectorCertificationMutationKind
{
    /// <summary>Creates a record.</summary>
    Create,
    /// <summary>Replaces a record.</summary>
    Replace,
    /// <summary>Deletes a record.</summary>
    Delete,
    /// <summary>Purges records.</summary>
    Purge,
}
/// <summary>Identifies one closed certification field value.</summary>
public enum BaseVectorCertificationValueKind
{
    /// <summary>Missing value.</summary>
    Missing,
    /// <summary>Explicit null.</summary>
    Null,
    /// <summary>Boolean value.</summary>
    Boolean,
    /// <summary>Integer value.</summary>
    Integer,
    /// <summary>String value.</summary>
    String,
    /// <summary>Identifier value.</summary>
    Id,
    /// <summary>Vector value.</summary>
    Vector,
}
/// <summary>Identifies one copied observation.</summary>
public enum BaseVectorCertificationObservationKind
{
    /// <summary>Log observation.</summary>
    Log,
    /// <summary>Metric observation.</summary>
    Metric,
    /// <summary>Trace observation.</summary>
    Trace,
    /// <summary>Health observation.</summary>
    Health,
    /// <summary>Diagnostic observation.</summary>
    Diagnostic,
    /// <summary>Operation-result observation.</summary>
    OperationResult,
}

/// <summary>Provides a provider-specific isolated certification host.</summary>
public interface IBaseVectorProviderCertificationFixture
{
    /// <summary>Gets immutable provider identity.</summary>
    BaseVectorCertificationIdentity Identity { get; }
    /// <summary>Gets the provider topology class.</summary>
    BaseVectorCertificationProviderClass ProviderClass { get; }
    /// <summary>Creates one fresh isolated case host.</summary>
    ValueTask<IBaseVectorCertificationHost> CreateHostAsync(BaseVectorCertificationHostRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Owns one isolated certification case.</summary>
public interface IBaseVectorCertificationHost : IAsyncDisposable
{
    /// <summary>Gets the initialized BASE application.</summary>
    IHPDBaseApplication Application { get; }
    /// <summary>Gets authoritative controls.</summary>
    IBaseVectorCertificationAuthorityControl Authority { get; }
    /// <summary>Gets provider controls.</summary>
    IBaseVectorCertificationProviderControl Provider { get; }
    /// <summary>Gets bounded observations.</summary>
    IBaseVectorCertificationObservationSource Observations { get; }
}

/// <summary>Controls the authoritative side of one closed certification case.</summary>
public interface IBaseVectorCertificationAuthorityControl
{
    /// <summary>Seeds the fresh authority once.</summary>
    ValueTask<OperationResult<BaseVectorCertificationSeedResult>> SeedAsync(BaseVectorCertificationSeedRequest request, CancellationToken cancellationToken = default);
    /// <summary>Commits ordered canonical mutations.</summary>
    ValueTask<OperationResult<BaseVectorCertificationMutationResult>> CommitAsync(BaseVectorCertificationMutationRequest request, CancellationToken cancellationToken = default);
    /// <summary>Captures one finite authority head.</summary>
    ValueTask<OperationResult<BaseVectorCertificationAuthorityHead>> CaptureHeadAsync(CancellationToken cancellationToken = default);
    /// <summary>Applies one closed generation transition.</summary>
    ValueTask<OperationResult<BaseVectorCertificationTransitionResult>> TransitionAsync(BaseVectorCertificationTransitionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Prunes retained history.</summary>
    ValueTask<OperationResult<BaseVectorCertificationPruneResult>> PruneHistoryAsync(BaseVectorCertificationPruneRequest request, CancellationToken cancellationToken = default);
    /// <summary>Returns bounded copied authority state.</summary>
    ValueTask<OperationResult<BaseVectorCertificationAuthorityState>> InspectAsync(CancellationToken cancellationToken = default);
}

/// <summary>Controls the provider side of one closed certification case.</summary>
public interface IBaseVectorCertificationProviderControl
{
    /// <summary>Consumes ordered derived work.</summary>
    ValueTask<OperationResult<BaseVectorCertificationAdvanceResult>> AdvanceAsync(BaseVectorCertificationAdvanceRequest request, CancellationToken cancellationToken = default);
    /// <summary>Publishes accepted searchable visibility.</summary>
    ValueTask<OperationResult<BaseVectorCertificationVisibilityResult>> PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest request, CancellationToken cancellationToken = default);
    /// <summary>Runs the provider's real rebuild path.</summary>
    ValueTask<OperationResult<BaseVectorCertificationRebuildResult>> RebuildAsync(BaseVectorCertificationRebuildRequest request, CancellationToken cancellationToken = default);
    /// <summary>Returns bounded copied provider state.</summary>
    ValueTask<OperationResult<BaseVectorCertificationProviderState>> InspectAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns safe fault-consumption state.</summary>
    ValueTask<OperationResult<BaseVectorCertificationFaultState>> InspectFaultAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads bounded copied certification observations.</summary>
public interface IBaseVectorCertificationObservationSource
{
    /// <summary>Reads one monotonic observation page.</summary>
    ValueTask<OperationResult<BaseVectorCertificationObservationPage>> ReadAsync(BaseVectorCertificationObservationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Contains stable provider and adapter identity used in a report.</summary>
public sealed class BaseVectorCertificationIdentity
{
    private BaseVectorCertificationIdentity(int protocolVersion, string packageId, string packageVersion, string adapterVersion, string sdkVersion, string? serverVersion, string? nativeVersion, string runtimeIdentifier, string topologyId, BaseVectorCertificationProviderClass providerClass)
    { ProtocolVersion = protocolVersion; PackageId = packageId; PackageVersion = packageVersion; AdapterVersion = adapterVersion; SdkVersion = sdkVersion; ServerVersion = serverVersion; NativeVersion = nativeVersion; RuntimeIdentifier = runtimeIdentifier; TopologyId = topologyId; ProviderClass = providerClass; }
    /// <summary>Gets the certification protocol version.</summary>
    public int ProtocolVersion { get; }
    /// <summary>Gets the provider package identifier.</summary>
    public string PackageId { get; }
    /// <summary>Gets the provider package version.</summary>
    public string PackageVersion { get; }
    /// <summary>Gets the certification adapter version.</summary>
    public string AdapterVersion { get; }
    /// <summary>Gets the exact provider SDK or protocol version.</summary>
    public string SdkVersion { get; }
    /// <summary>Gets the exact server version when applicable.</summary>
    public string? ServerVersion { get; }
    /// <summary>Gets the exact native-library version when applicable.</summary>
    public string? NativeVersion { get; }
    /// <summary>Gets the supported runtime identifier.</summary>
    public string RuntimeIdentifier { get; }
    /// <summary>Gets the topology identifier.</summary>
    public string TopologyId { get; }
    /// <summary>Gets the declared provider topology class.</summary>
    public BaseVectorCertificationProviderClass ProviderClass { get; }
    /// <summary>Creates a deeply owned bounded certification identity.</summary>
    public static BaseVectorCertificationIdentity Create(int protocolVersion, string packageId, string packageVersion, string adapterVersion, string sdkVersion, string runtimeIdentifier, string topologyId, BaseVectorCertificationProviderClass providerClass, string? serverVersion = null, string? nativeVersion = null) =>
        new(protocolVersion,
            BaseVectorCertificationValidation.Id(packageId, nameof(packageId)),
            BaseVectorCertificationValidation.Id(packageVersion, nameof(packageVersion)),
            BaseVectorCertificationValidation.Id(adapterVersion, nameof(adapterVersion)),
            BaseVectorCertificationValidation.Id(sdkVersion, nameof(sdkVersion)),
            serverVersion is null ? null : BaseVectorCertificationValidation.Id(serverVersion, nameof(serverVersion)),
            nativeVersion is null ? null : BaseVectorCertificationValidation.Id(nativeVersion, nameof(nativeVersion)),
            BaseVectorCertificationValidation.Id(runtimeIdentifier, nameof(runtimeIdentifier)),
            BaseVectorCertificationValidation.Id(topologyId, nameof(topologyId)),
            providerClass);
    internal BaseVectorCertificationIdentity Copy() => Create(ProtocolVersion, PackageId, PackageVersion, AdapterVersion, SdkVersion, RuntimeIdentifier, TopologyId, ProviderClass, ServerVersion, NativeVersion);
}

/// <summary>Contains one immutable case-host request.</summary>
public sealed class BaseVectorCertificationHostRequest
{
    internal BaseVectorCertificationHostRequest(string caseId, int seed, BaseVectorCertificationSchema schema, DateTimeOffset timeOrigin, TimeSpan deadline, BaseVectorCertificationFaultPlan fault)
    { CaseId = caseId; Seed = seed; Schema = schema; TimeOrigin = timeOrigin; Deadline = deadline; Fault = fault; }
    /// <summary>Gets the protocol case identifier.</summary>
    public string CaseId { get; }
    /// <summary>Gets the deterministic seed.</summary>
    public int Seed { get; }
    /// <summary>Gets the frozen protocol-owned certification schema.</summary>
    public BaseVectorCertificationSchema Schema { get; }
    /// <summary>Gets the fake UTC origin.</summary>
    public DateTimeOffset TimeOrigin { get; }
    /// <summary>Gets the case deadline.</summary>
    public TimeSpan Deadline { get; }
    /// <summary>Gets the closed fault plan.</summary>
    public BaseVectorCertificationFaultPlan Fault { get; }
}

/// <summary>Describes the fixed protocol-owned certification schema.</summary>
public sealed class BaseVectorCertificationSchema
{
    internal static BaseVectorCertificationSchema Version1 { get; } = new();
    private BaseVectorCertificationSchema() { }
    /// <summary>Gets the stable certification schema identifier.</summary>
    public string Id => "hpd.base.vector.certification.v1";
    /// <summary>Gets the schema protocol version.</summary>
    public int Version => 1;
    /// <summary>Gets the canonical certification collection identifier.</summary>
    public string CollectionId => "hpd.base.vector.certification.records";
    /// <summary>Gets the canonical cosine index identifier.</summary>
    public string CosineIndexId => "hpd.base.vector.certification.cosine";
    /// <summary>Gets the canonical Euclidean index identifier.</summary>
    public string EuclideanIndexId => "hpd.base.vector.certification.euclidean";
    /// <summary>Gets the canonical dot-product index identifier.</summary>
    public string DotProductIndexId => "hpd.base.vector.certification.dot";
    /// <summary>Gets the canonical vector field identifier.</summary>
    public string VectorFieldId => "hpd.base.vector.certification.vector";
    /// <summary>Gets the canonical tenant filter field identifier.</summary>
    public string TenantFieldId => "hpd.base.vector.certification.tenant";
}
