using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Owns immutable activation provenance for a handler-scoped session.</summary>
internal sealed record BaseActivationSessionProvenance
{
    internal required string ActivationId { get; init; }
    internal required int AttemptNumber { get; init; }
    internal required long ClaimEpoch { get; init; }
    internal required System.Collections.Immutable.ImmutableArray<byte> FencingToken { get; init; }
    internal required string WorkerIdentity { get; init; }
    internal required long CancellationGeneration { get; init; }
    internal required string StoreInstanceId { get; init; }
    internal required long RestoreEpoch { get; init; }
    internal required System.Collections.Immutable.ImmutableArray<byte> DefinitionChecksum { get; init; }

    internal static BaseActivationSessionProvenance From(BaseActivationClaimAuthority claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (string.IsNullOrWhiteSpace(claim.ActivationId) || claim.AttemptNumber <= 0 || claim.ClaimEpoch <= 0
            || claim.FencingToken.Length != 32 || string.IsNullOrWhiteSpace(claim.WorkerIdentity)
            || claim.CancellationGeneration < 0 || string.IsNullOrWhiteSpace(claim.StoreInstanceId)
            || claim.RestoreEpoch < 0 || claim.DefinitionChecksum.Length != 32)
            throw new InvalidOperationException("base.activation.sessionProvenanceInvalid");
        return new BaseActivationSessionProvenance
        {
            ActivationId = new string(claim.ActivationId.AsSpan()),
            AttemptNumber = claim.AttemptNumber,
            ClaimEpoch = claim.ClaimEpoch,
            FencingToken = claim.FencingToken.ToArray().ToImmutableArray(),
            WorkerIdentity = new string(claim.WorkerIdentity.AsSpan()),
            CancellationGeneration = claim.CancellationGeneration,
            StoreInstanceId = new string(claim.StoreInstanceId.AsSpan()),
            RestoreEpoch = claim.RestoreEpoch,
            DefinitionChecksum = claim.DefinitionChecksum.ToArray().ToImmutableArray(),
        };
    }

    internal bool Matches(BaseActivationClaimAuthority claim) =>
        string.Equals(ActivationId, claim.ActivationId, StringComparison.Ordinal)
        && AttemptNumber == claim.AttemptNumber && ClaimEpoch == claim.ClaimEpoch
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(FencingToken.AsSpan(), claim.FencingToken.AsSpan())
        && string.Equals(WorkerIdentity, claim.WorkerIdentity, StringComparison.Ordinal)
        && CancellationGeneration == claim.CancellationGeneration
        && string.Equals(StoreInstanceId, claim.StoreInstanceId, StringComparison.Ordinal)
        && RestoreEpoch == claim.RestoreEpoch
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(DefinitionChecksum.AsSpan(), claim.DefinitionChecksum.AsSpan());
}

/// <summary>
/// Binds application operations to one trusted principal and stable scope.
/// </summary>
public sealed class BaseSession
{
    private readonly IBaseRecordRuntime _runtime;
    private readonly TimeProvider _timeProvider;
    private readonly PrincipalContext _principal;
    private readonly BaseSessionOptions _options;
    private readonly IFileObjectService? _files;
    private readonly IBaseDependencyReferenceFactory? _dependencies;
    private readonly IBaseRealtimeFeedSource? _realtime;
    private readonly IBaseLiveQueryCoordinator? _liveQueries;
    private readonly IBaseRegisteredReadRuntime? _reads;
    private readonly IServiceProvider _services;
    private readonly string _applicationId;
    private readonly BaseActivationSessionProvenance? _activationProvenance;

    internal BaseSession(
        IBaseRecordRuntime runtime,
        TimeProvider timeProvider,
        PrincipalContext principal,
        BaseSessionOptions options,
        IFileObjectService? files = null,
        IBaseDependencyReferenceFactory? dependencies = null,
        IBaseRealtimeFeedSource? realtime = null,
        IBaseLiveQueryCoordinator? liveQueries = null,
        IBaseRegisteredReadRuntime? reads = null,
        int maxQueryPageSize = 500,
        IServiceProvider? services = null,
        string applicationId = "hpd.base.application",
        BaseActivationSessionProvenance? activationProvenance = null)
    {
        _runtime = runtime;
        _timeProvider = timeProvider;
        _principal = principal;
        _options = options;
        _files = files;
        _dependencies = dependencies;
        _realtime = realtime;
        _liveQueries = liveQueries;
        _reads = reads;
        _services = services ?? EmptyServiceProvider.Instance;
        _applicationId = new string(applicationId.AsSpan());
        _activationProvenance = activationProvenance;
        MaxQueryPageSize = maxQueryPageSize;
    }

    /// <summary>
    /// Opens typed operations for a registered collection contract.
    /// </summary>
    public BaseCollectionSession<T> Collection<T>(BaseCollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return new BaseCollectionSession<T>(this, collection);
    }

    /// <summary>Begins a provider-required atomic mutation batch.</summary>
    public BaseBatchBuilder Atomic() =>
        new(this, BaseRecordBatchExecutionMode.Atomic);

    /// <summary>Begins an identified provider-required atomic mutation batch.</summary>
    public BaseBatchBuilder Atomic(BaseMutationRequestIdentity requestIdentity)
    {
        ArgumentNullException.ThrowIfNull(requestIdentity);
        return new(this, BaseRecordBatchExecutionMode.Atomic, requestIdentity);
    }

    /// <summary>Begins an ordered independent-commit mutation batch.</summary>
    public BaseBatchBuilder OrderedIndependent() =>
        new(this, BaseRecordBatchExecutionMode.OrderedIndependent);

    /// <summary>Begins an ordered independent batch that stops on failure.</summary>
    public BaseBatchBuilder OrderedStopOnFailure() =>
        new(this, BaseRecordBatchExecutionMode.OrderedStopOnFailure);

    /// <summary>Gets bucket-bound file operations for this session identity.</summary>
    public BaseSessionFiles Files => new(
        _files ?? Missing<IFileObjectService>("files"),
        FileContext());

    /// <summary>Gets typed opaque dependency-reference helpers.</summary>
    public BaseSessionDependencies Dependencies => new(
        _dependencies ?? Missing<IBaseDependencyReferenceFactory>("dependencies"),
        _options.TenantId);

    /// <summary>Gets valid live, durable, and resume feed builders.</summary>
    public BaseSessionRealtime Realtime => new(
        _realtime ?? Missing<IBaseRealtimeFeedSource>("realtime"),
        this);

    /// <summary>Gets valid server-side live-query helpers.</summary>
    public BaseSessionLiveQueries LiveQueries => new(
        _liveQueries ?? Missing<IBaseLiveQueryCoordinator>("live queries"));

    /// <summary>Gets registered typed relational-read operations.</summary>
    public BaseSessionReads Reads => new(
        _reads ?? Missing<IBaseRegisteredReadRuntime>("relational reads"),
        _liveQueries,
        this);

    /// <summary>Gets registered module mutations bound to this session and installed graph.</summary>
    public BaseModuleMutationSession ModuleMutations => new(this);
    /// <summary>Gets graph-installed durable exported-subject lifecycle consumers.</summary>
    public BaseSubjectLifecycleSession SubjectLifecycle => new(this);
    /// <summary>Gets mutually installed exported-subject retirement consumers.</summary>
    public BaseSubjectRetirementSession SubjectRetirements => new(this);

    /// <summary>Gets durable activations bound to this session and installed graph.</summary>
    public BaseActivationSession Activations => new(this);

    /// <summary>Resolves one generated exported-subject contract from this installed application graph.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public BaseExportedSubjectContract<TSubject> GetExportedSubjectContract<TSubject>(BaseGeneratedSubjectRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        BaseGeneratedSubjectRegistration? installed = _services.GetService(typeof(BaseSubjectContractRegistry)) is BaseSubjectContractRegistry registry
            ? registry.Find(typeof(TSubject)) : null;
        if (installed is null || !ReferenceEquals(installed, registration) ||
            !string.Equals(installed.Checksum, registration.Checksum, StringComparison.Ordinal) ||
            !installed.Definition.Audiences.Contains(_options.Audience))
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        return new BaseExportedSubjectContract<TSubject>(this, installed);
    }

    internal IBaseRecordRuntime Runtime => _runtime;
    internal int MaxQueryPageSize { get; }

    internal PrincipalContext Principal => _principal;
    internal IServiceProvider Services => _services;
    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> Serializer<T>(BaseCollection<T> collection) =>
        _services.GetService(typeof(BaseSerializerMetadataOwner)) is BaseSerializerMetadataOwner owner
            ? owner.Resolve(collection)
            : collection.JsonTypeInfo;
    internal string ApplicationId => _applicationId;
    internal HPDBaseEndpointAudience Audience => _options.Audience;
    internal BaseActivationSessionProvenance? ActivationProvenance => _activationProvenance;

    internal bool ActivationDeclaresSourceGrants(params string[] requiredGrantIds)
    {
        if (_activationProvenance is null)
            return true;
        if (requiredGrantIds.Length == 0 || requiredGrantIds.Any(string.IsNullOrWhiteSpace))
            return false;
        BaseActivationRegistry? registry = _services.GetService(typeof(BaseActivationRegistry)) as BaseActivationRegistry;
        if (registry is null)
            return false;
        BaseActivationDefinition[] matches = registry.Definitions
            .Where(definition => definition.Checksum.Length == 32
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    definition.Checksum.AsSpan(), _activationProvenance.DefinitionChecksum.AsSpan()))
            .ToArray();
        return matches is [BaseActivationDefinition definition]
            && requiredGrantIds.All(required => definition.SourceGrantIds.Contains(required, StringComparer.Ordinal));
    }
    internal BaseSession WithActivationProvenance(BaseActivationClaimAuthority claim) => new(
        _runtime, _timeProvider, _principal, _options, _files, _dependencies, _realtime, _liveQueries, _reads,
        MaxQueryPageSize, _services, _applicationId, BaseActivationSessionProvenance.From(claim));
    internal BaseOwnedSubjectScopeEvidence ActivationScope => _options.ProjectId is not null
        ? new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Project, Value = new string(_options.ProjectId.AsSpan()) }
        : _options.TenantId is not null
            ? new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = new string(_options.TenantId.AsSpan()) }
            : new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global };

    internal FileOperationContext FileContext() => new()
    {
        SubjectId = _principal.SubjectId,
        TenantId = _options.TenantId,
        CorrelationId = _options.CorrelationId,
        IsAdmin = _principal.AuthenticationState is
            PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System,
    };

    internal OperationContext Operation(
        BaseOperationKind kind,
        string collectionId,
        RecordId? recordId = null) =>
        new()
        {
            ApplicationId = _applicationId,
            Audience = _options.Audience,
            Operation = kind,
            CollectionId = collectionId,
            RecordId = recordId?.Value,
            TenantId = _options.TenantId,
            ProjectId = _options.ProjectId,
            Mode = _options.Mode,
            CorrelationId = _options.CorrelationId,
            Now = _timeProvider.GetUtcNow(),
        };

    private static TService Missing<TService>(string feature) =>
        throw new InvalidOperationException(
            $"HPD.BASE {feature} support is not installed. Register the feature in AddHPDBase.");

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        internal static EmptyServiceProvider Instance { get; } = new();
        public object? GetService(Type serviceType) => null;
    }
}
