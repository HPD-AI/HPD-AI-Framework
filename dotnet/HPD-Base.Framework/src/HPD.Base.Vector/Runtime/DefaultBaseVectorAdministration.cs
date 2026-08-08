namespace HPD.Base;

internal sealed class DefaultBaseVectorAdministration(
    IEnumerable<IBaseVectorAdministrationProvider> providers,
    BaseCollectionRegistry collections,
    IBasePolicyOrchestrator policy,
    HPDBaseVectorSnapshot options,
    TimeProvider timeProvider,
    BaseVectorOperationalState operationalState) : IBaseVectorAdministration, IBaseVectorRebuildService
{
    private readonly SemaphoreSlim _rebuildSlots = new(options.MaxConcurrentRebuilds, options.MaxConcurrentRebuilds);
    public ValueTask<OperationResult<BaseVectorIndexStatus[]>> ListAsync(CancellationToken cancellationToken = default) =>
        Provider<BaseVectorIndexStatus[]>(provider => provider.ListAsync(cancellationToken), cancellationToken);

    public ValueTask<OperationResult<BaseVectorIndexStatus>> GetAsync(string collectionId, string vectorIndexId, CancellationToken cancellationToken = default) =>
        Provider<BaseVectorIndexStatus>(provider => provider.GetAsync(collectionId, vectorIndexId, cancellationToken), cancellationToken);

    public async ValueTask<OperationResult<BaseVectorRebuildResult>> RebuildAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!collections.Collections.TryGetValue(request.CollectionId, out CollectionDefinition? collection) ||
            (collection.VectorIndexes ?? []).SingleOrDefault(index => string.Equals(index.Id, request.VectorIndexId, StringComparison.Ordinal)) is not { } index)
            return OperationResults.NotFound<BaseVectorRebuildResult>(new BaseError { Code = "base.vector.indexNotFound", Message = "The vector index was not found.", Category = ErrorCategory.NotFound });
        var operation = new OperationContext { Operation = BaseOperationKind.VectorRebuild, CollectionId = collection.Id, Mode = OperationMode.System, Now = timeProvider.GetUtcNow() };
        OperationResult<BasePolicyEvaluation> authorized = await policy.EvaluateWriteAsync(new BasePolicyRequest { Principal = request.Principal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.VectorIndex, VectorIndexId = index.Id, VectorSpaceId = index.VectorSpaceId }, cancellationToken).ConfigureAwait(false);
        if (!authorized.Status.IsSuccess()) return new OperationResult<BaseVectorRebuildResult> { Status = OperationStatus.PolicyDenied, Error = new BaseError { Code = "base.vector.unauthorized", Message = "The vector rebuild is not authorized.", Category = ErrorCategory.Authorization } };
        IBaseVectorAdministrationProvider[] installed = providers.ToArray();
        if (installed.Length != 1) return Unavailable<BaseVectorRebuildResult>();
        if (!await _rebuildSlots.WaitAsync(options.AdministrationTimeout, cancellationToken).ConfigureAwait(false)) return Timeout<BaseVectorRebuildResult>();
        operationalState.Enter();
        var lifetime = new CancellationTokenSource(options.AdministrationTimeout);
        Task<OperationResult<BaseVectorRebuildResult>> work = installed[0].RebuildAsync(request, lifetime.Token).AsTask();
        bool release = true;
        try
        {
            return await work.WaitAsync(options.AdministrationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!work.IsCompleted) { release = false; operationalState.Quarantine(); ReleaseWhenComplete(work, lifetime); }
            return new OperationResult<BaseVectorRebuildResult> { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseVectorErrorCodes.Cancelled, Message = "The vector rebuild wait was cancelled.", Category = ErrorCategory.Store } };
        }
        catch (TimeoutException)
        {
            if (!work.IsCompleted) { release = false; operationalState.Quarantine(); ReleaseWhenComplete(work, lifetime); }
            return Timeout<BaseVectorRebuildResult>();
        }
        catch (Exception)
        {
            if (!work.IsCompleted) { release = false; operationalState.Quarantine(); ReleaseWhenComplete(work, lifetime); }
            return Unavailable<BaseVectorRebuildResult>();
        }
        finally
        {
            if (release) { lifetime.Dispose(); operationalState.Exit(); _rebuildSlots.Release(); }
        }
    }

    private async ValueTask<OperationResult<T>> Provider<T>(Func<IBaseVectorAdministrationProvider, ValueTask<OperationResult<T>>> invoke, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IBaseVectorAdministrationProvider[] installed = providers.ToArray();
        if (installed.Length != 1) return Unavailable<T>();
        return await invoke(installed[0]).ConfigureAwait(false);
    }

    private void ReleaseWhenComplete(Task work, CancellationTokenSource lifetime) => _ = work.ContinueWith(static (_, state) => { var owned = ((SemaphoreSlim Slots, CancellationTokenSource Lifetime, BaseVectorOperationalState State))state!; owned.Lifetime.Dispose(); owned.State.ReleaseQuarantine(); owned.Slots.Release(); }, (_rebuildSlots, lifetime, operationalState), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private static OperationResult<T> Unavailable<T>() => new() { Status = OperationStatus.CapabilityUnavailable, Error = new BaseError { Code = BaseVectorErrorCodes.ProviderUnavailable, Message = "The vector provider is unavailable.", Category = ErrorCategory.Capability } };
    private static OperationResult<T> Timeout<T>() => new() { Status = OperationStatus.StoreError, Error = new BaseError { Code = BaseVectorErrorCodes.Timeout, Message = "The vector administration operation exceeded its deadline.", Category = ErrorCategory.Store } };
}
