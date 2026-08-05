namespace HPD.Base;
/// <summary>Identifies the asynchronous HPD.BASE application lifecycle state.</summary>
public enum BaseApplicationReadinessState
{
    /// <summary>Identifies not Started.</summary>
NotStarted,
    /// <summary>Identifies initializing.</summary>
Initializing,
    /// <summary>Identifies ready.</summary>
Ready,
    /// <summary>Identifies failed.</summary>
Failed
}

/// <summary>Reports bounded public application readiness.</summary>
public sealed record BaseApplicationReadiness
{
    /// <summary>Gets the current lifecycle state.</summary>
    public required BaseApplicationReadinessState State { get; init; }
    /// <summary>Gets the active accepted schema generation.</summary>
    public long? SchemaGeneration { get; init; }
    /// <summary>Gets whether the selected provider is ready.</summary>
    public bool ProviderReady { get; init; }
    /// <summary>Gets whether all required schema assets are ready.</summary>
    public bool RequiredAssetsReady { get; init; }
    /// <summary>Gets the bounded schema compatibility classification.</summary>
    public string? SchemaCompatibility { get; init; }
    /// <summary>Gets bounded safe lifecycle diagnostics.</summary>
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
}

/// <summary>Owns coalesced asynchronous host initialization and readiness.</summary>
public interface IHPDBaseApplication
{
    /// <summary>Gets the atomically published readiness snapshot.</summary>
    BaseApplicationReadiness CurrentReadiness { get; }

    /// <summary>Gets host-only provider administration after successful initialization.</summary>
    IHPDBaseAdministration Administration { get; }

    /// <summary>Initializes the selected provider and accepted schema once.</summary>
    ValueTask<OperationResult<BaseApplicationReadiness>> InitializeAsync(CancellationToken cancellationToken = default);
}

internal sealed class DefaultHPDBaseApplication(IBaseProviderBootstrap bootstrap, HPDBaseInstalledFeatures features, IRecordStoreRegistry stores, IBaseApplicationLifetime lifetime, Microsoft.Extensions.Options.IOptions<HPDBaseSchemaOptions> schemaOptions, IHPDBaseAdministration administration) : IHPDBaseApplication
{
    private readonly Lock _gate = new();
    private Task<OperationResult<BaseApplicationReadiness>>? _initialization;
    private BaseApplicationReadiness _readiness = new()
    {
        State = BaseApplicationReadinessState.NotStarted,
    };
    /// <summary>Gets current Readiness.</summary>
    public BaseApplicationReadiness CurrentReadiness => Volatile.Read(ref _readiness);

    /// <summary>Gets host-only provider administration after readiness.</summary>
    public IHPDBaseAdministration Administration => CurrentReadiness.State == BaseApplicationReadinessState.Ready
        ? administration
        : throw new InvalidOperationException("HPD.BASE administration is unavailable before successful initialization.");

    /// <summary>Performs initialize Async.</summary>
    public async ValueTask<OperationResult<BaseApplicationReadiness>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task<OperationResult<BaseApplicationReadiness>> shared;
        lock (_gate)
        {
            shared = _initialization ??= InitializeCoreAsync();
        }

        return await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<BaseApplicationReadiness>> InitializeCoreAsync()
    {
        Publish(new BaseApplicationReadiness { State = BaseApplicationReadinessState.Initializing });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Stopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await bootstrap.EnsureInitializedAsync(timeout.Token).ConfigureAwait(false);
            long generation = 0;
            BaseSchemaCompatibility compatibility = BaseSchemaCompatibility.Compatible;
            bool assetsReady = true;
            IRecordStore? recordStore = features.CollectionIds.Length == 0 ? stores.GetStore(features.Provider) : stores.GetStoreForCollection(features.CollectionIds[0]);
            if (recordStore is IBaseSchemaStore schemaStore)
            {
                if (!schemaStore.SchemaExecution.Inspect)
                    return Fail("base.schema.capabilityUnavailable");
                OperationResult<BaseSchemaObservedState> observed = await schemaStore.InspectSchemaAsync(new BaseSchemaInspectionRequest { ApplicationId = features.LogicalSchema.ApplicationId, ExpectedLogicalChecksum = features.LogicalSchema.CanonicalChecksum, Visibility = VisibilityLevel.Admin, InspectionTimeout = schemaOptions.Value.MigrationLeaseTimeout, }, timeout.Token).AsTask().WaitAsync(schemaOptions.Value.MigrationLeaseTimeout, timeout.Token).ConfigureAwait(false);
                if (!observed.IsSuccess() || observed.Value is null)
                    return Fail(observed.Error?.Code ?? "base.schema.inspectFailed");
                generation = observed.Value.Generation;
                compatibility = observed.Value.Compatibility;
                assetsReady = observed.Value.Assets.All(static asset => asset.State == BaseSchemaAssetState.Ready);
                if (compatibility != BaseSchemaCompatibility.Compatible || !assetsReady || !string.Equals(observed.Value.AcceptedChecksum, features.LogicalSchema.CanonicalChecksum, StringComparison.Ordinal))
                    return Fail("base.schema.notReady");
            }

            var ready = new BaseApplicationReadiness
            {
                State = BaseApplicationReadinessState.Ready,
                SchemaGeneration = generation,
                ProviderReady = true,
                RequiredAssetsReady = assetsReady,
                SchemaCompatibility = compatibility.ToString(),
            };
            Publish(ready);
            return OperationResults.Ok(ready);
        }
        catch (OperationCanceledException)
        {
            return Fail("base.application.initializationTimeout");
        }
        catch
        {
            return Fail("base.application.initializationFailed");
        }
    }

    private OperationResult<BaseApplicationReadiness> Fail(string code)
    {
        var failed = new BaseApplicationReadiness
        {
            State = BaseApplicationReadinessState.Failed,
            ProviderReady = false,
            RequiredAssetsReady = false,
            SchemaCompatibility = "unknown",
        };
        Publish(failed);
        return OperationResults.StoreError<BaseApplicationReadiness>(new BaseError { Code = code, Message = "HPD.BASE application initialization failed.", Category = ErrorCategory.Store, });
    }

    private void Publish(BaseApplicationReadiness readiness) => Volatile.Write(ref _readiness, readiness);
}

internal interface IBaseProviderBootstrap
{
    /// <summary>Executes the ensure initialized async operation.</summary>
    ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default);
}

internal interface IBaseApplicationLifetime
{
    /// <summary>Gets the stopping.</summary>
    CancellationToken Stopping { get; }
}

internal sealed class DefaultBaseApplicationLifetime : IBaseApplicationLifetime
{
    /// <summary>Gets stopping.</summary>
    public CancellationToken Stopping => CancellationToken.None;
}

internal sealed class DefaultBaseProviderBootstrap(IServiceProvider services, HPDBaseInstalledFeatures features, IBaseApplicationLifetime lifetime) : IBaseProviderBootstrap
{
    private readonly Lock _gate = new();
    private Task? _initialization;
    /// <summary>Performs ensure Initialized Async.</summary>
    public async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        Task shared;
        lock (_gate)
            shared = _initialization ??= InitializeCoreAsync();
        await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        foreach (IHPDBaseBuilderExtension extension in features.Extensions)
            await extension.InitializeAsync(services, timeout.Token).ConfigureAwait(false);
    }
}
