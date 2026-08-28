using System.Runtime.CompilerServices;

namespace HPD.Base;

internal sealed record BaseRegisteredReadEvaluation<TRow>
{
    /// <summary>Gets or sets the page.</summary>
    public required BasePage<TRow> Page { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public required BaseDependencySet Dependencies { get; init; }
    /// <summary>Gets the recursively owned provider-snapshot authority.</summary>
    public required BaseRegisteredReadSnapshotAuthority Authority { get; init; }
}

/// <summary>Captures one contributing collection generation from a registered read.</summary>
public sealed record BaseRegisteredReadCollectionAuthority
{
    /// <summary>Gets the stable collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the collection generation observed with the returned rows.</summary>
    public required long CollectionGeneration { get; init; }
}

/// <summary>Represents deeply owned store and schema authority captured with a registered read.</summary>
public sealed record BaseRegisteredReadSnapshotAuthority
{
    /// <summary>Gets the installed application identifier.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the selected logical store identifier.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the provider store-instance identifier.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the restore epoch observed with the rows.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the schema generation observed with the rows.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the complete accepted logical-schema checksum.</summary>
    public required System.Collections.Immutable.ImmutableArray<byte> LogicalSchemaChecksum { get; init; }
    /// <summary>Gets all contributing collection generations in ordinal identifier order.</summary>
    public required System.Collections.Immutable.ImmutableArray<BaseRegisteredReadCollectionAuthority> Collections { get; init; }
    /// <summary>Gets the opaque checksum over the complete snapshot authority.</summary>
    public required System.Collections.Immutable.ImmutableArray<byte> AuthorityChecksum { get; init; }
}

/// <summary>Returns a typed registered-read page together with its exact snapshot authority.</summary>
/// <typeparam name="TRow">The generated registered-read row type.</typeparam>
public sealed record BaseRegisteredReadResult<TRow>
{
    /// <summary>Gets the complete bounded typed page.</summary>
    public required BasePage<TRow> Page { get; init; }
    /// <summary>Gets the authority captured in the same provider snapshot as the page.</summary>
    public required BaseRegisteredReadSnapshotAuthority Authority { get; init; }
}

/// <summary>Returns the first typed registered-read row and its exact snapshot authority.</summary>
/// <typeparam name="TRow">The generated registered-read row type.</typeparam>
public sealed record BaseRegisteredReadFirstResult<TRow>
{
    /// <summary>Gets the first row, or null when the authorized result was empty.</summary>
    public TRow? Item { get; init; }
    /// <summary>Gets the authority captured even when the authorized result was empty.</summary>
    public required BaseRegisteredReadSnapshotAuthority Authority { get; init; }
}

internal interface IBaseRegisteredReadRuntime
{
    /// <summary>Executes the execute async operation.</summary>
    ValueTask<OperationResult<BaseRegisteredReadEvaluation<TRow>>> ExecuteAsync<TParameters, TRow>(
        BaseReadDefinition<TParameters, TRow> definition,
        TParameters parameters,
        BaseRegisteredReadWindow? window,
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
        var result = await Evaluate(handle, parameters, PageWindow(validated), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page);
    }

    /// <summary>Executes one bounded registered-read page and preserves its same-snapshot authority.</summary>
    public async ValueTask<BaseResult<BaseRegisteredReadResult<TRow>>> ExecuteWithAuthorityAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        BaseReadPageRequest page,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        BaseReadPageRequest validated = BaseReadPageRequest.Create(page.Page, page.PerPage);
        var result = await Evaluate(handle, parameters, PageWindow(validated), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => new BaseRegisteredReadResult<TRow>
        {
            Page = evaluation.Page,
            Authority = evaluation.Authority,
        });
    }

    /// <summary>Executes one bounded arbitrary-offset registered-read window.</summary>
    public async ValueTask<BaseResult<BasePage<TRow>>> ExecuteOffsetAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        BaseReadOffsetRequest window,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        BaseReadOffsetRequest validated = BaseReadOffsetRequest.Create(window.Offset, window.Limit);
        var result = await Evaluate(handle, parameters, new BaseRegisteredReadWindow
        {
            Kind = BaseRegisteredReadWindowKind.Offset,
            Offset = validated.Offset,
            Limit = validated.Limit,
        }, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page);
    }

    /// <summary>Executes one bounded offset window and preserves its same-snapshot authority.</summary>
    public async ValueTask<BaseResult<BaseRegisteredReadResult<TRow>>> ExecuteOffsetWithAuthorityAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        BaseReadOffsetRequest window,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        BaseReadOffsetRequest validated = BaseReadOffsetRequest.Create(window.Offset, window.Limit);
        var result = await Evaluate(handle, parameters, new BaseRegisteredReadWindow
        {
            Kind = BaseRegisteredReadWindowKind.Offset,
            Offset = validated.Offset,
            Limit = validated.Limit,
        }, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => new BaseRegisteredReadResult<TRow>
        {
            Page = evaluation.Page,
            Authority = evaluation.Authority,
        });
    }

    /// <summary>Executes the complete registered read within its declared bounds.</summary>
    public async ValueTask<BaseResult<TRow[]>> ToArrayAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        var result = await Evaluate(handle, parameters, window: null, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page.Items);
    }

    /// <summary>Executes the complete bounded read and preserves its same-snapshot authority.</summary>
    public async ValueTask<BaseResult<BaseRegisteredReadResult<TRow>>> ToArrayWithAuthorityAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        var result = await Evaluate(handle, parameters, window: null, cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => new BaseRegisteredReadResult<TRow>
        {
            Page = evaluation.Page,
            Authority = evaluation.Authority,
        });
    }

    /// <summary>Returns the first registered-read row or null.</summary>
    public async ValueTask<BaseResult<TRow?>> FirstAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        if (handle.Definition.Plan.Topology == BaseRelationalReadTopology.CompoundCount)
            return Unsupported<TRow?>();
        var result = await Evaluate(handle, parameters, PageWindow(BaseReadPageRequest.Create(1, 1)), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => evaluation.Page.Items.FirstOrDefault());
    }

    /// <summary>Returns the first registered-read row and preserves authority for empty results.</summary>
    public async ValueTask<BaseResult<BaseRegisteredReadFirstResult<TRow>>> FirstWithAuthorityAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        if (handle.Definition.Plan.Topology == BaseRelationalReadTopology.CompoundCount)
            return Unsupported<BaseRegisteredReadFirstResult<TRow>>();
        var result = await Evaluate(handle, parameters, PageWindow(BaseReadPageRequest.Create(1, 1)), cancellationToken).ConfigureAwait(false);
        return BaseResultMapper.Map(result, static evaluation => new BaseRegisteredReadFirstResult<TRow>
        {
            Item = evaluation.Page.Items.FirstOrDefault(),
            Authority = evaluation.Authority,
        });
    }

    /// <summary>Returns whether the registered read has any visible row.</summary>
    public async ValueTask<BaseResult<bool>> AnyAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Validate(handle, parameters);
        if (handle.Definition.Plan.Topology == BaseRelationalReadTopology.CompoundCount)
            return Unsupported<bool>();
        var result = await Evaluate(handle, parameters, PageWindow(BaseReadPageRequest.Create(1, 1)), cancellationToken).ConfigureAwait(false);
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
                    var evaluation = await Evaluate(handle, parameters, window: null, token).ConfigureAwait(false);
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
        BaseRegisteredReadWindow? window,
        CancellationToken cancellationToken) =>
        _runtime.ExecuteAsync(
            handle.Definition,
            parameters,
            window,
            _session.Principal,
            _session.Operation(BaseOperationKind.Query, handle.Id),
            cancellationToken);

    private static BaseRegisteredReadWindow PageWindow(BaseReadPageRequest page) => new()
    {
        Kind = BaseRegisteredReadWindowKind.Page,
        Page = page.Page,
        PerPage = page.PerPage,
    };

    private static void Validate<TParameters, TRow>(BaseReadHandle<TParameters, TRow> handle, TParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(parameters);
    }

    private static BaseResult<T> Unsupported<T>() => BaseProviderResultContract.Failure<T>(
        OperationStatus.CapabilityUnavailable,
        new BaseError
        {
            Code = "base.relational.read.terminalUnsupported",
            Message = "The registered-read terminal is not supported for this topology.",
            Category = ErrorCategory.Unsupported,
        });
}
