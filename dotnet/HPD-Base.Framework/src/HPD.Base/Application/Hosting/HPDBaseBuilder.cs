using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;
/// <summary>Installs one optional non-store hosting integration into HPD.BASE.</summary>
public interface IHPDBaseBuilderExtension
{
    /// <summary>Gets id.</summary>
    string Id { get; }

    /// <summary>Gets immutable feature-scoped storage requirements captured by <c>Use</c>.</summary>
    System.Collections.Immutable.ImmutableArray<BaseStorageProtectionRequirement> StorageProtectionRequirements => [];

    /// <summary>Gets immutable module capabilities captured by <c>Use</c>.</summary>
    System.Collections.Immutable.ImmutableArray<BaseStorageProtectionCapability> StorageProtectionCapabilities => [];

    /// <summary>Performs configure.</summary>
    void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections);
    /// <summary>Performs initialize Async.</summary>
    ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Collects one deterministic HPD.BASE host configuration.</summary>
public sealed class HPDBaseBuilder
{
    /// <summary>Provides _services.</summary>
    private readonly IServiceCollection _services;
    /// <summary>Provides _collections.</summary>
    private readonly Dictionary<string, CollectionDefinition> _collections = new(StringComparer.Ordinal);
    private readonly List<IBaseSerializerMetadataSource> _serializerMetadata = [];
    /// <summary>Provides _reads.</summary>
    private readonly Dictionary<string, IBaseReadRegistration> _reads = new(StringComparer.Ordinal);
    /// <summary>Provides _dependency Templates.</summary>
    private readonly List<BaseDependencyTemplate> _dependencyTemplates = [];
    /// <summary>Provides _extensions.</summary>
    private readonly List<IHPDBaseBuilderExtension> _extensions = [];
    private readonly Dictionary<string, BaseStorageProtectionRequirement> _applicationStorageRequirements = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Feature, string Module), BaseStorageProtectionRequirement> _featureStorageRequirements = [];
    private readonly Dictionary<string, BaseStorageProtectionCapability> _extensionStorageCapabilities = new(StringComparer.Ordinal);
    private readonly List<BaseSelectionOperationProfile> _selectionProfiles = [];
    private HPDBaseSelectionMutationOptions? _selectionOptions;
    private HPDBaseStoreProvider? _storeProvider;
    /// <summary>Provides _runtime.</summary>
    private Action<HPDBaseRuntimeOptions>? _runtime;
    /// <summary>Provides _files.</summary>
    private Action<HPDBaseFilesOptions>? _files;
    /// <summary>Provides _dependencies.</summary>
    private Action<BaseDependencyOptions>? _dependencies;
    /// <summary>Provides _realtime.</summary>
    private Action<BaseRealtimeOptions>? _realtime;
    /// <summary>Provides _live Queries.</summary>
    private Action<BaseLiveQueryOptions>? _liveQueries;
    /// <summary>Provides the optional InMemory store configuration.</summary>
    private Action<HPDBaseInMemoryStoreOptions>? _inMemoryStore;
    /// <summary>Provides _relational.</summary>
    private Action<HPDBaseRelationalOptions>? _relational;
    /// <summary>Provides _schema.</summary>
    private Action<HPDBaseSchemaOptions>? _schema;
    private Action<HPDBaseTokenProtectionOptions>? _tokenProtection;
    private Action<HPDBaseVectorOptions>? _vector;
    /// <summary>Provides _built.</summary>
    private bool _built;
    internal HPDBaseBuilder(IServiceCollection services) => _services = services;
    /// <summary>Performs configure Runtime.</summary>
    public HPDBaseBuilder ConfigureRuntime(Action<HPDBaseRuntimeOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _runtime += configure;
        return this;
    }

    /// <summary>Configures bounded relational-read and include execution.</summary>
    public HPDBaseBuilder ConfigureRelational(Action<HPDBaseRelationalOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _relational += configure;
        return this;
    }

    /// <summary>Configures bounded schema planning and application.</summary>
    public HPDBaseBuilder ConfigureSchema(Action<HPDBaseSchemaOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _schema += configure;
        return this;
    }

    /// <summary>Configures the shared key ring for durable purpose-bound BASE tokens and artifacts.</summary>
    public HPDBaseBuilder ConfigureTokenProtection(Action<HPDBaseTokenProtectionOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _tokenProtection += configure;
        return this;
    }

    /// <summary>Configures the built-in process-local InMemory provider.</summary>
    /// <remarks>An explicit record provider cannot be combined with InMemory-provider configuration.</remarks>
    public HPDBaseBuilder ConfigureInMemoryStore(Action<HPDBaseInMemoryStoreOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _inMemoryStore += configure;
        return this;
    }

    /// <summary>Selects the one explicit authoritative store bundle.</summary>
    /// <param name="provider">The immutable validated provider descriptor.</param>
    /// <returns>This builder.</returns>
    public HPDBaseBuilder UseStore(HPDBaseStoreProvider provider)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(provider);
        if (_storeProvider is not null)
            throw new InvalidOperationException("base.store.selection.duplicate");
        _storeProvider = provider;
        return this;
    }

    /// <summary>Configures provider-neutral vector execution when generated schema declares vector indexes.</summary>
    /// <param name="configure">The vector runtime configuration callback.</param>
    /// <returns>This builder.</returns>
    public HPDBaseBuilder ConfigureVector(Action<HPDBaseVectorOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _vector += configure;
        return this;
    }

    /// <summary>Installs an advanced provider or hosting extension.</summary>
    public HPDBaseBuilder Use(IHPDBaseBuilderExtension extension)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(extension);
        if (_extensions.Any(item => string.Equals(item.Id, extension.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"HPD.BASE extension '{extension.Id}' is already installed.");
        foreach (BaseStorageProtectionRequirement requirement in extension.StorageProtectionRequirements)
            AddFeatureRequirement(extension.Id, requirement);
        foreach (BaseStorageProtectionCapability capability in extension.StorageProtectionCapabilities)
        {
            BaseStorageProtectionContract.ValidateCapability(capability);
            if (!_extensionStorageCapabilities.TryAdd(capability.OwningModuleId, BaseStorageProtectionContract.Clone(capability)))
                throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageDescriptorInvalid);
        }
        _extensions.Add(extension);
        return this;
    }

    /// <summary>Requires storage protection at application scope.</summary>
    public HPDBaseBuilder RequireStorageProtection(BaseStorageProtectionRequirement requirement)
    {
        EnsureMutable();
        BaseStorageProtectionContract.NormalizeRequirement(requirement);
        if (!_applicationStorageRequirements.TryAdd(requirement.OwningModuleId, BaseStorageProtectionContract.Clone(requirement)))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementDuplicate);
        return this;
    }

    /// <summary>Requires storage protection for one installed feature.</summary>
    public HPDBaseBuilder RequireFeatureStorageProtection(string featureId, BaseStorageProtectionRequirement requirement)
    {
        EnsureMutable();
        BaseApplicationId.Validate(featureId, nameof(featureId));
        AddFeatureRequirement(featureId, requirement);
        return this;
    }

    private void AddFeatureRequirement(string featureId, BaseStorageProtectionRequirement requirement)
    {
        BaseStorageProtectionContract.NormalizeRequirement(requirement);
        if (!_featureStorageRequirements.TryAdd((new string(featureId.AsSpan()), requirement.OwningModuleId), BaseStorageProtectionContract.Clone(requirement)))
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementDuplicate);
    }

    /// <summary>Performs add Collection.</summary>
    public HPDBaseBuilder AddCollection<T>(BaseCollection<T> collection)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(collection);
        if (!_collections.TryAdd(collection.Id, collection.Definition))
            throw new InvalidOperationException($"Collection '{collection.Id}' is already registered.");
        _serializerMetadata.Add(collection);
        return this;
    }

    /// <summary>Registers one generated typed relational read definition.</summary>
    public HPDBaseBuilder AddRead<TParameters, TRow>(BaseReadDefinition<TParameters, TRow> definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        if (!_reads.TryAdd(definition.Id, definition))
            throw new InvalidOperationException($"Read '{definition.Id}' is already registered.");
        return this;
    }

    /// <summary>Registers one immutable transaction-bound selection operation profile.</summary>
    public HPDBaseBuilder AddSelectionOperationProfile(BaseSelectionOperationProfile profile)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(profile);
        ValidateSelectionProfile(profile);
        if (_selectionProfiles.Any(item => string.Equals(item.ApplicationId, profile.ApplicationId, StringComparison.Ordinal)
            && string.Equals(item.Id, profile.Id, StringComparison.Ordinal)
            && item.Version == profile.Version))
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileDuplicate);
        _selectionProfiles.Add(CloneSelectionProfile(profile));
        return this;
    }

    /// <summary>Configures the single immutable host safety envelope for selection mutations.</summary>
    public HPDBaseBuilder ConfigureSelectionMutations(HPDBaseSelectionMutationOptions options)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(options);
        if (_selectionOptions is not null) throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileDuplicate);
        ValidateSelectionLimits(options.HostMaxima);
        if (options.MaximumReceiptIdentityBytes is < 1 or > 4096
            || options.MaximumEvidenceTokenBytes is < 1 or > 4096
            || options.MaximumRouteNameBytes is < 1 or > 128
            || options.MaximumRequestBodyBytes is < 1 or > 1_048_576)
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
        _selectionOptions = options with { HostMaxima = options.HostMaxima with { } };
        return this;
    }

    /// <summary>Performs add Files.</summary>
    public HPDBaseBuilder AddFiles(Action<HPDBaseFilesOptions>? configure = null)
    {
        EnsureMutable();
        if (_files is not null)
            throw new InvalidOperationException("Files are already registered.");
        _files = configure ?? (_ =>
        {
        });
        return this;
    }

    /// <summary>Performs add Dependencies.</summary>
    public HPDBaseBuilder AddDependencies(Action<BaseDependencyOptions>? configure = null, Action<BaseDependencyCatalog>? define = null)
    {
        EnsureMutable();
        if (_dependencies is not null)
            throw new InvalidOperationException("Dependencies are already registered.");
        _dependencies = configure ?? (_ =>
        {
        });
        define?.Invoke(new BaseDependencyCatalog(_dependencyTemplates));
        return this;
    }

    /// <summary>Performs add Realtime.</summary>
    public HPDBaseBuilder AddRealtime(Action<BaseRealtimeOptions>? configure = null)
    {
        EnsureMutable();
        if (_realtime is not null)
            throw new InvalidOperationException("Realtime is already registered.");
        _realtime = configure ?? (_ =>
        {
        });
        return this;
    }

    /// <summary>Performs add Live Queries.</summary>
    public HPDBaseBuilder AddLiveQueries(Action<BaseLiveQueryOptions>? configure = null)
    {
        EnsureMutable();
        if (_liveQueries is not null)
            throw new InvalidOperationException("Live queries are already registered.");
        _liveQueries = configure ?? (_ =>
        {
        });
        return this;
    }

    /// <summary>Performs replace Policy Evaluator.</summary>
    public HPDBaseBuilder ReplacePolicyEvaluator<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    T>()
        where T : class, IPolicyEvaluator
    {
        EnsureMutable();
        _services.Replace(ServiceDescriptor.Singleton<IPolicyEvaluator, T>());
        return this;
    }

    /// <summary>Performs add Descriptor Contributor.</summary>
    public HPDBaseBuilder AddDescriptorContributor<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    T>()
        where T : class, IBaseDescriptorContributor
    {
        EnsureMutable();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, T>());
        return this;
    }

    /// <summary>Performs build.</summary>
    internal void Build()
    {
        if (_built)
            throw new InvalidOperationException("The HPD.BASE builder was already applied.");
        _built = true;
        if (_storeProvider is not null && _inMemoryStore is not null)
            throw new InvalidOperationException("ConfigureInMemoryStore cannot be combined with an explicit HPD.BASE record provider.");
        if (_selectionProfiles.Count != 0 && _selectionOptions is null)
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
        HPDBaseStoreProvider provider = _storeProvider ?? InMemoryProviderInstaller.Create(_inMemoryStore);
        CollectionDefinition[] collections = _collections.Values.ToArray();
        var relationalOptions = new HPDBaseRelationalOptions();
        _relational?.Invoke(relationalOptions);
        relationalOptions.Validate();
        var schemaOptions = new HPDBaseSchemaOptions();
        _schema?.Invoke(schemaOptions);
        schemaOptions.Validate();
        BaseApplicationGraphValidator.Validate(collections, _reads.Values, relationalOptions, schemaOptions);
        BaseSerializerMetadataOwner serializerMetadataOwner = BaseSerializerMetadataOwner.Create(_serializerMetadata.Concat(_reads.Values));
        BaseStorageProtectionGraph storageProtection = BaseStorageProtectionContract.FinalizeGraph(
            _applicationStorageRequirements.Values,
            collections,
            _featureStorageRequirements,
            provider.StorageProtectionCapabilities.Concat(_extensionStorageCapabilities.Values));
        int requiredBinaryMaximum = _collections.Values.SelectMany(static collection => collection.Fields ?? [])
            .Where(static field => string.Equals(field.Format, "base64", StringComparison.Ordinal))
            .Select(static field => field.MaximumBytes ?? 0).DefaultIfEmpty().Max();
        if (requiredBinaryMaximum > provider.MaximumBinaryFieldBytes)
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.ProviderCapabilityMissing);
        BaseLogicalSchema logicalSchema = BaseLogicalSchemaFactory.Create(schemaOptions, collections, _reads.Values, storageProtection);
        ValidateIndexCapabilities(collections, provider);
        _services.AddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration>(_reads, StringComparer.Ordinal)));
        _services.AddSingleton(new BaseCollectionRegistry(collections.ToDictionary(static collection => collection.Id, StringComparer.Ordinal)));
        _services.AddSingleton(logicalSchema);
        _services.AddSingleton(storageProtection);
        _services.AddSingleton(serializerMetadataOwner);
        foreach (BaseSelectionOperationProfile profile in _selectionProfiles)
        {
            if (_selectionOptions is null || !Fits(profile, _selectionOptions))
                throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
            if (!collections.Any(collection => string.Equals(collection.Id, profile.CollectionId, StringComparison.Ordinal)))
                throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
        }
        _services.AddSingleton(new BaseSelectionProfileRegistry(_selectionProfiles));
        if (_selectionOptions is not null) _services.AddSingleton(_selectionOptions);
        _services.AddHPDBaseRuntime(_runtime).UseFailClosedPolicy();
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(relationalOptions));
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(schemaOptions));
        HPDBaseTokenProtectionOptions tokenOptions = CreateTokenOptions();
        _tokenProtection?.Invoke(tokenOptions);
        ValidateTokenOptions(tokenOptions);
        tokenOptions = CloneTokenOptions(tokenOptions);
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(tokenOptions));
        _services.AddSingleton(new BaseTokenProtectionRegistration(_tokenProtection is not null));
        _services.TryAddSingleton<BaseOpaqueTokenProtector>();
        _services.AddSingleton<IBaseSchemaPlanProtector, DefaultBaseSchemaPlanProtector>();
        _services.AddSingleton<IBaseSchemaManager, DefaultBaseSchemaManager>();
        _services.AddSingleton<BaseSchemaCommandHost>();
        _services.TryAddSingleton<IBaseApplicationLifetime, DefaultBaseApplicationLifetime>();
        _services.AddSingleton<IBaseProviderBootstrap, DefaultBaseProviderBootstrap>();
        if (_files is not null)
        {
            _services.AddHPDBaseFiles(options =>
            {
                _files(options);
                for (var index = 0; index < options.Buckets.Count; index++)
                {
                    if (options.Buckets[index].ProviderRef is null)
                    {
                        options.Buckets[index] = options.Buckets[index] with
                        {
                            ProviderRef = new FileProviderRef("inmemory")
                        };
                    }
                }
            });
            _services.AddHPDBaseFilesInMemoryProvider();
        }

        if (_dependencies is not null)
            _services.AddHPDBaseDependencies(_dependencies, _dependencyTemplates.ToArray());
        if (_realtime is not null)
            _services.AddHPDBaseRealtime(_realtime);
        if (_liveQueries is not null)
        {
            if (_dependencies is null)
                throw new InvalidOperationException("Live queries require AddDependencies.");
            _services.AddHPDBaseLiveQuery(_liveQueries);
        }

        IHPDBaseBuilderExtension[] installedExtensions = _extensions.ToArray();
        foreach (IHPDBaseBuilderExtension extension in installedExtensions)
            extension.Configure(_services, collections);
        var installation = new HPDBaseStoreInstallationContext(_services, provider, collections);
        HPDBaseStoreRegistrationReceipt receipt;
        try { receipt = provider.Installer.Configure(installation); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("base.store.", StringComparison.Ordinal)) { throw; }
        catch (Exception) { throw new InvalidOperationException("base.store.providerInvalid"); }
        finally { installation.Complete(); }
        if (receipt is null || receipt.Kind != provider.Kind || receipt.ProtocolVersion != provider.ProtocolVersion ||
            !string.Equals(receipt.SchemaDigest, HPDBaseStoreInstallationContext.ComputeSchemaDigest(collections), StringComparison.Ordinal) ||
            !receipt.ContributorIds.SequenceEqual(provider.RegistrationIds, StringComparer.Ordinal))
            throw new InvalidOperationException("base.store.providerInvalid");
        ConfigureVectorRuntime(collections);
        _services.AddSingleton(new HPDBaseInstalledFeatures { Provider = provider.Kind, StoreProvider = provider, StoreReceipt = receipt, CollectionIds = collections.Select(static item => item.Id).ToArray(), ReadIds = _reads.Keys.ToArray(), Files = _files is not null, Dependencies = _dependencies is not null, Realtime = _realtime is not null, LiveQueries = _liveQueries is not null, ExtensionIds = installedExtensions.Select(static item => item.Id).ToArray(), Extensions = installedExtensions, LogicalSchema = logicalSchema });
        _services.TryAddSingleton<IHPDBaseApplication, DefaultHPDBaseApplication>();
        _services.TryAddSingleton<IHPDBaseAdministration, DefaultHPDBaseAdministration>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseApplicationHealthContributor>());
    }

    private static HPDBaseTokenProtectionOptions CreateTokenOptions() => new()
    {
        ActiveKey = new BaseOpaqueTokenKey { Id = 0, Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), IssueNotBefore = DateTimeOffset.UnixEpoch },
    };

    private static void ValidateTokenOptions(HPDBaseTokenProtectionOptions options)
    {
        BaseOpaqueTokenKey[] keys = [options.ActiveKey, .. options.DecryptionKeys ?? []];
        if (keys.Any(static key => key?.Key is not { Length: 32 }) || keys.Select(static key => key.Id).Distinct().Count() != keys.Length)
            throw new ArgumentException("Token protection keys must have unique IDs and exactly 32 bytes.", nameof(options));
        if (keys.Any(static key => key.IssueNotBefore.Offset != TimeSpan.Zero
            || key.IssueUntil is { } issueUntil && issueUntil.Offset != TimeSpan.Zero
            || key.DecryptUntil is { } decryptUntil && decryptUntil.Offset != TimeSpan.Zero))
            throw new ArgumentException("Token protection key lifecycle instants must use UTC offset zero.", nameof(options));
        if (options.ActiveKey.IssueUntil is null && options.ActiveKey.DecryptUntil is not null)
            throw new ArgumentException("An indefinitely issuing active token key cannot have a finite decryption lifetime.", nameof(options));
        foreach (BaseOpaqueTokenKey key in keys)
        {
            if (key.IssueUntil is { } issueUntil && issueUntil <= key.IssueNotBefore)
                throw new ArgumentException("A token key issuance lifetime is invalid.", nameof(options));
            if (key.DecryptUntil is { } decryptUntil
                && (key.IssueUntil is not { } stopped
                    || decryptUntil < checked(stopped + TimeSpan.FromDays(30))))
                throw new ArgumentException("A retained token key must decrypt for at least 30 days after issuance stops.", nameof(options));
        }
        foreach (BaseOpaqueTokenKey key in options.DecryptionKeys ?? [])
        {
            if (key.IssueUntil is null)
                throw new ArgumentException("A decryption-only token key requires its issuance-stop instant.", nameof(options));
        }
    }

    private static HPDBaseTokenProtectionOptions CloneTokenOptions(HPDBaseTokenProtectionOptions options) => new()
    {
        ActiveKey = CloneKey(options.ActiveKey),
        DecryptionKeys = (options.DecryptionKeys ?? []).Select(CloneKey).ToArray(),
    };

    private static BaseOpaqueTokenKey CloneKey(BaseOpaqueTokenKey key) => new()
    {
        Id = key.Id,
        Key = key.Key.ToArray(),
        IssueNotBefore = key.IssueNotBefore,
        IssueUntil = key.IssueUntil,
        DecryptUntil = key.DecryptUntil,
    };

    /// <summary>Performs validate Index Capabilities.</summary>
    private void ConfigureVectorRuntime(CollectionDefinition[] collections)
    {
        VectorIndexDefinition[] indexes = collections.SelectMany(static collection => collection.VectorIndexes ?? []).ToArray();
        if (indexes.Length == 0)
            return;
        var configured = new HPDBaseVectorOptions();
        _vector?.Invoke(configured);
        configured.Validate();
        var snapshot = new HPDBaseVectorSnapshot(configured.MaxDimensions, configured.MaxTopK, configured.MaxFilterFields, configured.ProviderTimeout, configured.ConsistencyWaitTimeout, configured.ConsistencyTokenLifetime, configured.MaxActiveAndQuarantinedOperations, configured.ShutdownDrainTimeout, configured.AdministrationTimeout, configured.MaxConcurrentRebuilds, configured.DerivedProviderDefaultConsistency);
        if (indexes.Any(index => index.Dimensions > snapshot.MaxDimensions || index.FilterFieldIds.Length > snapshot.MaxFilterFields))
            throw new InvalidOperationException("A declared vector index exceeds the configured vector limits.");
        _services.AddSingleton(snapshot);
        _services.AddSingleton<BaseVectorOperationalState>();
        _services.TryAddSingleton(TimeProvider.System);
        _services.AddSingleton<IBaseVectorRuntime, DefaultBaseVectorRuntime>();
        _services.AddSingleton<IBaseVectorAdministration, DefaultBaseVectorAdministration>();
        _services.AddSingleton<IBaseVectorRebuildService>(static serviceProvider => (DefaultBaseVectorAdministration)serviceProvider.GetRequiredService<IBaseVectorAdministration>());
        _services.AddSingleton<IBaseDescriptorContributor, BaseVectorDescriptorContributor>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseVectorHealthContributor>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseVectorHealthContributor>());
    }

    private void EnsureMutable()
    {
        if (_built) throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageRequirementLate);
    }

    private static void ValidateSelectionProfile(BaseSelectionOperationProfile profile)
    {
        BaseApplicationId.Validate(profile.Id, nameof(profile));
        BaseApplicationId.Validate(profile.ApplicationId, nameof(profile));
        BaseApplicationId.Validate(profile.CollectionId, nameof(profile));
        BaseApplicationId.Validate(profile.RequiredGrantId, nameof(profile));
        if (profile.Version < 1 || !Enum.IsDefined(profile.MutationKind) || profile.Limits is null)
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
        BaseSelectionOperationLimits limits = profile.Limits;
        ValidateSelectionLimits(limits);
        if (profile.HttpProjection is { } http
            && (!Enum.IsDefined(http.Audience)
                || string.IsNullOrWhiteSpace(http.RouteName)
                || http.MaximumRequestBodyBytes is < 1 or > 1_048_576))
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
    }

    private static void ValidateSelectionLimits(BaseSelectionOperationLimits limits)
    {
        long[] byteLimits = [limits.MaximumSelectedBytes, limits.MaximumWrittenBytes, limits.MaximumFactBytes,
            limits.MaximumJournalBytes, limits.MaximumReceiptBytes, limits.MaximumTransientBytes, limits.MaximumResultBytes];
        int[] countLimits = [limits.MaximumQueryNodes, limits.MaximumQueryDepth, limits.MaximumLiteralValues,
            limits.MaximumSelectedRecords, limits.MaximumProducedMutations, limits.MaximumQueryExecutions,
            limits.MaximumReadIntervals, limits.MaximumRelationChecks, limits.MaximumUniqueConstraintChecks,
            limits.MaximumPreviousStateRequirements];
        if (countLimits.Any(static value => value < 1) || byteLimits.Any(static value => value < 1)
            || limits.MaximumProducedMutations != limits.MaximumSelectedRecords
            || limits.MaximumQueryExecutions != 1
            || limits.AcquisitionTimeout <= TimeSpan.Zero
            || limits.ExecutionTimeout <= TimeSpan.Zero
            || limits.CallerCommitObservationTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
    }

    private static bool Fits(BaseSelectionOperationProfile profile, HPDBaseSelectionMutationOptions options)
    {
        BaseSelectionOperationLimits p = profile.Limits, h = options.HostMaxima;
        return p.MaximumQueryNodes <= h.MaximumQueryNodes && p.MaximumQueryDepth <= h.MaximumQueryDepth
            && p.MaximumLiteralValues <= h.MaximumLiteralValues && p.MaximumSelectedRecords <= h.MaximumSelectedRecords
            && p.MaximumSelectedBytes <= h.MaximumSelectedBytes && p.MaximumProducedMutations <= h.MaximumProducedMutations
            && p.MaximumReadIntervals <= h.MaximumReadIntervals && p.MaximumWrittenBytes <= h.MaximumWrittenBytes
            && p.MaximumFactBytes <= h.MaximumFactBytes && p.MaximumJournalBytes <= h.MaximumJournalBytes
            && p.MaximumReceiptBytes <= h.MaximumReceiptBytes && p.MaximumRelationChecks <= h.MaximumRelationChecks
            && p.MaximumUniqueConstraintChecks <= h.MaximumUniqueConstraintChecks
            && p.MaximumPreviousStateRequirements <= h.MaximumPreviousStateRequirements
            && p.MaximumTransientBytes <= h.MaximumTransientBytes && p.MaximumResultBytes <= h.MaximumResultBytes
            && p.AcquisitionTimeout <= h.AcquisitionTimeout && p.ExecutionTimeout <= h.ExecutionTimeout
            && p.CallerCommitObservationTimeout <= h.CallerCommitObservationTimeout
            && (profile.HttpProjection is null || profile.HttpProjection.MaximumRequestBodyBytes <= options.MaximumRequestBodyBytes
                && System.Text.Encoding.UTF8.GetByteCount(profile.HttpProjection.RouteName) <= options.MaximumRouteNameBytes);
    }

    private static BaseSelectionOperationProfile CloneSelectionProfile(BaseSelectionOperationProfile profile) => profile with
    {
        Id = new string(profile.Id.AsSpan()),
        ApplicationId = new string(profile.ApplicationId.AsSpan()),
        CollectionId = new string(profile.CollectionId.AsSpan()),
        RequiredGrantId = new string(profile.RequiredGrantId.AsSpan()),
        HttpProjection = profile.HttpProjection is null ? null : profile.HttpProjection with
        {
            RouteName = new string(profile.HttpProjection.RouteName.AsSpan()),
        },
        Limits = profile.Limits with { },
    };

    private static void ValidateIndexCapabilities(CollectionDefinition[] collections, HPDBaseStoreProvider provider)
    {
        IndexDefinition? required = collections.SelectMany(static collection => collection.Indexes ?? []).FirstOrDefault(static index => index.Enforcement != EnforcementOwner.Advisory);
        if (required is not null && !provider.Capabilities.HasFlag(BaseStoreProviderCapabilities.RequiredIndexes))
            throw new InvalidOperationException($"Required physical index '{required.CollectionId}/{required.Id}' cannot be installed by the selected provider '{provider.Kind}'. Mark it Advisory or select a capable provider.");
    }
}

/// <summary>Represents hPDBase Installed Features.</summary>
public sealed record HPDBaseInstalledFeatures
{
    /// <summary>Gets or sets provider.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets or sets collection Ids.</summary>
    public required string[] CollectionIds { get; init; }
    /// <summary>Gets or sets read Ids.</summary>
    public required string[] ReadIds { get; init; }
    /// <summary>Gets or sets extension Ids.</summary>
    public required string[] ExtensionIds { get; init; }
    /// <summary>Gets or sets files.</summary>
    public bool Files { get; init; }
    /// <summary>Gets or sets dependencies.</summary>
    public bool Dependencies { get; init; }
    /// <summary>Gets or sets realtime.</summary>
    public bool Realtime { get; init; }
    /// <summary>Gets or sets live Queries.</summary>
    public bool LiveQueries { get; init; }
    internal IHPDBaseBuilderExtension[] Extensions { get; init; } = [];
    internal HPDBaseStoreProvider StoreProvider { get; init; } = null!;
    internal HPDBaseStoreRegistrationReceipt StoreReceipt { get; init; } = null!;
    internal BaseLogicalSchema LogicalSchema { get; init; } = null!;
}

/// <summary>Defines validated application dependency-template handles.</summary>
public sealed class BaseDependencyCatalog
{
    /// <summary>Provides _templates.</summary>
    private readonly List<BaseDependencyTemplate> _templates;
    internal BaseDependencyCatalog(List<BaseDependencyTemplate> templates) => _templates = templates;
    /// <summary>Performs define.</summary>
    public BaseDependencyTemplateHandle Define(string id, BaseDependencyKind kind, BaseDependencyVisibility visibility, string? description = null, params ReadOnlySpan<string> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_templates.Any(template => string.Equals(template.Id, id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Dependency template '{id}' is already registered.");
        var template = new BaseDependencyTemplate
        {
            Id = id,
            Kind = kind,
            Visibility = visibility,
            Description = description,
            ParameterNames = parameters.ToArray()
        };
        _templates.Add(template);
        return new BaseDependencyTemplateHandle(template);
    }
}
