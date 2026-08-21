using Microsoft.Extensions.DependencyInjection;

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
            await bootstrap.EnsureSubjectReadinessAsync(timeout.Token).ConfigureAwait(false);

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
        catch (InvalidOperationException exception) when (string.Equals(exception.Message, "base.store.authorityAmbiguous", StringComparison.Ordinal))
        {
            return Fail("base.store.authorityAmbiguous");
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
    /// <summary>Validates subject-plan dynamic authority after the accepted schema exists.</summary>
    ValueTask EnsureSubjectReadinessAsync(CancellationToken cancellationToken = default);
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

internal sealed class DefaultBaseProviderBootstrap(
    IServiceProvider services,
    HPDBaseInstalledFeatures features,
    IBaseApplicationLifetime lifetime,
    Microsoft.Extensions.Options.IOptions<HPDBaseTokenProtectionOptions> tokenOptions,
    TimeProvider timeProvider) : IBaseProviderBootstrap
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
        ValidateTokenLifetimes(tokenOptions.Value, timeProvider.GetUtcNow());
        var storeContext = new HPDBaseStoreInitializationContext(services, features.StoreProvider, features.StoreReceipt);
        try
        {
            await features.StoreProvider.Installer.InitializeAsync(storeContext, timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            storeContext.Complete();
        }
        foreach (IHPDBaseBuilderExtension extension in features.Extensions)
            await extension.InitializeAsync(services, timeout.Token).ConfigureAwait(false);
        ValidateAuthorityGraph();
        if (features.LogicalSchema.ExportedSubjects.Length != 0)
            await ValidateSubjectPlanReceiptsAsync(requireDynamicAuthority: false, timeout.Token).ConfigureAwait(false);
        if (services.GetService<HPDBaseVectorSnapshot>() is { } snapshot)
        {
            if (!string.Equals(features.Provider, "inmemory", StringComparison.Ordinal) &&
                !services.GetRequiredService<BaseTokenProtectionRegistration>().ExplicitlyConfigured)
                throw new InvalidOperationException("base.vector.tokenProtectionRequired: vector execution requires explicitly configured token protection.");
            IBaseVectorProvider[] providers = services.GetServices<IBaseVectorProvider>().ToArray();
            if (providers.Length != 1 || services.GetServices<IBaseVectorAuthority>().Count() != 1)
                throw new InvalidOperationException("base.vector.providerUnavailable: vector execution requires exactly one provider and authority.");
            if (providers[0].Descriptor.Consistency == BaseVectorProviderConsistency.DerivedJournal && snapshot.DerivedProviderDefaultConsistency is null)
                throw new InvalidOperationException("base.vector.consistencyInvalid: a derived provider requires an explicit consistency default.");
        }
        BaseTextIndexDefinition[] textIndexes = features.CollectionDefinitions
            .SelectMany(static collection => collection.TextIndexes ?? [])
            .ToArray();
        if (textIndexes.Length != 0)
        {
            IBaseTextProvider[] providers = services.GetServices<IBaseTextProvider>().ToArray();
            if (providers.Length != 1 || providers[0].Authority is null)
                throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
            BaseTextProviderDescriptor descriptor = providers[0].Descriptor;
            BaseTextProviderCapability capability = descriptor.Capability;
            if (descriptor.ProviderClass != capability.ProviderClass
                || descriptor.Id is not { Length: >= 1 and <= 128 }
                || descriptor.Version <= 0
                || descriptor.CertificationReceipt.Length != 32
                || !capability.TransactionalMaintenanceSupported
                || !capability.ExactRevisionHydrationSupported
                || !capability.PolicyBeforeRankingSupported
                || !capability.ExactFixedPointScoreSupported)
                throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
            BaseTextExecutionLimits maximum = BaseTextPlatform.ExecutionLimits(capability);
            if (textIndexes.Any(index => !BaseTextIndexContract.Fits(index.Limits, maximum)))
                throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
        }
    }

    /// <inheritdoc />
    public async ValueTask EnsureSubjectReadinessAsync(CancellationToken cancellationToken = default)
    {
        if (features.LogicalSchema.ExportedSubjects.Length == 0) return;
        await ValidateSubjectPlanReceiptsAsync(requireDynamicAuthority: true, cancellationToken).ConfigureAwait(false);
        await services.GetRequiredService<BaseSubjectControlDispatcher>()
            .InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseSubjectRetirementRegistry retirement=services.GetRequiredService<BaseSubjectRetirementRegistry>();
        if(retirement.Consumers.Count!=0||retirement.Policies.Count!=0)
            await services.GetRequiredService<BaseSubjectRetirementControlDispatcher>()
                .InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ValidateSubjectPlanReceiptsAsync(
        bool requireDynamicAuthority,
        CancellationToken cancellationToken)
    {
        RecordStoreRegistration registration = services.GetRequiredService<IRecordStoreRegistry>().GetRegistrations().Single();
        if (registration.Store is not IBaseSubjectValidationPlanReceiptStore receiptStore)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        OperationResult<BaseSubjectValidationPlanReceipt[]> observed =
            await receiptStore.ReadSubjectValidationPlanReceiptsAsync(cancellationToken).ConfigureAwait(false);
        if (!observed.IsSuccess() || observed.Value is null)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);

        BaseGeneratedSubjectRegistration[] contracts = services.GetRequiredService<BaseSubjectContractRegistry>().All
            .OrderBy(static value => value.Definition.ValidationPlan.Id, StringComparer.Ordinal)
            .ThenBy(static value => value.Definition.ValidationPlan.Version)
            .ToArray();
        BaseSubjectValidationPlanReceipt[] receipts = observed.Value;
        if (receipts.Length != contracts.Length)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        if (requireDynamicAuthority && registration.Store is not IAtomicRecordStore)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        IAtomicRecordStore? atomicStore = registration.Store as IAtomicRecordStore;
        Dictionary<string, CollectionDefinition> collections = features.CollectionDefinitions
            .ToDictionary(static value => value.Id, StringComparer.Ordinal);
        for (int index = 0; index < contracts.Length; index++)
        {
            BaseSubjectValidationPlanDefinition plan = contracts[index].Definition.ValidationPlan;
            BaseSubjectValidationPlanReceipt receipt = receipts[index];
            if (!collections.TryGetValue(plan.PrivateCollectionId, out CollectionDefinition? privateCollection))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            OperationResult<BaseAtomicMutationAuthorityRequirement>? authority = requireDynamicAuthority
                ? await atomicStore!.CaptureAtomicMutationAuthorityRequirementAsync(
                    features.LogicalSchema.ApplicationId,
                    [privateCollection],
                    AuthorityAcquisitionLimits(),
                    cancellationToken).ConfigureAwait(false)
                : null;
            if (!string.Equals(receipt.PlanId, plan.Id, StringComparison.Ordinal)
                || receipt.PlanVersion != plan.Version
                || !string.Equals(receipt.PlanChecksum, contracts[index].PlanChecksum, StringComparison.Ordinal)
                || !string.Equals(receipt.StoreInstanceId, features.StoreReceipt.RecordStoreRegistrationId, StringComparison.Ordinal)
                || requireDynamicAuthority && (authority is null || !authority.IsSuccess() || authority.Value is null
                    || receipt.SchemaGeneration != authority.Value.SchemaGeneration
                    || !string.Equals(receipt.StoreInstanceId, authority.Value.StoreInstanceId, StringComparison.Ordinal))
                || receipt.Access != plan.Access
                || receipt.LoweringFormatVersion != 1)
                throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        }
    }

    private static BaseAtomicMutationExecutionLimits AuthorityAcquisitionLimits() => new()
    {
        MaximumItems = 1,
        MaximumQueryNodes = 1,
        MaximumQueryDepth = 1,
        MaximumLiteralValues = 1,
        MaximumSelectedRecords = 1,
        MaximumProducedMutations = 1,
        MaximumQueryExecutions = 1,
        MaximumPreviousStateRequirements = 1,
        MaximumRecordCaptures = 1,
        MaximumRelationTargetCaptures = 1,
        MaximumGenerationReads = 1,
        MaximumGenerationComparisons = 1,
        MaximumGenerationIncrements = 1,
        MaximumGuardNodes = 1,
        MaximumGuardDepth = 1,
        MaximumStatements = 1,
        MaximumBranches = 1,
        MaximumExpressionNodes = 1,
        MaximumSelectedBytes = 1,
        MaximumEvidenceBytes = 1,
        MaximumTransientBytes = 1,
        MaximumReadIntervals = 1,
        MaximumSubjectValidations = 1,
        MaximumAuthorityReads = 1,
        MaximumRelationChecks = 1,
        MaximumUniqueConstraintChecks = 1,
        MaximumRetirementProjections = 1,
        MaximumRetirementBarrierReads = 1,
        MaximumRetirementAcknowledgementReads = 1,
        MaximumRetirementPublications = 1,
        MaximumRequestBytes = 1,
        MaximumGenerationBytes = 1,
        MaximumWrittenBytes = 1,
        MaximumFactBytes = 1,
        MaximumJournalBytes = 1,
        MaximumReceiptBytes = 1,
        MaximumResultBytes = 1,
        MaximumRetirementEvidenceBytes = 1,
        MaximumRetirementPublicationBytes = 1,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(30),
            TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
    };

    private void ValidateAuthorityGraph()
    {
        HPDBaseStoreInstallationMarker[] markers = services.GetServices<HPDBaseStoreInstallationMarker>().ToArray();
        if (markers.Length != 1 || markers[0].Identity != features.StoreReceipt.Identity)
            throw new InvalidOperationException("base.store.authorityAmbiguous");
        RecordStoreRegistration[] registrations = services.GetRequiredService<IRecordStoreRegistry>().GetRegistrations();
        if (registrations.Length != 1)
            throw new InvalidOperationException("base.store.authorityAmbiguous");
        RecordStoreRegistration registration = registrations[0];
        IRecordStore[] recordStores = services.GetServices<IRecordStore>().ToArray();
        if (recordStores.Length != 1 || !ReferenceEquals(recordStores[0], registration.Store) ||
            !string.Equals(registration.StoreId, features.StoreReceipt.RecordStoreRegistrationId, StringComparison.Ordinal))
            throw new InvalidOperationException("base.store.authorityAmbiguous");

        object store = registration.Store;
        IBaseVectorProvider[] vectorProviders = services.GetServices<IBaseVectorProvider>().ToArray();
        IBaseVectorAuthority[] vectorAuthorities = services.GetServices<IBaseVectorAuthority>().ToArray();
        IBaseTextProvider[] textProviders = services.GetServices<IBaseTextProvider>().ToArray();
        foreach (string role in features.StoreReceipt.RequiredRoles)
        {
            bool valid = role switch
            {
                "records" => store is IRecordStore,
                "mutation" => store is IRecordMutationStore,
                "atomic" => store is IAtomicRecordStore,
                "schema" => store is IBaseSchemaStore,
                "relational" => store is IRelationalReadStore,
                "journal" or "history" => store is ITransactionalMutationJournalStore,
                "administration" => store is IRecordStoreAdministration,
                "vector.provider" => vectorProviders.Length == 1,
                "vector.authority" => vectorAuthorities.Length == 1 && vectorProviders.Length == 1 && ReferenceEquals(vectorAuthorities[0], vectorProviders[0]),
                "text.provider" => textProviders.Length == 1,
                "text.authority" => textProviders.Length == 1 && ReferenceEquals(textProviders[0].Authority, textProviders[0]),
                _ => false,
            };
            if (!valid)
                throw new InvalidOperationException("base.store.authorityAmbiguous");
        }

        if (services.GetServices<IRecordMutationStore>().Count() != 1 ||
            services.GetServices<IAtomicRecordStore>().Count() != 1)
            throw new InvalidOperationException("base.store.authorityAmbiguous");
        if (features.StoreReceipt.RequiredRoles.Contains("vector.provider", StringComparer.Ordinal) &&
            (services.GetServices<IBaseVectorAdministrationProvider>().Count() != 1 ||
             !ReferenceEquals(services.GetServices<IBaseVectorAdministrationProvider>().Single(), vectorProviders[0])))
            throw new InvalidOperationException("base.store.authorityAmbiguous");
    }

    private static void ValidateTokenLifetimes(HPDBaseTokenProtectionOptions options, DateTimeOffset now)
    {
        BaseOpaqueTokenKey active = options.ActiveKey;
        if (now < active.IssueNotBefore || active.IssueUntil is { } issueUntil && now >= issueUntil)
            throw new InvalidOperationException("The active BASE token key is outside its issuance lifetime.");
        foreach (BaseOpaqueTokenKey key in options.DecryptionKeys ?? [])
            if (key.DecryptUntil is { } decryptUntil && now >= decryptUntil)
                throw new InvalidOperationException("A retained BASE token key is outside its decryption lifetime.");
    }
}
