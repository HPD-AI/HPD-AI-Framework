
namespace HPD.Base;

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
        string applicationId = "hpd.base.application")
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
        return new BaseExportedSubjectContract<TSubject>(installed);
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
