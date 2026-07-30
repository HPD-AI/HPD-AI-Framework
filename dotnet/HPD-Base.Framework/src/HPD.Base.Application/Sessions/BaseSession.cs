using HPD.Base.Application.Collections;
using HPD.Base.Application.Batches;
using HPD.Base.Records;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Operations;

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

    internal BaseSession(
        IBaseRecordRuntime runtime,
        TimeProvider timeProvider,
        PrincipalContext principal,
        BaseSessionOptions options)
    {
        _runtime = runtime;
        _timeProvider = timeProvider;
        _principal = principal;
        _options = options;
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

    internal IBaseRecordRuntime Runtime => _runtime;

    internal PrincipalContext Principal => _principal;

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
}
