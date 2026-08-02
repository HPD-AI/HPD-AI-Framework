using System.Runtime.CompilerServices;

namespace HPD.Base;

internal sealed record BaseRegisteredReadEvaluation<TRow>
{
    /// <summary>Gets or sets the page.</summary>
    public required BasePage<TRow> Page { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public required BaseDependencySet Dependencies { get; init; }
}

internal interface IBaseRegisteredReadRuntime
{
    /// <summary>Executes the execute async operation.</summary>
    ValueTask<OperationResult<BaseRegisteredReadEvaluation<TRow>>> ExecuteAsync<TParameters, TRow>(
        BaseReadDefinition<TParameters, TRow> definition,
        TParameters parameters,
        BaseReadPageRequest? page,
        PrincipalContext principal,
        OperationContext operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes only registered typed relational reads for one principal-bound session.</summary>
public sealed class BaseSessionReads
{
    private readonly IBaseRegisteredReadRuntime _runtime;
    private readonly IBaseLiveQueryCoordinator? _liveQueries;
    private readonly BaseSession _session;

    internal BaseSessionReads(
        IBaseRegisteredReadRuntime runtime,
        IBaseLiveQueryCoordinator? liveQueries,
        BaseSession session)
    {
        _runtime = runtime;
        _liveQueries = liveQueries;
        _session = session;
    }

    /// <summary>Executes one bounded registered read page.</summary>
    public async ValueTask<BaseResult<BasePage<TRow>>> ExecuteAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        BaseReadPageRequest page,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        BaseReadPageRequest validated = BaseReadPageRequest.Create(page.Page, page.PerPage);
        var result = await Evaluate(handle, parameters, validated, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page);
    }

    /// <summary>Executes the complete registered read within its declared bounds.</summary>
    public async ValueTask<BaseResult<TRow[]>> ToArrayAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        var result = await Evaluate(handle, parameters, page: null, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page.Items);
    }

    /// <summary>Returns the first registered-read row or null.</summary>
    public async ValueTask<BaseResult<TRow?>> FirstAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var result = await Evaluate(handle, parameters, BaseReadPageRequest.Create(1, 1), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page.Items.FirstOrDefault());
    }

    /// <summary>Returns whether the registered read has any visible row.</summary>
    public async ValueTask<BaseResult<bool>> AnyAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var result = await Evaluate(handle, parameters, BaseReadPageRequest.Create(1, 1), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page.Items.Length != 0);
    }

    /// <summary>Emits complete non-paged replacement arrays for a registered read.</summary>
    public async IAsyncEnumerable<BaseLiveQueryTransition<TRow[]>> LiveAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        IBaseLiveQueryCoordinator coordinator = _liveQueries ?? throw new InvalidOperationException(
            "HPD.BASE live queries are not installed.");
        await using IBaseLiveQuerySubscription<TRow[]> subscription = await coordinator.SubscribeAsync(
            new BaseLiveQueryRequest<TRow[]>
            {
                QueryId = handle.Id,
                ExecuteAsync = async token =>
                {
                    var evaluation = await Evaluate(handle, parameters, page: null, token).ConfigureAwait(false);
                    if (!evaluation.IsSuccess() || evaluation.Value is null)
                    {
                        string code = evaluation.Error?.Code switch
                        {
                            "base.relational.read.limitExceeded" => "base.relational.read.limitExceeded",
                            "base.relational.read.timeout" => "base.relational.read.timeout",
                            "base.relational.read.schemaNotReady" => "base.relational.read.schemaNotReady",
                            "base.relational.read.snapshotUnavailable" => "base.relational.read.snapshotUnavailable",
                            "base.relational.dependencies.invalid" => "base.relational.dependencies.invalid",
                            _ => "base.relational.read.resultInvalid",
                        };
                        throw new BaseLiveQueryException(code, "Registered read execution failed.");
                    }
                    return new BaseLiveQueryEvaluation<TRow[]>
                    {
                        Value = evaluation.Value.Page.Items,
                        Dependencies = evaluation.Value.Dependencies,
                    };
                },
            }, cancellationToken).ConfigureAwait(false);
        await foreach (BaseLiveQueryTransition<TRow[]> transition in subscription.Transitions
            .WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return transition;
    }

    private ValueTask<OperationResult<BaseRegisteredReadEvaluation<TRow>>> Evaluate<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        BaseReadPageRequest? page,
        CancellationToken cancellationToken) =>
        _runtime.ExecuteAsync(
            handle.Definition,
            parameters,
            page,
            _session.Principal,
            _session.Operation(BaseOperationKind.Query, handle.Id),
            cancellationToken);

    private static void Validate<TParameters, TRow>(BaseReadHandle<TParameters, TRow> handle, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(parameters);
    }
}
