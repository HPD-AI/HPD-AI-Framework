using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
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
    private readonly List<BaseGeneratedSubjectRegistration> _subjectContracts = [];
    private readonly Dictionary<string, BaseModuleGenerationCellDefinition> _moduleGenerationCells = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, int Version), BaseRegisteredModuleMutationDefinition> _moduleMutations = [];
    private readonly Dictionary<(string Id, int Version), IBaseModuleMutationRegistration> _moduleMutationRegistrations = [];
    private readonly Dictionary<(string Id, int Version), IBaseActivationRegistration> _activationRegistrations = [];
    private readonly Dictionary<(string Id, int Version), IBaseSemanticActivationRegistration> _semanticActivationRegistrations = [];
    private readonly Dictionary<(string Id, int Version), BaseSemanticActivationMigrationDefinition> _semanticActivationMigrations = [];
    private readonly Dictionary<string, BaseSemanticActivationRestoreSelection> _semanticRestoreSelections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BaseSemanticRecoveryAuthorityRegistration> _semanticRecoveryAuthorities = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, int Version), IBaseActivationMigrationRegistration> _activationMigrations = [];
    private readonly Dictionary<(string Id, int Version), BaseScheduleDefinition> _activationSchedules = [];
    private BaseTimeZoneAuthority? _timeZoneAuthority;
    private readonly Dictionary<(string Id, int Version), BaseScheduleRecoveryVerificationKey> _scheduleRecoveryKeys = [];
    private readonly List<BaseSubjectAcquisitionDefinition> _subjectAcquisitions = [];
    private readonly List<BaseSubjectLifecycleConsumerDefinition> _subjectLifecycleConsumers = [];
    private readonly List<BaseSubjectRetirementConsumerDefinition> _subjectRetirementConsumers = [];
    private readonly List<BaseSubjectRetirementPolicy> _subjectRetirementPolicies = [];
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
    private Action<HPDBaseSubjectLifecycleOptions>? _subjectLifecycle;
    private Action<HPDBaseVectorOptions>? _vector;
    /// <summary>Gets the graph-owned policy and grant authority builder.</summary>
    public BasePolicyAuthorityBuilder PolicyAuthority { get; }
    /// <summary>Provides _built.</summary>
    private bool _built;
    internal HPDBaseBuilder(IServiceCollection services)
    {
        _services = services;
        PolicyAuthority = new BasePolicyAuthorityBuilder();
    }
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

    /// <summary>Configures durable exported-subject lifecycle continuation authority.</summary>
    public HPDBaseBuilder ConfigureSubjectLifecycle(Action<HPDBaseSubjectLifecycleOptions> configure)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _subjectLifecycle += configure;
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

    /// <summary>Installs one source-generated exported logical-subject contract.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public HPDBaseBuilder AddExportedSubject(BaseGeneratedSubjectRegistration registration)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(registration);
        if (_subjectContracts.Any(value => value.MarkerType == registration.MarkerType ||
            string.Equals(value.Definition.Id, registration.Definition.Id, StringComparison.Ordinal) &&
            value.Definition.Version == registration.Definition.Version))
            throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
        _subjectContracts.Add(registration);
        return this;
    }

    /// <summary>Registers one authorized L35 exported-subject acquisition projection.</summary>
    public HPDBaseBuilder AddSubjectAcquisition(BaseSubjectAcquisitionDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        try
        {
            BaseApplicationId.Validate(definition.Id, nameof(definition));
            BaseApplicationId.Validate(definition.ContractId, nameof(definition));
            BaseApplicationId.Validate(definition.RegisteredReadId, nameof(definition));
            BaseApplicationId.Validate(definition.RequiredGrantId, nameof(definition));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception);
        }
        if (definition.Version < 1 || definition.ContractVersion < 1 || definition.MaximumResults is < 1 or > 256
            || !Enum.IsDefined(definition.Audience)
            || _subjectAcquisitions.Any(existing => string.Equals(existing.Id, definition.Id, StringComparison.Ordinal)
                || existing.ContractId == definition.ContractId && existing.ContractVersion == definition.ContractVersion
                    && existing.RegisteredReadId == definition.RegisteredReadId && existing.Audience == definition.Audience))
            throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
        _subjectAcquisitions.Add(definition with
        {
            Id = new string(definition.Id.AsSpan()),
            ContractId = new string(definition.ContractId.AsSpan()),
            RegisteredReadId = new string(definition.RegisteredReadId.AsSpan()),
            RequiredGrantId = new string(definition.RequiredGrantId.AsSpan()),
        });
        return this;
    }

    /// <summary>Registers one immutable durable exported-subject lifecycle consumer.</summary>
    public HPDBaseBuilder AddSubjectLifecycleConsumer(BaseSubjectLifecycleConsumerDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        _subjectLifecycleConsumers.Add(BaseSubjectLifecycleRegistry.Normalize(definition));
        return this;
    }

    /// <summary>Registers one consumer-owned advisory or required retirement profile.</summary>
    public HPDBaseBuilder AddSubjectRetirementConsumer(BaseSubjectRetirementConsumerDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        _subjectRetirementConsumers.Add(BaseSubjectRetirementRegistry.Normalize(definition));
        return this;
    }

    /// <summary>Registers one exporter-owned coordinated-retirement policy.</summary>
    public HPDBaseBuilder AddSubjectRetirementPolicy(BaseSubjectRetirementPolicy policy)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(policy);
        _subjectRetirementPolicies.Add(BaseSubjectRetirementRegistry.NormalizePolicy(policy));
        return this;
    }

    /// <summary>Registers one immutable module generation-cell definition.</summary>
    public HPDBaseBuilder AddModuleGenerationCell(BaseModuleGenerationCellDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        BaseModuleMutationContractValidator.ValidateCell(definition);
        if (!_moduleGenerationCells.TryAdd(definition.Id, definition with
        {
            Id = new string(definition.Id.AsSpan()), OwningModuleId = new string(definition.OwningModuleId.AsSpan()),
        })) throw new InvalidOperationException("base.moduleMutation.invalid");
        return this;
    }

    /// <summary>Registers one trusted-host-authored module mutation definition.</summary>
    public HPDBaseBuilder AddModuleMutation(BaseRegisteredModuleMutationDefinition definition)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(definition);
        if (!_moduleMutations.TryAdd((definition.Id, definition.Version), definition))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        return this;
    }

    /// <summary>Registers one generated module mutation and its graph-owned request/result metadata.</summary>
    public HPDBaseBuilder AddModuleMutation<TRequest, TResult>(
        BaseRegisteredModuleMutationDefinition definition,
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(definition.Id, identity.Id, StringComparison.Ordinal)
            || definition.Version != identity.Version
            || !definition.Checksum.ToArray().AsSpan().SequenceEqual(identity.Checksum))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        AddModuleMutation(definition);
        if (!_moduleMutationRegistrations.TryAdd((definition.Id, definition.Version), new BaseModuleMutationRegistration<TRequest, TResult>(definition, identity)))
            throw new InvalidOperationException("base.moduleMutation.invalid");
        _serializerMetadata.Add(identity);
        return this;
    }

    /// <summary>Registers one graph-owned durable activation and its Native-AOT-safe handler factory.</summary>
    public HPDBaseBuilder AddActivation<TInput, TResult>(
        BaseActivationHandlerRegistration<TInput, TResult> registration)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(registration);
        BaseActivationDefinition definition = BaseActivationContract.Seal(registration.Definition);
        if (!string.Equals(definition.Id, registration.Identity.Id, StringComparison.Ordinal) ||
            definition.Version != registration.Identity.Version ||
            !CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), registration.Identity.Checksum.Span))
            throw new InvalidOperationException("base.activation.definitionInvalid");
        if (!_activationRegistrations.TryAdd((definition.Id, definition.Version),
            new BaseActivationRegistration<TInput, TResult>(registration with { Definition = definition })))
            throw new InvalidOperationException("base.activation.definitionDuplicate");
        _serializerMetadata.Add(registration.Identity);
        return this;
    }

    /// <summary>Registers one graph-owned semantic activation identity.</summary>
    public HPDBaseBuilder AddSemanticActivation<TRequest, TDefinition>(BaseSemanticActivationRegistration<TRequest, TDefinition> registration)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(registration);
        var installed = new BaseInstalledSemanticActivationRegistration<TRequest, TDefinition>(registration);
        if (_semanticActivationRegistrations.Keys.Any(key => string.Equals(key.Id, installed.Definition.Id, StringComparison.Ordinal))
            || !_semanticActivationRegistrations.TryAdd((installed.Definition.Id, installed.Definition.Version), installed))
            throw new InvalidOperationException("base.semanticActivation.registrationConflict");
        return this;
    }

    /// <summary>Registers one callback-free graph-owned semantic definition migration.</summary>
    public HPDBaseBuilder AddSemanticActivationMigration(BaseSemanticActivationMigrationDefinition migration)
    {
        EnsureMutable();
        BaseSemanticActivationMigrationDefinition installed = BaseSemanticActivationMigrationContract.Seal(migration);
        if (!_semanticActivationMigrations.TryAdd((installed.Id, installed.Version), installed))
            throw new InvalidOperationException("base.semanticActivation.migrationInvalid");
        return this;
    }

    /// <summary>Selects semantic restore authority for one logical store.</summary>
    public HPDBaseBuilder SetSemanticActivationRestoreSelection(BaseSemanticActivationRestoreSelection selection)
    {
        EnsureMutable(); ArgumentNullException.ThrowIfNull(selection);
        if (!_semanticRestoreSelections.TryAdd(selection.LogicalStoreId, selection))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        return this;
    }

    /// <summary>Registers one certified external semantic recovery authority.</summary>
    public HPDBaseBuilder AddSemanticRecoveryAuthority(BaseSemanticRecoveryAuthorityRegistration registration)
    {
        EnsureMutable(); ArgumentNullException.ThrowIfNull(registration);
        if (!BaseSemanticRecoveryAuthorityContract.IsValid(registration)
            || !_semanticRecoveryAuthorities.TryAdd(registration.Definition.LogicalStoreId, registration))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        return this;
    }

    /// <summary>Registers one callback-free graph-owned activation input migration.</summary>
    public HPDBaseBuilder AddActivationMigration<TSource, TTarget>(
        BaseActivationMigrationRegistration<TSource, TTarget> registration)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(registration);
        var installed = new BaseInstalledActivationMigration<TSource, TTarget>(registration);
        if (!_activationMigrations.TryAdd((installed.Definition.Id, installed.Definition.Version), installed))
            throw new InvalidOperationException("base.activation.migrationDuplicate");
        return this;
    }

    /// <summary>Registers one graph-owned handler-free transactional activation.</summary>
    public HPDBaseBuilder AddActivation<TInput, TResult>(
        BaseTransactionalActivationRegistration<TInput, TResult> registration)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(registration);
        BaseActivationDefinition definition = BaseActivationContract.Seal(registration.Definition);
        if (!string.Equals(definition.Id, registration.Identity.Id, StringComparison.Ordinal)
            || definition.Version != registration.Identity.Version
            || !CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), registration.Identity.Checksum.Span))
            throw new InvalidOperationException("base.activation.definitionInvalid");
        if (!_activationRegistrations.TryAdd((definition.Id, definition.Version),
            new BaseInstalledTransactionalActivationRegistration<TInput, TResult>(registration with { Definition = definition })))
            throw new InvalidOperationException("base.activation.definitionDuplicate");
        _serializerMetadata.Add(registration.Identity);
        return this;
    }

    /// <summary>Registers one graph-owned durable schedule.</summary>
    public HPDBaseBuilder AddSchedule(BaseScheduleDefinition definition)
    {
        EnsureMutable();
        BaseScheduleDefinition sealedDefinition = BaseScheduleDefinitionBuilder.Create(definition);
        if (!_activationSchedules.TryAdd((sealedDefinition.Id, sealedDefinition.Version), sealedDefinition))
            throw new InvalidOperationException("base.activation.scheduleDuplicate");
        return this;
    }

    /// <summary>Installs one exact compiled IANA time-zone authority for durable schedules.</summary>
    public HPDBaseBuilder UseTimeZoneAuthority(BaseTimeZoneAuthority authority)
    {
        EnsureMutable();
        if (_timeZoneAuthority is not null) throw new InvalidOperationException("base.activation.timeZoneDuplicate");
        _timeZoneAuthority = BaseTimeZoneAuthorityBuilder.Create(authority);
        return this;
    }

    /// <summary>Registers one retained graph-owned verification key for schedule disaster recovery.</summary>
    public HPDBaseBuilder AddScheduleRecoveryVerificationKey(BaseScheduleRecoveryVerificationKey key)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(key);
        BaseScheduleRecoveryVerificationKey expected = BaseScheduleRecoveryManifestContract.CreateVerificationKey(
            key.Id, key.Version, key.PublicKey.AsSpan(), key.ActiveFrom, key.RetireAfter);
        if (!CryptographicOperations.FixedTimeEquals(expected.Checksum.AsSpan(), key.Checksum.AsSpan())
            || !_scheduleRecoveryKeys.TryAdd((key.Id, key.Version), key))
            throw new InvalidOperationException("base.activation.recoveryKeyInvalid");
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

    /// <summary>Adds one explicitly identified graph-owned policy evaluator.</summary>
    public HPDBaseBuilder AddPolicyAuthority<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    T>(BasePolicyAuthorityDefinition definition)
        where T : class, IPolicyEvaluator, new()
    {
        EnsureMutable();
        PolicyAuthority.AddPolicy(definition, new T());
        return this;
    }

    /// <summary>Adds one explicitly constructed graph-owned policy evaluator.</summary>
    public HPDBaseBuilder AddPolicyAuthority(
        BasePolicyAuthorityDefinition definition,
        IPolicyEvaluator evaluator)
    {
        EnsureMutable();
        PolicyAuthority.AddPolicy(definition, evaluator);
        return this;
    }

    /// <summary>Adds one graph-owned policy evaluator resolved by its exact registered service type.</summary>
    public HPDBaseBuilder AddPolicyAuthorityFromServices<T>(BasePolicyAuthorityDefinition definition)
        where T : class, IPolicyEvaluator
    {
        EnsureMutable();
        PolicyAuthority.AddPolicyFactory(definition, typeof(T), static services => services.GetRequiredService<T>());
        return this;
    }

    /// <summary>Adds one immutable graph-owned grant authority.</summary>
    public BaseInstalledGrantRegistration AddStaticGrantAuthority(
        BaseGrantAuthorityDefinition definition,
        AccessGrant grant)
    {
        EnsureMutable();
        return PolicyAuthority.AddStaticGrant(definition, grant);
    }

    /// <summary>Adds one dynamic graph-owned grant authority source.</summary>
    public BaseInstalledGrantRegistration AddGrantAuthority(
        BaseGrantAuthorityDefinition definition,
        IBaseGrantAuthoritySource source)
    {
        EnsureMutable();
        return PolicyAuthority.AddGrant(definition, source);
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
        BaseSerializerMetadataOwner serializerMetadataOwner = BaseSerializerMetadataOwner.Create(_serializerMetadata.Concat(_reads.Values));
        foreach (IBaseSerializerMetadataSource source in _serializerMetadata)
            if (source.CollectionDefinition is { } bound) _collections[bound.Id] = bound;
        CollectionDefinition[] collections = _collections.Values.ToArray();
        foreach (BaseRegisteredModuleMutationDefinition definition in _moduleMutations.Values)
        {
            _moduleMutationRegistrations.TryGetValue((definition.Id, definition.Version), out IBaseModuleMutationRegistration? registration);
            if (registration is null) throw new InvalidOperationException("base.moduleMutation.invalid");
            BaseModuleMutationContractValidator.ValidateDefinition(definition, _collections, _moduleGenerationCells, registration);
            if (!BaseModuleMutationCapabilityContract.Supports(definition.Limits, provider.ModuleMutations))
                throw new InvalidOperationException(BaseModuleMutationErrorCodes.CapabilityMissing);
        }
        var moduleMutationRegistry = new BaseModuleMutationRegistry(_moduleMutations.Values, _moduleGenerationCells.Values, _moduleMutationRegistrations.Values);
        var activationRegistry = new BaseActivationRegistry(_activationRegistrations.Values);
        var semanticActivationRegistry = new BaseSemanticActivationRegistry(_semanticActivationRegistrations.Values);
        BaseSemanticActivationRestoreSelection[] semanticRestoreSelections = [.. _semanticRestoreSelections.Values];
        BaseSemanticRecoveryAuthorityRegistration[] semanticRecoveryAuthorities = [.. _semanticRecoveryAuthorities.Values];
        ServiceDescriptor? timeDescriptor = _services.LastOrDefault(static descriptor => descriptor.ServiceType == typeof(TimeProvider));
        TimeProvider graphTimeProvider = timeDescriptor?.ImplementationInstance as TimeProvider
            ?? (semanticActivationRegistry.Definitions.Count == 0 ? TimeProvider.System
                : throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid));
        var activationMigrationRegistry = new BaseActivationMigrationRegistry(_activationMigrations.Values);
        foreach (IBaseActivationMigrationRegistration migration in _activationMigrations.Values)
        {
            BaseActivationDefinition? source = activationRegistry.Find(migration.Definition.Source.Id, migration.Definition.Source.Version);
            BaseActivationDefinition? target = activationRegistry.Find(migration.Definition.Target.Id, migration.Definition.Target.Version);
            if (source is null || target is null
                || !CryptographicOperations.FixedTimeEquals(source.Checksum.AsSpan(), migration.Definition.Source.Checksum.AsSpan())
                || !CryptographicOperations.FixedTimeEquals(target.Checksum.AsSpan(), migration.Definition.Target.Checksum.AsSpan()))
                throw new InvalidOperationException("base.activation.migrationInvalid");
        }
        foreach (BaseActivationDefinition activation in activationRegistry.Definitions)
        {
            IBaseActivationRegistration registration = activationRegistry.Registration(activation.Id, activation.Version)
                ?? throw new InvalidOperationException("base.activation.definitionInvalid");
            switch (activation.TransactionalTarget)
            {
                case BaseSelectionMutationActivationTarget target:
                    BaseSelectionOperationProfile[] matches = _selectionProfiles.Where(profile =>
                        profile.Id == target.ProfileId && profile.Version == target.ProfileVersion
                        && string.Equals(BaseSelectionProfileChecksum.Compute(profile), target.ProfileChecksum, StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException("base.activation.definitionInvalid");
                    if (registration.InputType != typeof(BaseSelectionActivationRequest)
                        || registration.ResultType != typeof(BaseSelectionMutationResult))
                        throw new InvalidOperationException("base.activation.definitionInvalid");
                    break;
                case BaseModuleMutationActivationTarget target:
                    if (!_moduleMutations.TryGetValue((target.OperationId, target.OperationVersion), out BaseRegisteredModuleMutationDefinition? operation)
                        || !string.Equals(Convert.ToHexStringLower(operation.Checksum.ToArray()), target.OperationChecksum, StringComparison.Ordinal))
                        throw new InvalidOperationException("base.activation.definitionInvalid");
                    IBaseModuleMutationRegistration targetRegistration = moduleMutationRegistry.FindRegistration(target.OperationId, target.OperationVersion)
                        ?? throw new InvalidOperationException("base.activation.definitionInvalid");
                    if (registration.InputType != targetRegistration.RequestTypeInfo.Type
                        || registration.ResultType != targetRegistration.ResultTypeInfo.Type)
                        throw new InvalidOperationException("base.activation.definitionInvalid");
                    break;
            }
        }
        foreach (BaseSemanticActivationKeyDefinition semantic in semanticActivationRegistry.Definitions)
        {
            RequireSemanticCapability(provider.SemanticActivations, semantic);
            BaseRegisteredModuleMutationDefinition? ensure = moduleMutationRegistry.Find(semantic.EnsureOperation.OperationId, semantic.EnsureOperation.OperationVersion);
            BaseRegisteredModuleMutationDefinition? retire = moduleMutationRegistry.Find(semantic.RetirementOperation.OperationId, semantic.RetirementOperation.OperationVersion);
            BaseActivationDefinition? activation = activationRegistry.Find(semantic.Activation.Id, semantic.Activation.Version);
            if (ensure is null || retire is null || activation is null
                || !string.Equals(ensure.OwningModuleId, semantic.OwningModuleId, StringComparison.Ordinal)
                || !string.Equals(retire.OwningModuleId, semantic.OwningModuleId, StringComparison.Ordinal)
                || !string.Equals(activation.OwningModuleId, semantic.OwningModuleId, StringComparison.Ordinal)
                || !string.Equals(Convert.ToHexStringLower(ensure.Checksum.ToArray()), semantic.EnsureOperation.OperationChecksum, StringComparison.Ordinal)
                || !string.Equals(Convert.ToHexStringLower(retire.Checksum.ToArray()), semantic.RetirementOperation.OperationChecksum, StringComparison.Ordinal)
                || !CryptographicOperations.FixedTimeEquals(activation.Checksum.AsSpan(), semantic.Activation.Checksum.AsSpan()))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
            IBaseModuleMutationRegistration ensureRegistration = moduleMutationRegistry.FindRegistration(ensure.Id, ensure.Version)
                ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
            IBaseModuleMutationRegistration retireRegistration = moduleMutationRegistry.FindRegistration(retire.Id, retire.Version)
                ?? throw new InvalidOperationException("base.semanticActivation.contractInvalid");
            ValidateSemanticCompaction(semantic, ensureRegistration, retireRegistration);
            ValidateSemanticProgram(ensure.Template, ensureOperation: true);
            ValidateSemanticProgram(retire.Template, ensureOperation: false);
        }
        HashSet<(string, int)> semanticOperations = semanticActivationRegistry.Definitions
            .SelectMany(static value => new[] { (value.EnsureOperation.OperationId, value.EnsureOperation.OperationVersion), (value.RetirementOperation.OperationId, value.RetirementOperation.OperationVersion) })
            .ToHashSet();
        if (semanticOperations.Count != semanticActivationRegistry.Definitions.Count * 2)
            throw new InvalidOperationException("base.semanticActivation.registrationConflict");
        if (semanticActivationRegistry.Definitions.Count > provider.SemanticActivations.MaximumDefinitions)
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
        foreach (BaseRegisteredModuleMutationDefinition operation in moduleMutationRegistry.Operations)
            if (!semanticOperations.Contains((operation.Id, operation.Version)) && HasSemanticProgramNodes(operation.Template))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        ValidateSemanticMigrations(semanticActivationRegistry);
        if ((_semanticActivationMigrations.Values.Any()
                || semanticActivationRegistry.Definitions.Any(static definition => definition.Compaction is not BaseSemanticActivationNoCompaction))
            && !provider.SemanticActivations.MaintenanceSupported)
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
        foreach (BaseScheduleDefinition schedule in _activationSchedules.Values)
        {
            BaseActivationDefinition? activation = activationRegistry.Find(schedule.Activation.Id, schedule.Activation.Version);
            if (activation is null || !CryptographicOperations.FixedTimeEquals(
                activation.Checksum.AsSpan(), schedule.Activation.Checksum.AsSpan()))
                throw new InvalidOperationException("base.activation.scheduleInvalid");
        }
        foreach (IBaseActivationRegistration registration in _activationRegistrations.Values)
            BaseActivationCapabilityContract.Require(provider.Activations, registration.Definition);
        foreach (BaseScheduleDefinition schedule in _activationSchedules.Values)
            BaseActivationCapabilityContract.Require(provider.Activations, schedule);
        var scheduleRegistry = new BaseScheduleRegistry(_activationSchedules.Values);
        var scheduleRecoveryKeys = new BaseScheduleRecoveryKeyRegistry(_scheduleRecoveryKeys.Values);
        var timeZoneRegistry = new BaseTimeZoneRegistry(_timeZoneAuthority);
        foreach (BaseScheduleDefinition schedule in _activationSchedules.Values)
            if (schedule.Expression is BaseCronSchedule { TimeZoneId: var cronZone } && !timeZoneRegistry.Contains(cronZone)
                || schedule.Expression is BaseCalendarSchedule { TimeZoneId: var calendarZone } && !timeZoneRegistry.Contains(calendarZone))
                throw new InvalidOperationException("base.activation.timeZoneUnavailable");
        BaseSubjectContractRegistry subjectRegistry = FinalizeSubjectGraph(collections);
        var subjectLifecycleRegistry = new BaseSubjectLifecycleRegistry(_subjectLifecycleConsumers, subjectRegistry);
        var subjectRetirementRegistry = new BaseSubjectRetirementRegistry(_subjectRetirementConsumers, _subjectRetirementPolicies, subjectLifecycleRegistry);
        if (!BaseSubjectRetirementCapabilityContract.Supports(subjectRetirementRegistry, provider.SubjectRetirement))
            throw new InvalidOperationException(BaseSubjectRetirementErrorCodes.ProviderContractInvalid);
        foreach (BaseGeneratedSubjectRegistration subject in subjectRegistry.All)
            if (!Fits(subject.Definition, provider.SubjectReferences))
                throw new InvalidOperationException(BaseSubjectErrorCodes.GuaranteeUnavailable);
        foreach (IGrouping<(string ContractId, int ContractVersion), BaseInstalledSubjectLifecycleConsumer> group in subjectLifecycleRegistry.All.GroupBy(static value => (value.Definition.ContractId, value.Definition.ContractVersion)))
        {
            if (group.Count() > provider.SubjectLifecycle.MaximumConsumersPerContract ||
                group.Any(value => value.Definition.Limits.MaximumFactsPerPage > provider.SubjectLifecycle.MaximumFactsPerPage ||
                    value.Definition.Limits.MaximumResultBytes > provider.SubjectLifecycle.MaximumResultBytes ||
                    value.Definition.Limits.ReadTimeout > provider.SubjectLifecycle.MaximumReadTimeout ||
                    value.Definition.ReconciliationGrantId is not null && !provider.SubjectLifecycle.ReconciliationSupported))
                throw new InvalidOperationException(BaseSubjectErrorCodes.GuaranteeUnavailable);
        }
        var relationalOptions = new HPDBaseRelationalOptions();
        _relational?.Invoke(relationalOptions);
        relationalOptions.Validate();
        var schemaOptions = new HPDBaseSchemaOptions();
        _schema?.Invoke(schemaOptions);
        schemaOptions.Validate();
        BaseApplicationGraphValidator.Validate(collections, _reads.Values, relationalOptions, schemaOptions);
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
        BaseLogicalSchema logicalSchema = BaseLogicalSchemaFactory.Create(schemaOptions, collections, _reads.Values, storageProtection, subjectRegistry);
        BasePolicyAuthorityOwner policyAuthorityOwner = PolicyAuthority.Freeze(logicalSchema.ApplicationId);
        semanticActivationRegistry.ValidatePolicyAuthority(policyAuthorityOwner);
        foreach (BaseSemanticActivationKeyDefinition semantic in semanticActivationRegistry.Definitions)
            if (!string.Equals(semantic.OwningApplicationId, logicalSchema.ApplicationId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        var lifecycleInspectionAuthorities = new BaseSubjectLifecycleInspectionAuthorityRegistry(
            logicalSchema.ApplicationId, subjectRegistry.All, policyAuthorityOwner);
        ValidateIndexCapabilities(collections, provider);
        _services.AddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration>(_reads, StringComparer.Ordinal)));
        _services.AddSingleton(new BaseCollectionRegistry(collections.ToDictionary(static collection => collection.Id, StringComparer.Ordinal)));
        _services.AddSingleton(logicalSchema);
        _services.AddSingleton(storageProtection);
        _services.AddSingleton(serializerMetadataOwner);
        _services.AddSingleton(policyAuthorityOwner);
        _services.AddSingleton(lifecycleInspectionAuthorities);
        _services.AddSingleton(moduleMutationRegistry);
        _services.AddSingleton(activationRegistry);
        _services.AddSingleton(semanticActivationRegistry);
        _services.AddSingleton(activationMigrationRegistry);
        _services.AddSingleton(scheduleRegistry);
        _services.AddSingleton(timeZoneRegistry);
        foreach (BaseSelectionOperationProfile profile in _selectionProfiles)
        {
            if (_selectionOptions is null || !Fits(profile, _selectionOptions))
                throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
            if (!collections.Any(collection => string.Equals(collection.Id, profile.CollectionId, StringComparison.Ordinal)))
                throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
        }
        _services.AddSingleton(new BaseSelectionProfileRegistry(_selectionProfiles));
        _services.AddSingleton(subjectRegistry);
        _services.AddSingleton(subjectLifecycleRegistry);
        _services.AddSingleton(subjectRetirementRegistry);
        _services.AddSingleton(subjectRetirementRegistry);
        if (_selectionOptions is not null) _services.AddSingleton(_selectionOptions);
        _services.AddHPDBaseRuntime(_runtime).UseFailClosedPolicy();
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(relationalOptions));
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(schemaOptions));
        HPDBaseTokenProtectionOptions tokenOptions = CreateTokenOptions();
        _tokenProtection?.Invoke(tokenOptions);
        if (subjectRegistry.All.Count != 0 && _tokenProtection is null)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
        ValidateTokenOptions(tokenOptions);
        tokenOptions = CloneTokenOptions(tokenOptions);
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(tokenOptions));
        var subjectLifecycleOptions = new HPDBaseSubjectLifecycleOptions();
        _subjectLifecycle?.Invoke(subjectLifecycleOptions);
        subjectLifecycleOptions.Validate();
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(subjectLifecycleOptions));
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
        BaseExportedSubjectDefinition[] installedSubjects = subjectRegistry.All.Select(static subject => subject.Definition).ToArray();
        var installation = new HPDBaseStoreInstallationContext(
            _services, provider, collections, installedSubjects,
            _moduleMutations.Values.ToArray(), _moduleGenerationCells.Values.ToArray(),
            subjectLifecycleRegistry.All.Select(static value => value.Definition).ToArray(),
            lifecycleInspectionAuthorities.All.ToArray(),
            subjectRetirementRegistry.Consumers.Select(static value => value.Definition).ToArray(),
            subjectRetirementRegistry.Policies.Select(static value => value.Definition).ToArray(),
            semanticActivationRegistry.Definitions.ToArray(), _semanticActivationMigrations.Values.ToArray(), logicalSchema.ApplicationId,
            semanticActivationRegistry.OwnerGeneration, semanticActivationRegistry.DefinitionSetChecksum);
        HPDBaseStoreRegistrationReceipt receipt;
        try { receipt = provider.Installer.Configure(installation); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("base.store.", StringComparison.Ordinal)) { throw; }
        catch (Exception) { throw new InvalidOperationException("base.store.providerInvalid"); }
        finally { installation.Complete(); }
        if (receipt is null || receipt.Kind != provider.Kind || receipt.ProtocolVersion != provider.ProtocolVersion ||
            !string.Equals(receipt.SchemaDigest, HPDBaseStoreInstallationContext.ComputeSchemaDigest(
                collections, installedSubjects, _moduleMutations.Values, _moduleGenerationCells.Values,
                subjectLifecycleRegistry.All.Select(static value => value.Definition), lifecycleInspectionAuthorities.All,
                subjectRetirementRegistry.Consumers.Select(static value => value.Definition),
                subjectRetirementRegistry.Policies.Select(static value => value.Definition),
                semanticActivationRegistry.Definitions, _semanticActivationMigrations.Values), StringComparison.Ordinal) ||
            !receipt.ContributorIds.SequenceEqual(provider.RegistrationIds, StringComparer.Ordinal))
            throw new InvalidOperationException("base.store.providerInvalid");
        ConfigureVectorRuntime(collections);
        ConfigureTextRuntime(collections, provider);
        _services.AddSingleton(new HPDBaseInstalledFeatures { Provider = provider.Kind, StoreProvider = provider, StoreReceipt = receipt, CollectionIds = collections.Select(static item => item.Id).ToArray(), CollectionDefinitions = collections, ReadIds = _reads.Keys.ToArray(), Files = _files is not null, Dependencies = _dependencies is not null, Realtime = _realtime is not null, LiveQueries = _liveQueries is not null, ExtensionIds = installedExtensions.Select(static item => item.Id).ToArray(), Extensions = installedExtensions, LogicalSchema = logicalSchema });
        _services.AddSingleton(scheduleRecoveryKeys);
        _services.TryAddSingleton<IHPDBaseApplication, DefaultHPDBaseApplication>();
        _services.TryAddSingleton<IHPDBaseAdministration, DefaultHPDBaseAdministration>();
        _services.TryAddSingleton(_ =>
        {
            var state = new BaseSubjectControlOperationalState();
            if (installedSubjects.Length == 0) state.MarkReady();
            return state;
        });
        _services.TryAddSingleton<BaseSubjectLiveControlHub>();
        _services.TryAddSingleton<BaseSubjectControlDispatcher>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseApplicationHealthContributor>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseSubjectControlHealthContributor>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseSubjectControlHealthContributor>());
        if (subjectLifecycleRegistry.All.Count != 0)
        {
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseSubjectLifecycleHealthContributor>());
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseSubjectLifecycleHealthContributor>());
        }
        if (subjectRetirementRegistry.Consumers.Count != 0 || subjectRetirementRegistry.Policies.Count != 0)
        {
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseSubjectRetirementHealthContributor>());
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseSubjectRetirementHealthContributor>());
        }
        if (_activationRegistrations.Count != 0)
        {
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseActivationHealthContributor>());
            _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseActivationHealthContributor>());
        }
        // External recovery instances are materialized only after every fallible graph and provider
        // validation above has completed. The registry constructor disposes a mismatched instance
        // before throwing, so successful construction is the application owner's publication point.
        var semanticRecoveryRegistry = new BaseSemanticRecoveryAuthorityRegistry(
            semanticRestoreSelections, semanticRecoveryAuthorities, provider.SemanticActivations,
            semanticActivationRegistry.Definitions.Count, graphTimeProvider);
        try { _services.AddSingleton(semanticRecoveryRegistry); }
        catch
        {
            semanticRecoveryRegistry.DisposeAfterFailedPublication();
            throw;
        }
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

    private static void RequireSemanticCapability(BaseSemanticActivationCapability capability,
        BaseSemanticActivationKeyDefinition definition)
    {
        BaseSemanticActivationLimits limits = definition.Limits;
        BaseSemanticActivationExecutionLimits execution = limits.Execution;
        if (!BaseSemanticActivationCapabilityContract.IsValid(capability)
            || definition.RequestSerializerChecksum.Length != 32 || definition.KeyExpressionChecksum.Length != 32
            || limits.MaximumCanonicalKeyBytes > capability.MaximumKeyBytes
            || limits.MaximumLiveSlots > capability.MaximumLiveSlots
            || limits.MaximumRetiredSlots > capability.MaximumRetiredSlots
            || limits.MaximumAbsenceMarkers > capability.MaximumAbsenceMarkers
            || execution.MaximumOperations > capability.MaximumOperationsPerTransaction
            || execution.MaximumScopeDirectoryReads > capability.MaximumScopeDirectoryReads
            || execution.MaximumSlotReads > capability.MaximumSlotReads
            || execution.MaximumActivationReads > capability.MaximumActivationReads
            || execution.MaximumReadIntervals > capability.MaximumReadIntervals
            || execution.MaximumIndexOperations > capability.MaximumIndexOperations
            || execution.MaximumActivationBytes > capability.MaximumActivationBytes
            || execution.MaximumScopeDirectoryBytes > capability.MaximumScopeDirectoryBytes
            || execution.MaximumEvidenceBytes > capability.MaximumEvidenceBytes
            || execution.MaximumReceiptBytes > capability.MaximumReceiptBytes
            || execution.MaximumTransientBytes > capability.MaximumTransientBytes
            || limits.Deadlines.AcquisitionTimeout > capability.Deadlines.AcquisitionTimeout
            || limits.Deadlines.TransactionTimeout > capability.Deadlines.TransactionTimeout
            || limits.Deadlines.CommitObservationTimeout > capability.Deadlines.CommitObservationTimeout
            || limits.Deadlines.ReceiptResolutionTimeout > capability.Deadlines.ReceiptResolutionTimeout
            || limits.Deadlines.MaintenanceTimeout > capability.Deadlines.MaintenanceTimeout
            || limits.Deadlines.QuarantineRetentionTimeout > capability.Deadlines.QuarantineRetentionTimeout)
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
    }

    private void ValidateSemanticMigrations(BaseSemanticActivationRegistry registry)
    {
        var from = new HashSet<(string, int, string)>();
        foreach (BaseSemanticActivationMigrationDefinition migration in _semanticActivationMigrations.Values)
        {
            BaseSemanticActivationKeyDefinition? target = registry.Find(migration.To.Id, migration.To.Version);
            if (target is null || !target.Checksum.AsSpan().SequenceEqual(migration.To.Checksum.AsSpan())
                || migration.From.Version == migration.To.Version
                && migration.From.Checksum.AsSpan().SequenceEqual(migration.To.Checksum.AsSpan())
                || !from.Add((migration.From.Id, migration.From.Version, Convert.ToHexString(migration.From.Checksum.AsSpan()))))
                throw new InvalidOperationException("base.semanticActivation.migrationInvalid");
        }
        foreach (IGrouping<string, BaseSemanticActivationMigrationDefinition> chain in _semanticActivationMigrations.Values.GroupBy(static value => value.From.Id, StringComparer.Ordinal))
        {
            var edges = chain.ToDictionary(static value => (value.From.Version, Convert.ToHexString(value.From.Checksum.AsSpan())));
            foreach (BaseSemanticActivationMigrationDefinition start in chain)
            {
                var visited = new HashSet<(int, string)>(); BaseSemanticActivationDefinitionKey cursor = start.From;
                while (edges.TryGetValue((cursor.Version, Convert.ToHexString(cursor.Checksum.AsSpan())), out BaseSemanticActivationMigrationDefinition? edge))
                {
                    if (!visited.Add((cursor.Version, Convert.ToHexString(cursor.Checksum.AsSpan()))))
                        throw new InvalidOperationException("base.semanticActivation.migrationInvalid");
                    cursor = edge.To;
                }
            }
        }
    }

    private static void ValidateSemanticProgram(BaseModuleMutationTemplate template, bool ensureOperation)
    {
        BaseModuleSemanticActivationStateGuard[] stateGuards = template.Guards
            .OfType<BaseModuleSemanticActivationStateGuard>().ToArray();
        if (stateGuards.Length != 4
            || stateGuards.Select(static value => value.Test).Distinct().Count() != 4)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        foreach (BaseModuleValueExpression expression in SemanticExpressions(template.Result.Value))
        {
            bool valid = ensureOperation
                ? expression is not BaseModuleSemanticActivationRetirementDispositionExpression
                : expression is not (BaseModuleSemanticActivationDispositionExpression or BaseModuleSemanticActivationIdExpression or BaseModuleSemanticActivationWasMaterializedExpression);
            if (!valid) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }

        IReadOnlyDictionary<string, BaseModuleGuard> guards = template.Guards.ToDictionary(static value => value.Id, StringComparer.Ordinal);
        foreach (BaseModuleSemanticActivationStateTest state in Enum.GetValues<BaseModuleSemanticActivationStateTest>())
        {
            bool terminal = state is BaseModuleSemanticActivationStateTest.Retired
                or BaseModuleSemanticActivationStateTest.CompactedAbsent;
            foreach (BaseModuleMutationBlock path in SelectSemanticPaths(template.Body, state, guards))
                if (terminal && ContainsSemanticTerminalWork(path))
                    throw new InvalidOperationException("base.semanticActivation.contractInvalid");
            if (terminal && ExpressionCanSelectSemanticActivationId(template.Result.Value, state, guards))
                throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
    }

    private static IEnumerable<BaseModuleMutationBlock> SelectSemanticPaths(
        BaseModuleMutationBlock block,
        BaseModuleSemanticActivationStateTest state,
        IReadOnlyDictionary<string, BaseModuleGuard> guards)
    {
        List<BaseModuleMutationBlock> paths = [new() { Statements = [] }];
        foreach (BaseModuleStatement statement in block.Statements)
        {
            if (statement is not BaseModuleIfStatement branch)
            {
                paths = paths.Select(path => path with { Statements = [.. path.Statements, statement] }).ToList();
                continue;
            }
            bool? decision = SemanticGuardDecision(branch.GuardId, state, guards, new(StringComparer.Ordinal));
            BaseModuleMutationBlock[] selections = decision switch
            {
                true => [branch.WhenTrue],
                false => [branch.WhenFalse],
                null => [branch.WhenTrue, branch.WhenFalse],
            };
            paths = paths.SelectMany(prefix => selections.SelectMany(selected =>
                SelectSemanticPaths(selected, state, guards).Select(suffix => new BaseModuleMutationBlock
                {
                    Statements = [.. prefix.Statements, .. suffix.Statements],
                }))).ToList();
            if (paths.Count > 4_096) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        }
        return paths;
    }

    private static bool ContainsSemanticTerminalWork(BaseModuleMutationBlock block) =>
        block.Statements.Any(static statement => statement is BaseModuleIncrementGenerationStatement
            or BaseModuleCreateStatement or BaseModulePatchStatement or BaseModuleReplaceStatement
            or BaseModuleDeleteStatement or BaseModuleUpsertStatement);

    private static bool ExpressionCanSelectSemanticActivationId(
        BaseModuleValueExpression expression,
        BaseModuleSemanticActivationStateTest state,
        IReadOnlyDictionary<string, BaseModuleGuard> guards) => expression switch
    {
        BaseModuleSemanticActivationIdExpression => true,
        BaseModuleConditionalExpression conditional => SemanticGuardDecision(conditional.GuardId, state, guards, new(StringComparer.Ordinal)) switch
        {
            true => ExpressionCanSelectSemanticActivationId(conditional.WhenTrue, state, guards),
            false => ExpressionCanSelectSemanticActivationId(conditional.WhenFalse, state, guards),
            null => ExpressionCanSelectSemanticActivationId(conditional.WhenTrue, state, guards)
                || ExpressionCanSelectSemanticActivationId(conditional.WhenFalse, state, guards),
        },
        BaseModuleObjectExpression obj => obj.Properties.Any(value => ExpressionCanSelectSemanticActivationId(value.Value, state, guards)),
        BaseModuleCoalesceExpression coalesce => coalesce.Values.Any(value => ExpressionCanSelectSemanticActivationId(value, state, guards)),
        BaseModuleBinaryNumericExpression numeric => ExpressionCanSelectSemanticActivationId(numeric.Left, state, guards)
            || ExpressionCanSelectSemanticActivationId(numeric.Right, state, guards),
        _ => false,
    };

    private static bool? SemanticGuardDecision(
        string guardId,
        BaseModuleSemanticActivationStateTest state,
        IReadOnlyDictionary<string, BaseModuleGuard> guards,
        HashSet<string> visiting)
    {
        if (!guards.TryGetValue(guardId, out BaseModuleGuard? guard) || !visiting.Add(guardId)) return null;
        try
        {
            if (guard is BaseModuleSemanticActivationStateGuard semantic) return semantic.Test == state;
            if (guard is not BaseModuleLogicalGuard logical) return null;
            bool?[] children = logical.ChildGuardIds.Select(id => SemanticGuardDecision(id, state, guards, visiting)).ToArray();
            return logical.Kind switch
            {
                BaseModuleLogicalGuardKind.Not when children.Length == 1 => children[0] is { } value ? !value : null,
                BaseModuleLogicalGuardKind.And when children.Any(static value => value == false) => false,
                BaseModuleLogicalGuardKind.And when children.All(static value => value == true) => true,
                BaseModuleLogicalGuardKind.Or when children.Any(static value => value == true) => true,
                BaseModuleLogicalGuardKind.Or when children.All(static value => value == false) => false,
                _ => null,
            };
        }
        finally { visiting.Remove(guardId); }
    }

    private void ValidateSemanticCompaction(
        BaseSemanticActivationKeyDefinition definition,
        IBaseModuleMutationRegistration ensure,
        IBaseModuleMutationRegistration retire)
    {
        if (definition.Compaction is BaseSemanticActivationNoCompaction) return;
        if (definition.Compaction is not BaseSemanticActivationSubjectRetirementCompaction compaction)
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        BaseGeneratedSubjectRegistration[] contracts = _subjectContracts.Where(value =>
            value.Definition.Id == compaction.SubjectContract.ContractId
            && value.Definition.Version == compaction.SubjectContract.ContractVersion
            && string.Equals(value.Checksum, Convert.ToHexStringLower(compaction.SubjectContract.ContractChecksum.AsSpan()), StringComparison.Ordinal))
            .ToArray();
        if (contracts.Length != 1) throw new InvalidOperationException("base.semanticActivation.contractInvalid");
        bool Matches(IBaseModuleMutationRegistration registration)
        {
            BaseModuleDtoPropertyBinding[] bindings = registration.RequestBindings.Values
                .Where(value => value.StablePropertyId == compaction.SubjectReferenceRequestPropertyId).ToArray();
            Type? propertyType = bindings.Length == 1 ? bindings[0].PropertyType : null;
            return propertyType?.IsGenericType == true
                && propertyType.GetGenericTypeDefinition() == typeof(BaseSubjectReference<>)
                && propertyType.GenericTypeArguments[0] == contracts[0].MarkerType
                && !bindings[0].Nullable;
        }
        if (!Matches(ensure) || !Matches(retire))
            throw new InvalidOperationException("base.semanticActivation.contractInvalid");
    }

    private static bool HasSemanticProgramNodes(BaseModuleMutationTemplate template) =>
        template.Guards.Any(static guard => guard is BaseModuleSemanticActivationStateGuard)
        || SemanticExpressions(template.Result.Value).Any(static expression => expression is BaseModuleSemanticActivationDispositionExpression
            or BaseModuleSemanticActivationIdExpression or BaseModuleSemanticActivationWasMaterializedExpression
            or BaseModuleSemanticActivationRetirementDispositionExpression);

    private static IEnumerable<BaseModuleValueExpression> SemanticExpressions(BaseModuleValueExpression value)
    {
        yield return value;
        switch (value)
        {
            case BaseModuleObjectExpression obj:
                foreach (BaseModuleObjectPropertyExpression property in obj.Properties)
                    foreach (BaseModuleValueExpression child in SemanticExpressions(property.Value)) yield return child;
                break;
            case BaseModuleCoalesceExpression coalesce:
                foreach (BaseModuleValueExpression item in coalesce.Values)
                    foreach (BaseModuleValueExpression child in SemanticExpressions(item)) yield return child;
                break;
            case BaseModuleConditionalExpression conditional:
                foreach (BaseModuleValueExpression child in SemanticExpressions(conditional.WhenTrue)) yield return child;
                foreach (BaseModuleValueExpression child in SemanticExpressions(conditional.WhenFalse)) yield return child;
                break;
            case BaseModuleBinaryNumericExpression numeric:
                foreach (BaseModuleValueExpression child in SemanticExpressions(numeric.Left)) yield return child;
                foreach (BaseModuleValueExpression child in SemanticExpressions(numeric.Right)) yield return child;
                break;
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

    private void ConfigureTextRuntime(CollectionDefinition[] collections, HPDBaseStoreProvider provider)
    {
        BaseTextIndexDefinition[] indexes = collections.SelectMany(static collection => collection.TextIndexes ?? []).ToArray();
        if (indexes.Length == 0) return;
        if (provider.TextSearch is not { } capability
            || !capability.TransactionalMaintenanceSupported
            || !capability.ExactRevisionHydrationSupported || !capability.PolicyBeforeRankingSupported || !capability.ExactFixedPointScoreSupported)
            throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
        if (collections.Any(collection => (collection.TextIndexes ?? []).Length > capability.MaximumIndexesPerCollection)
            || indexes.Any(index => index.Fields.Length > capability.MaximumFieldsPerIndex || index.FilterFields.Length > capability.MaximumFilterFields))
            throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
        foreach (BaseTextIndexDefinition index in indexes)
        {
            BaseTextExecutionLimits requested = index.Limits, maximum = BaseTextPlatform.ExecutionLimits(capability);
            if (!BaseTextIndexContract.Fits(requested, maximum))
                throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
        }
        if (!_services.Any(static descriptor => descriptor.ServiceType == typeof(IBaseTextProvider)))
            throw new InvalidOperationException(BaseTextErrorCodes.CapabilityUnavailable);
        foreach (BaseTextIndexDefinition index in indexes) BaseTextIndexContract.Seal(index);
        _services.TryAddSingleton(TimeProvider.System);
        _services.AddSingleton<BaseTextCursorCodec>();
        _services.AddSingleton<BaseTextConsistencyTokenCodec>();
        _services.AddSingleton<BaseTextOperationalState>();
        _services.AddSingleton<IBaseTextRuntime, DefaultBaseTextRuntime>();
        _services.AddSingleton<IBaseTextAdministration, DefaultBaseTextAdministration>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseTextHealthContributor>());
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDiagnosticContributor, BaseTextHealthContributor>());
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

    private BaseSubjectContractRegistry FinalizeSubjectGraph(CollectionDefinition[] collections)
    {
        var registry = new BaseSubjectContractRegistry(_subjectContracts, _subjectAcquisitions);
        var byCollection = collections.ToDictionary(static collection => collection.Id, StringComparer.Ordinal);
        foreach (BaseGeneratedSubjectRegistration registration in registry.All)
        {
            BaseExportedSubjectDefinition contract = registration.Definition;
            BaseSubjectValidationPlanDefinition plan = contract.ValidationPlan;
            if (!byCollection.TryGetValue(plan.PrivateCollectionId, out CollectionDefinition? source) ||
                !source.System || source.Exposed ||
                !string.Equals(source.SystemOwnerModuleId, contract.OwningModuleId, StringComparison.Ordinal))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
            var fields = (source.Fields ?? []).ToDictionary(static field => field.Id, StringComparer.Ordinal);
            if (plan.Active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField &&
                (!fields.TryGetValue(plan.Active.FieldId!, out FieldDefinition? active) || active.Type != "boolean" || !active.Required || active.Nullable))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
            if (plan.Active.Kind != BaseSubjectActiveBindingKind.RequiredBooleanField ||
                !fields.TryGetValue(contract.TombstoneFieldId, out FieldDefinition? tombstone) || tombstone.Type != "boolean" || !tombstone.Required || tombstone.Nullable ||
                string.Equals(plan.Active.FieldId, contract.TombstoneFieldId, StringComparison.Ordinal))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
            if (plan.Scope.Kind != BaseSubjectScopeBindingKind.Global &&
                (!fields.TryGetValue(plan.Scope.FieldId!, out FieldDefinition? scope) || scope.Type != "string" || !scope.Required || scope.Nullable))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
            if (contract.Scope switch
                {
                    BaseSubjectScopeKind.Global => plan.Scope.Kind != BaseSubjectScopeBindingKind.Global,
                    BaseSubjectScopeKind.Tenant => plan.Scope.Kind != BaseSubjectScopeBindingKind.RequiredTenantField,
                    BaseSubjectScopeKind.Project => plan.Scope.Kind != BaseSubjectScopeBindingKind.RequiredProjectField,
                    _ => true,
                })
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        }
        foreach (BaseSubjectAcquisitionDefinition acquisition in registry.Acquisitions)
        {
            try
            {
                BaseApplicationId.Validate(acquisition.Id, nameof(acquisition));
                BaseApplicationId.Validate(acquisition.ContractId, nameof(acquisition));
                BaseApplicationId.Validate(acquisition.RegisteredReadId, nameof(acquisition));
                BaseApplicationId.Validate(acquisition.RequiredGrantId, nameof(acquisition));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid, exception);
            }
            BaseGeneratedSubjectRegistration? contract = registry.Find(acquisition.ContractId, acquisition.ContractVersion);
            if (acquisition.Version < 1 || acquisition.ContractVersion < 1 ||
                acquisition.MaximumResults is < 1 or > 256 || !Enum.IsDefined(acquisition.Audience) ||
                contract is null || !_reads.TryGetValue(acquisition.RegisteredReadId, out IBaseReadRegistration? read)
                || read.SourceAuthority != BaseRegisteredReadSourceAuthority.System
                || read.Disclosure == BaseRegisteredReadDisclosure.Ordinary
                || read.Audience != acquisition.Audience
                || !string.Equals(read.RequiredGrantId, acquisition.RequiredGrantId, StringComparison.Ordinal)
                || !string.Equals(contract.Definition.AcquisitionGrantId, acquisition.RequiredGrantId, StringComparison.Ordinal)
                || !contract.Definition.Audiences.Contains(acquisition.Audience)
                || read.Plan.Budgets.MaxResultRows < acquisition.MaximumResults
                || read.Plan.Projection.Length != 1
                || read.Plan.Projection[0].Operand.Kind != BaseRelationalOperandKind.SubjectReference
                || read.Plan.Projection.Any(projection => projection.Operand.Kind == BaseRelationalOperandKind.SubjectReference
                    && (!string.Equals(projection.Operand.SubjectContractId, acquisition.ContractId, StringComparison.Ordinal)
                        || projection.Operand.SubjectContractVersion != acquisition.ContractVersion)))
                throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        }
        for (int collectionIndex = 0; collectionIndex < collections.Length; collectionIndex++)
        {
            CollectionDefinition collection = collections[collectionIndex];
            FieldDefinition[] fields = collection.Fields ?? [];
            var normalized = new FieldDefinition[fields.Length];
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                FieldDefinition field = fields[fieldIndex];
                if (field.SubjectReference is not { } reference) { normalized[fieldIndex] = field; continue; }
                BaseGeneratedSubjectRegistration? target = registry.Find(reference.ContractId, reference.ContractVersion);
                if (target is null || !string.IsNullOrEmpty(reference.ContractChecksum) &&
                    !string.Equals(reference.ContractChecksum, target.Checksum, StringComparison.Ordinal) ||
                    reference.Guarantee != BaseSubjectValidationGuarantee.TransactionSnapshot ||
                    reference.Requirement == BaseSubjectReferenceRequirement.Active &&
                    target.Definition.ValidationPlan.Active.Kind != BaseSubjectActiveBindingKind.RequiredBooleanField)
                    throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
                normalized[fieldIndex] = field with { SubjectReference = reference with { ContractChecksum = target.Checksum } };
            }
            collections[collectionIndex] = collection with { Fields = normalized };
            _collections[collection.Id] = collections[collectionIndex];
        }
        return registry;
    }

    private static bool Fits(BaseExportedSubjectDefinition definition, BaseSubjectReferenceCapability capability)
    {
        BaseSubjectValidationLimits limits = definition.ValidationPlan.Limits;
        return capability.TransactionSnapshotValidationSupported && definition.MaximumSubjectIdUtf8Bytes <= capability.MaximumSubjectIdUtf8Bytes &&
            limits.MaximumReferencesPerRecord <= capability.MaximumReferencesPerRecord &&
            limits.MaximumReferencesPerMutation <= capability.MaximumReferencesPerMutation &&
            limits.MaximumValidationPlansPerMutation <= capability.MaximumValidationPlansPerMutation &&
            limits.MaximumAuthorityReads <= capability.MaximumAuthorityReads && limits.MaximumReadIntervals <= capability.MaximumReadIntervals &&
            limits.MaximumSelectedBytes <= capability.MaximumSelectedBytes && limits.MaximumEvidenceBytes <= capability.MaximumEvidenceBytes &&
            limits.MaximumTransientBytes <= capability.MaximumTransientBytes && limits.ExecutionTimeout <= capability.MaximumExecutionTime;
    }
}

/// <summary>Represents hPDBase Installed Features.</summary>
public sealed record HPDBaseInstalledFeatures
{
    /// <summary>Gets or sets provider.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets or sets collection Ids.</summary>
    public required string[] CollectionIds { get; init; }
    internal CollectionDefinition[] CollectionDefinitions { get; init; } = [];
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
