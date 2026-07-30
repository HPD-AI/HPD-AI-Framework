using HPD.Base.Application.Collections;
using HPD.Base.Application.Batches;
using HPD.Base.Records;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Operations;
using HPD.Base.Application.Dependencies;
using HPD.Base.Application.Files;
using HPD.Base.Application.LiveQueries;
using HPD.Base.Application.Realtime;
using HPD.Base.Dependencies;
using HPD.Base.Files.Objects;
using HPD.Base.LiveQuery;
using HPD.Base.Realtime.Feeds;

namespace HPD.Base.Application.Sessions;

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

    internal BaseSession(
        IBaseRecordRuntime runtime,
        TimeProvider timeProvider,
        PrincipalContext principal,
        BaseSessionOptions options,
        IFileObjectService? files = null,
        IBaseDependencyReferenceFactory? dependencies = null,
        IBaseRealtimeFeedSource? realtime = null,
        IBaseLiveQueryCoordinator? liveQueries = null)
    {
        _runtime = runtime;
        _timeProvider = timeProvider;
        _principal = principal;
        _options = options;
        _files = files;
        _dependencies = dependencies;
        _realtime = realtime;
        _liveQueries = liveQueries;
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

    internal IBaseRecordRuntime Runtime => _runtime;

    internal PrincipalContext Principal => _principal;

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
}
