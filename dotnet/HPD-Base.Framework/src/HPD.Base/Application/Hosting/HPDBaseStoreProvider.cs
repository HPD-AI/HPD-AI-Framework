using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Describes the immutable capabilities of one authoritative BASE store bundle.</summary>
[Flags]
public enum BaseStoreProviderCapabilities
{
    /// <summary>No capabilities are advertised.</summary>
    None = 0,
    /// <summary>Provides authoritative record storage.</summary>
    Records = 1,
    /// <summary>Provides atomic mutation execution.</summary>
    AtomicMutations = 2,
    /// <summary>Enforces required physical indexes.</summary>
    RequiredIndexes = 4,
    /// <summary>Executes relational reads.</summary>
    RelationalExecution = 8,
    /// <summary>Commits a transactional mutation journal.</summary>
    TransactionalJournal = 16,
    /// <summary>Provides retained historical reads.</summary>
    HistoricalReads = 32,
    /// <summary>Provides administrative operations.</summary>
    Administration = 64,
    /// <summary>Provides vector execution in the authoritative transaction boundary.</summary>
    CoLocatedVectors = 128,
    /// <summary>Provides policy-safe lexical execution in the authoritative transaction boundary.</summary>
    CoLocatedTextSearch = 256,
}

/// <summary>Supplies immutable identity and admission facts for a store provider.</summary>
public sealed class BaseStoreProviderDescriptor
{
    /// <summary>Gets or initializes the stable provider kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or initializes the provider protocol version.</summary>
    public int ProtocolVersion { get; init; } = HPDBaseStoreProviderFactory.ProtocolVersion;
    /// <summary>Gets or initializes the build-time capabilities.</summary>
    public BaseStoreProviderCapabilities Capabilities { get; init; }
    /// <summary>Gets or initializes the stable registration identifiers.</summary>
    public required string[] RegistrationIds { get; init; }
    /// <summary>Gets immutable storage-protection capabilities owned by this bundle.</summary>
    public BaseStorageProtectionCapability[] StorageProtectionCapabilities { get; init; } = [];
    /// <summary>Gets or initializes the maximum decoded binary field size supported by the provider.</summary>
    public int MaximumBinaryFieldBytes { get; init; } = 1_048_576;
    /// <summary>Gets the provider's certified exported-subject validation envelope.</summary>
    public required BaseSubjectReferenceCapability SubjectReferences { get; init; }
    /// <summary>Gets the provider's certified exported-subject lifecycle envelope.</summary>
    public required BaseSubjectLifecycleCapability SubjectLifecycle { get; init; }
    /// <summary>Gets the provider's certified coordinated-retirement envelope.</summary>
    public required BaseSubjectRetirementCapability SubjectRetirement { get; init; }
    /// <summary>Gets the provider's certified registered module-mutation envelope.</summary>
    public required BaseModuleMutationCapability ModuleMutations { get; init; }
    /// <summary>Gets the certified lexical-search envelope when co-located text search is advertised.</summary>
    public BaseTextProviderCapability? TextSearch { get; init; }
}

/// <summary>Represents one validated immutable authoritative store selection.</summary>
public sealed class HPDBaseStoreProvider
{
    private readonly string[] _registrationIds;
    private readonly BaseStorageProtectionCapability[] _storageProtectionCapabilities;
    internal HPDBaseStoreProvider(BaseStoreProviderDescriptor descriptor, IHPDBaseStoreInstaller installer)
    {
        Kind = descriptor.Kind;
        ProtocolVersion = descriptor.ProtocolVersion;
        Capabilities = descriptor.Capabilities;
        _registrationIds = descriptor.RegistrationIds.ToArray();
        _storageProtectionCapabilities = descriptor.StorageProtectionCapabilities.Select(BaseStorageProtectionContract.Clone).ToArray();
        MaximumBinaryFieldBytes = descriptor.MaximumBinaryFieldBytes;
        SubjectReferences = descriptor.SubjectReferences with { };
        SubjectLifecycle = descriptor.SubjectLifecycle with { };
        SubjectRetirement = descriptor.SubjectRetirement with { };
        ModuleMutations = descriptor.ModuleMutations with { MaximumLimits = descriptor.ModuleMutations.MaximumLimits with { Deadlines = descriptor.ModuleMutations.MaximumLimits.Deadlines with { } } };
        TextSearch = descriptor.TextSearch is null ? null : descriptor.TextSearch with { MaximumLimits = descriptor.TextSearch.MaximumLimits with { } };
        Installer = installer;
    }

    /// <summary>Gets the stable provider kind.</summary>
    public string Kind { get; }
    /// <summary>Gets the store-provider protocol version.</summary>
    public int ProtocolVersion { get; }
    /// <summary>Gets immutable build-time capability facts.</summary>
    public BaseStoreProviderCapabilities Capabilities { get; }
    internal IReadOnlyList<string> RegistrationIds => _registrationIds;
    internal IReadOnlyList<BaseStorageProtectionCapability> StorageProtectionCapabilities => _storageProtectionCapabilities;
    /// <summary>Gets the provider's certified maximum decoded binary-field size.</summary>
    public int MaximumBinaryFieldBytes { get; }
    /// <summary>Gets the provider's certified exported-subject validation envelope.</summary>
    public BaseSubjectReferenceCapability SubjectReferences { get; }
    /// <summary>Gets the provider's certified exported-subject lifecycle envelope.</summary>
    public BaseSubjectLifecycleCapability SubjectLifecycle { get; }
    /// <summary>Gets the provider's certified coordinated-retirement envelope.</summary>
    public BaseSubjectRetirementCapability SubjectRetirement { get; }
    /// <summary>Gets the provider's certified registered module-mutation envelope.</summary>
    public BaseModuleMutationCapability ModuleMutations { get; }
    /// <summary>Gets the certified lexical-search envelope.</summary>
    public BaseTextProviderCapability? TextSearch { get; }
    internal IHPDBaseStoreInstaller Installer { get; }
}

/// <summary>Creates validated immutable store-provider descriptors for provider packages.</summary>
public static class HPDBaseStoreProviderFactory
{
    /// <summary>Gets the supported provider protocol version.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Creates one immutable store provider from a descriptor and installer.</summary>
    public static HPDBaseStoreProvider Create(BaseStoreProviderDescriptor descriptor, IHPDBaseStoreInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(installer);
        if (descriptor.ProtocolVersion != ProtocolVersion || !ValidIdentifier(descriptor.Kind) || descriptor.MaximumBinaryFieldBytes is < 1 or > 1_048_576 ||
            !ValidSubjectCapability(descriptor.SubjectReferences)
            || !ValidLifecycleCapability(descriptor.SubjectLifecycle)
            || !ValidRetirementCapability(descriptor.SubjectRetirement)
            || !BaseModuleMutationCapabilityContract.IsValid(descriptor.ModuleMutations)
            || descriptor.Capabilities.HasFlag(BaseStoreProviderCapabilities.CoLocatedTextSearch) != (descriptor.TextSearch is not null)
            || descriptor.TextSearch is not null && !ValidTextCapability(descriptor.TextSearch))
            throw new InvalidOperationException("base.store.providerInvalid");
        const BaseStoreProviderCapabilities known = BaseStoreProviderCapabilities.Records | BaseStoreProviderCapabilities.AtomicMutations |
            BaseStoreProviderCapabilities.RequiredIndexes | BaseStoreProviderCapabilities.RelationalExecution |
            BaseStoreProviderCapabilities.TransactionalJournal | BaseStoreProviderCapabilities.HistoricalReads |
            BaseStoreProviderCapabilities.Administration | BaseStoreProviderCapabilities.CoLocatedVectors |
            BaseStoreProviderCapabilities.CoLocatedTextSearch;
        if ((descriptor.Capabilities & ~known) != 0 ||
            (descriptor.Capabilities & (BaseStoreProviderCapabilities.Records | BaseStoreProviderCapabilities.AtomicMutations)) !=
            (BaseStoreProviderCapabilities.Records | BaseStoreProviderCapabilities.AtomicMutations) ||
            descriptor.Capabilities.HasFlag(BaseStoreProviderCapabilities.HistoricalReads) && !descriptor.Capabilities.HasFlag(BaseStoreProviderCapabilities.TransactionalJournal))
            throw new InvalidOperationException("base.store.providerInvalid");
        string[] ids = descriptor.RegistrationIds?.Select(static id => new string(id.AsSpan())).ToArray() ?? [];
        if (ids.Length == 0 || ids.Length > 32 || ids.Any(static id => !ValidIdentifier(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException("base.store.providerInvalid");
        BaseStorageProtectionCapability[] protection = descriptor.StorageProtectionCapabilities?.Select(BaseStorageProtectionContract.Clone).ToArray() ?? [];
        foreach (BaseStorageProtectionCapability capability in protection) BaseStorageProtectionContract.ValidateCapability(capability);
        if (protection.Select(static item => item.OwningModuleId).Distinct(StringComparer.Ordinal).Count() != protection.Length)
            throw new InvalidOperationException(BaseConfidentialityErrorCodes.StorageDescriptorInvalid);
        return new HPDBaseStoreProvider(new BaseStoreProviderDescriptor
        {
            Kind = new string(descriptor.Kind.AsSpan()), ProtocolVersion = descriptor.ProtocolVersion,
            Capabilities = descriptor.Capabilities, RegistrationIds = ids, StorageProtectionCapabilities = protection,
            MaximumBinaryFieldBytes = descriptor.MaximumBinaryFieldBytes,
            SubjectReferences = descriptor.SubjectReferences with { },
            SubjectLifecycle = descriptor.SubjectLifecycle with { },
            SubjectRetirement = descriptor.SubjectRetirement with { },
            ModuleMutations = descriptor.ModuleMutations with
            {
                MaximumLimits = descriptor.ModuleMutations.MaximumLimits with
                { Deadlines = descriptor.ModuleMutations.MaximumLimits.Deadlines with { } },
            },
            TextSearch = descriptor.TextSearch is null ? null : descriptor.TextSearch with { MaximumLimits = descriptor.TextSearch.MaximumLimits with { } },
        }, installer);
    }

    private static bool ValidTextCapability(BaseTextProviderCapability value) => value.TransactionalMaintenanceSupported && value.ExactRevisionHydrationSupported
        && value.PhraseSupported && value.PrefixSupported && value.MaximumLimits is not null
        && value.MaximumLimits.MaximumResults >= 1 && value.MaximumLimits.MaximumResults <= BaseTextPlatform.DefaultLimits.MaximumResults
        && value.MaximumLimits.MaximumQueryNodes >= 1 && value.MaximumLimits.MaximumQueryNodes <= BaseTextPlatform.DefaultLimits.MaximumQueryNodes
        && value.MaximumLimits.MaximumTransientBytes >= 1 && value.MaximumLimits.MaximumTransientBytes <= BaseTextPlatform.DefaultLimits.MaximumTransientBytes;

    private static bool ValidSubjectCapability(BaseSubjectReferenceCapability? value) => value is not null &&
        value.MaximumReferencesPerRecord is >= 1 and <= 32 && value.MaximumReferencesPerMutation is >= 1 and <= 1_024 &&
        value.MaximumSubjectIdUtf8Bytes is >= 1 and <= 256 && value.MaximumValidationPlansPerMutation is >= 1 and <= 64 &&
        value.MaximumAuthorityReads is >= 1 and <= 1_024 && value.MaximumReadIntervals is >= 1 and <= 1_024 &&
        value.MaximumSelectedBytes is >= 1_024 and <= 8_388_608 && value.MaximumEvidenceBytes is >= 1_024 and <= 8_388_608 &&
        value.MaximumTransientBytes is >= 65_536 and <= 67_108_864 && value.MaximumExecutionTime >= TimeSpan.FromMilliseconds(100) && value.MaximumExecutionTime <= TimeSpan.FromMinutes(2);

    private static bool ValidLifecycleCapability(BaseSubjectLifecycleCapability? value) => value is not null &&
        value.TransactionalPublicationSupported && value.IndependentCursorSupported &&
        value.MaximumConsumersPerContract is >= 1 and <= 32 && value.MaximumFactsPerPage is >= 1 and <= 256 &&
        value.MaximumResultBytes is >= 1_024 and <= 1_048_576 && value.MaximumRetainedFacts >= 1 &&
        value.MaximumReadTimeout >= TimeSpan.FromMilliseconds(100) && value.MaximumReadTimeout <= TimeSpan.FromMinutes(2);

    private static bool ValidRetirementCapability(BaseSubjectRetirementCapability? value) => value is not null
        && value.TransactionalBarrierSupported && value.TransactionalFinalPurgeSupported
        && value.MaximumRequiredConsumersPerContract is >= 1 and <= 32
        && value.MaximumAcknowledgementsPerCommit is >= 1 and <= 256
        && value.MaximumPendingBarriers >= 1
        && value.MaximumCoordinationWindow >= TimeSpan.FromMinutes(1) && value.MaximumCoordinationWindow <= TimeSpan.FromDays(30)
        && value.MaximumAdministrationPageSize is >= 1 and <= 256
        && value.MaximumResultBytes is >= 1 and <= 1_048_576
        && value.MaximumRetirementProjectionsPerCommit is >= 1 and <= 256
        && value.MaximumBarrierReadsPerCommit is >= 1 and <= 256
        && value.MaximumAcknowledgementReadsPerCommit is >= 1 and <= 256
        && value.MaximumPublicationsPerCommit is >= 1 and <= 256
        && value.MaximumEvidenceBytes is >= 1 and <= 1_048_576
        && value.MaximumPublicationBytes is >= 1 and <= 1_048_576
        && value.MaximumTransientBytes is >= 1 and <= 32_000_000
        && value.MaximumAcquisitionTimeout >= TimeSpan.FromMilliseconds(1) && value.MaximumAcquisitionTimeout <= TimeSpan.FromSeconds(5)
        && value.MaximumTransactionTimeout >= TimeSpan.FromMilliseconds(1) && value.MaximumTransactionTimeout <= TimeSpan.FromSeconds(30)
        && value.MaximumCommitCompletionTimeout >= TimeSpan.FromMilliseconds(1) && value.MaximumCommitCompletionTimeout <= TimeSpan.FromSeconds(30)
        && value.MaximumReceiptResolutionTimeout >= TimeSpan.FromMilliseconds(1) && value.MaximumReceiptResolutionTimeout <= TimeSpan.FromSeconds(30);

    internal static bool ValidIdentifier(string? value) => value is { Length: >= 1 and <= 128 } && value.All(static character => character is >= '!' and <= '~');
}

/// <summary>Installs one provider-authored store bundle under BASE validation.</summary>
public interface IHPDBaseStoreInstaller
{
    /// <summary>Configures the provider services and returns their frozen registration receipt.</summary>
    HPDBaseStoreRegistrationReceipt Configure(HPDBaseStoreInstallationContext context);
    /// <summary>Initializes the configured store bundle.</summary>
    ValueTask InitializeAsync(HPDBaseStoreInitializationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Provides the bounded single-use store-installation environment.</summary>
public sealed class HPDBaseStoreInstallationContext
{
    private bool _completed;
    private bool _issued;
    private readonly IServiceCollection _services;
    private readonly HPDBaseStoreProvider _provider;
    private readonly CollectionDefinition[] _collections;
    private readonly BaseExportedSubjectDefinition[] _subjects;
    private readonly BaseRegisteredModuleMutationDefinition[] _moduleMutations;
    private readonly BaseModuleGenerationCellDefinition[] _moduleGenerationCells;
    private readonly BaseSubjectLifecycleConsumerDefinition[] _lifecycleConsumers;
    private readonly BaseSubjectLifecycleInspectionAuthority[] _lifecycleInspectionAuthorities;
    private readonly BaseSubjectRetirementConsumerDefinition[] _retirementConsumers;
    private readonly BaseSubjectRetirementPolicy[] _retirementPolicies;
    private readonly string _schemaDigest;
    internal HPDBaseStoreInstallationContext(
        IServiceCollection services,
        HPDBaseStoreProvider provider,
        CollectionDefinition[] collections,
        BaseExportedSubjectDefinition[]? subjects = null,
        BaseRegisteredModuleMutationDefinition[]? moduleMutations = null,
        BaseModuleGenerationCellDefinition[]? moduleGenerationCells = null,
        BaseSubjectLifecycleConsumerDefinition[]? lifecycleConsumers = null,
        BaseSubjectLifecycleInspectionAuthority[]? lifecycleInspectionAuthorities = null,
        BaseSubjectRetirementConsumerDefinition[]? retirementConsumers = null,
        BaseSubjectRetirementPolicy[]? retirementPolicies = null)
    {
        _services = services;
        _provider = provider;
        _collections = collections.Select(CloneCollection).ToArray();
        _subjects = (subjects ?? []).Select(CloneSubject).ToArray();
        _moduleMutations = (moduleMutations ?? []).Select(static value => BaseModuleMutationContract.Seal(value)).ToArray();
        _moduleGenerationCells = (moduleGenerationCells ?? []).Select(static value => value with { }).ToArray();
        _lifecycleConsumers = (lifecycleConsumers ?? []).Select(static value => BaseSubjectLifecycleRegistry.Normalize(value)).ToArray();
        _lifecycleInspectionAuthorities = (lifecycleInspectionAuthorities ?? []).Select(static value => value with { }).ToArray();
        _retirementConsumers = (retirementConsumers ?? []).Select(static value => BaseSubjectRetirementRegistry.Normalize(value)).ToArray();
        _retirementPolicies = (retirementPolicies ?? []).Select(static value => BaseSubjectRetirementRegistry.NormalizePolicy(value)).ToArray();
        _schemaDigest = ComputeSchemaDigest(_collections, _subjects, _moduleMutations, _moduleGenerationCells, _lifecycleConsumers, _lifecycleInspectionAuthorities, _retirementConsumers, _retirementPolicies);
    }
    /// <summary>Gets the host service collection during the installation call.</summary>
    public IServiceCollection Services { get { ThrowIfCompleted(); return _services; } }
    /// <summary>Gets the selected immutable provider descriptor.</summary>
    public HPDBaseStoreProvider Provider { get { ThrowIfCompleted(); return _provider; } }
    /// <summary>Gets an owned view of the accepted collections.</summary>
    public IReadOnlyList<CollectionDefinition> Collections { get { ThrowIfCompleted(); return Array.AsReadOnly(_collections.Select(CloneCollection).ToArray()); } }
    /// <summary>Gets an owned view of the accepted exported logical subject definitions.</summary>
    public IReadOnlyList<BaseExportedSubjectDefinition> ExportedSubjects { get { ThrowIfCompleted(); return Array.AsReadOnly(_subjects.Select(CloneSubject).ToArray()); } }
    /// <summary>Gets an owned view of the accepted registered module mutations.</summary>
    public IReadOnlyList<BaseRegisteredModuleMutationDefinition> ModuleMutations { get { ThrowIfCompleted(); return Array.AsReadOnly(_moduleMutations.Select(static value => BaseModuleMutationContract.Seal(value)).ToArray()); } }
    /// <summary>Gets an owned view of the accepted module generation cells.</summary>
    public IReadOnlyList<BaseModuleGenerationCellDefinition> ModuleGenerationCells { get { ThrowIfCompleted(); return Array.AsReadOnly(_moduleGenerationCells.Select(static value => value with { }).ToArray()); } }
    /// <summary>Gets an owned view of installed exported-subject lifecycle consumers.</summary>
    public IReadOnlyList<BaseSubjectLifecycleConsumerDefinition> SubjectLifecycleConsumers { get { ThrowIfCompleted(); return Array.AsReadOnly(_lifecycleConsumers.Select(static value => BaseSubjectLifecycleRegistry.Normalize(value)).ToArray()); } }
    /// <summary>Gets immutable all-scope lifecycle inspection authority receipts.</summary>
    public IReadOnlyList<BaseSubjectLifecycleInspectionAuthority> SubjectLifecycleInspectionAuthorities { get { ThrowIfCompleted(); return Array.AsReadOnly(_lifecycleInspectionAuthorities.Select(static value => value with { }).ToArray()); } }
    /// <summary>Gets installed consumer-owned retirement profiles.</summary>
    public IReadOnlyList<BaseSubjectRetirementConsumerDefinition> SubjectRetirementConsumers { get { ThrowIfCompleted(); return Array.AsReadOnly(_retirementConsumers.Select(static value => BaseSubjectRetirementRegistry.Normalize(value)).ToArray()); } }
    /// <summary>Gets installed exporter-owned retirement policies.</summary>
    public IReadOnlyList<BaseSubjectRetirementPolicy> SubjectRetirementPolicies { get { ThrowIfCompleted(); return Array.AsReadOnly(_retirementPolicies.Select(static value => BaseSubjectRetirementRegistry.NormalizePolicy(value)).ToArray()); } }
    /// <summary>Creates the single frozen receipt for this installation.</summary>
    public HPDBaseStoreRegistrationReceipt CreateReceipt(string recordStoreRegistrationId)
    {
        ThrowIfCompleted();
        if (_issued || !HPDBaseStoreProviderFactory.ValidIdentifier(recordStoreRegistrationId)) throw new InvalidOperationException("base.store.providerInvalid");
        _issued = true;
        string[] roles = RequiredRoles(Provider.Capabilities, _collections);
        var receipt = new HPDBaseStoreRegistrationReceipt(
            Provider.Kind,
            Provider.ProtocolVersion,
            new string(recordStoreRegistrationId.AsSpan()),
            roles,
            Provider.RegistrationIds.ToArray(),
            _schemaDigest,
            Guid.NewGuid());
        _services.AddSingleton(new HPDBaseStoreInstallationMarker(receipt.Identity));
        return receipt;
    }
    internal void Complete() => _completed = true;
    private void ThrowIfCompleted() { if (_completed) throw new ObjectDisposedException(nameof(HPDBaseStoreInstallationContext)); }

    private static string[] RequiredRoles(BaseStoreProviderCapabilities capabilities, CollectionDefinition[] collections)
    {
        var roles = new List<string> { "records", "mutation", "atomic" };
        if (capabilities.HasFlag(BaseStoreProviderCapabilities.RequiredIndexes)) roles.Add("schema");
        if (capabilities.HasFlag(BaseStoreProviderCapabilities.RelationalExecution)) roles.Add("relational");
        if (capabilities.HasFlag(BaseStoreProviderCapabilities.TransactionalJournal)) roles.Add("journal");
        if (capabilities.HasFlag(BaseStoreProviderCapabilities.HistoricalReads)) roles.Add("history");
        if (capabilities.HasFlag(BaseStoreProviderCapabilities.Administration)) roles.Add("administration");
        if (collections.SelectMany(static collection => collection.VectorIndexes ?? []).Any())
        {
            if (!capabilities.HasFlag(BaseStoreProviderCapabilities.CoLocatedVectors))
                throw new InvalidOperationException("base.store.providerInvalid");
            roles.Add("vector.provider");
            roles.Add("vector.authority");
        }
        return roles.ToArray();
    }

    internal static string ComputeSchemaDigest(
        IEnumerable<CollectionDefinition> collections,
        IEnumerable<BaseExportedSubjectDefinition>? subjects = null,
        IEnumerable<BaseRegisteredModuleMutationDefinition>? moduleMutations = null,
        IEnumerable<BaseModuleGenerationCellDefinition>? moduleGenerationCells = null,
        IEnumerable<BaseSubjectLifecycleConsumerDefinition>? lifecycleConsumers = null,
        IEnumerable<BaseSubjectLifecycleInspectionAuthority>? lifecycleInspectionAuthorities = null,
        IEnumerable<BaseSubjectRetirementConsumerDefinition>? retirementConsumers = null,
        IEnumerable<BaseSubjectRetirementPolicy>? retirementPolicies = null)
    {
        var canonical = new StringBuilder();
        foreach (CollectionDefinition collection in collections.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            canonical.Append(collection.Id).Append('\n');
            foreach (FieldDefinition field in (collection.Fields ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal))
                canonical.Append("f:").Append(field.Id).Append(':').Append(field.Type).Append('\n');
            foreach (IndexDefinition index in (collection.Indexes ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal))
                canonical.Append("i:").Append(index.Id).Append('\n');
            foreach (VectorIndexDefinition index in (collection.VectorIndexes ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal))
                canonical.Append("v:").Append(index.Id).Append(':').Append(index.Dimensions).Append(':').Append((int)index.Function).Append('\n');
            foreach (BaseTextIndexDefinition index in (collection.TextIndexes ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
                canonical.Append("t:").Append(index.Id).Append(':').Append(index.Version).Append(':')
                    .Append(Convert.ToHexStringLower(BaseTextIndexContract.Seal(index).DefinitionChecksum.AsSpan())).Append('\n');
        }
        foreach (BaseExportedSubjectDefinition subject in (subjects ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
            canonical.Append("s:").Append(subject.Id).Append(':').Append(subject.Version).Append(':')
                .Append(BaseSubjectContractGraph.Checksum(subject)).Append('\n');
        foreach (BaseRegisteredModuleMutationDefinition operation in (moduleMutations ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
            canonical.Append("m:").Append(operation.Id).Append(':').Append(operation.Version).Append(':')
                .Append(Convert.ToHexStringLower(operation.Checksum.ToArray())).Append('\n');
        foreach (BaseModuleGenerationCellDefinition cell in (moduleGenerationCells ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
            canonical.Append("g:").Append(cell.Id).Append(':').Append(cell.Version).Append(':').Append((int)cell.Scope)
                .Append(':').Append(cell.OwningModuleId).Append(':').Append(cell.MaximumKeyUtf8Bytes).Append(':').Append(cell.MaximumCellsPerOperation).Append('\n');
        foreach (BaseSubjectLifecycleConsumerDefinition consumer in (lifecycleConsumers ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version))
            canonical.Append("l:").Append(consumer.Id).Append(':').Append(consumer.Version).Append(':')
                .Append(BaseSubjectLifecycleRegistry.Checksum(
                    BaseSubjectLifecycleRegistry.Normalize(consumer),
                    BaseSubjectContractGraph.Checksum((subjects ?? []).Single(subject => subject.Id == consumer.ContractId && subject.Version == consumer.ContractVersion)))).Append('\n');
        foreach (BaseSubjectLifecycleInspectionAuthority authority in (lifecycleInspectionAuthorities ?? []).OrderBy(static value => value.ContractId, StringComparer.Ordinal).ThenBy(static value => value.ContractVersion))
            canonical.Append("la:").Append(authority.ContractId).Append(':').Append(authority.ContractVersion).Append(':')
                .Append(authority.OwningModuleId).Append(':').Append(authority.GrantId).Append(':').Append(authority.Digest).Append('\n');
        foreach (BaseSubjectRetirementConsumerDefinition consumer in (retirementConsumers ?? []).OrderBy(static value => value.ConsumerId, StringComparer.Ordinal).ThenBy(static value => value.ConsumerVersion))
            canonical.Append("rc:").Append(consumer.ConsumerId).Append(':').Append(consumer.ConsumerVersion).Append(':')
                .Append(BaseSubjectRetirementRegistry.ConsumerChecksum(consumer)).Append('\n');
        foreach (BaseSubjectRetirementPolicy policy in (retirementPolicies ?? []).OrderBy(static value => value.ContractId, StringComparer.Ordinal).ThenBy(static value => value.ContractVersion))
            canonical.Append("rp:").Append(policy.ContractId).Append(':').Append(policy.ContractVersion).Append(':').Append(policy.PolicyChecksum).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static BaseExportedSubjectDefinition CloneSubject(BaseExportedSubjectDefinition value) => value with
    {
        TombstoneFieldId = new string(value.TombstoneFieldId.AsSpan()),
        Audiences = value.Audiences.ToArray(),
        ValidationPlan = value.ValidationPlan with
        {
            Active = value.ValidationPlan.Active with { },
            Scope = value.ValidationPlan.Scope with { },
            Limits = value.ValidationPlan.Limits with { },
        },
    };

    private static CollectionDefinition CloneCollection(CollectionDefinition value) => value with
    {
        Fields = value.Fields?.Select(static field => field with
        {
            RequiredCapabilities = field.RequiredCapabilities?.ToArray(),
            Extensions = CloneExtensions(field.Extensions),
        }).ToArray(),
        Indexes = value.Indexes?.Select(static index => index with
        {
            Parts = index.Parts?.Select(static part => part with { Extensions = CloneExtensions(part.Extensions) }).ToArray(),
            Extensions = CloneExtensions(index.Extensions),
        }).ToArray(),
        VectorIndexes = value.VectorIndexes?.Select(static index => index with { FilterFieldIds = index.FilterFieldIds.ToArray() }).ToArray(),
        TextIndexes = value.TextIndexes?.Select(BaseTextIndexContract.Seal).ToArray(),
        PolicyRefs = value.PolicyRefs?.ToArray(),
        RequiredCapabilities = value.RequiredCapabilities?.ToArray(),
        Diagnostics = value.Diagnostics?.ToArray(),
        Extensions = CloneExtensions(value.Extensions),
    };

    private static Dictionary<string, System.Text.Json.JsonElement>? CloneExtensions(Dictionary<string, System.Text.Json.JsonElement>? values) =>
        values?.ToDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value.Clone(), StringComparer.Ordinal);
}

internal sealed record HPDBaseStoreInstallationMarker(Guid Identity);

/// <summary>Provides the bounded single-use initialized store environment.</summary>
public sealed class HPDBaseStoreInitializationContext
{
    private bool _completed;
    private readonly IServiceProvider _services;
    private readonly HPDBaseStoreProvider _provider;
    private readonly HPDBaseStoreRegistrationReceipt _receipt;
    internal HPDBaseStoreInitializationContext(IServiceProvider services, HPDBaseStoreProvider provider, HPDBaseStoreRegistrationReceipt receipt)
    { _services = services; _provider = provider; _receipt = receipt; }
    /// <summary>Gets the initialized host service provider.</summary>
    public IServiceProvider Services { get { ThrowIfCompleted(); return _services; } }
    /// <summary>Gets the selected immutable provider descriptor.</summary>
    public HPDBaseStoreProvider Provider { get { ThrowIfCompleted(); return _provider; } }
    /// <summary>Gets the frozen installation receipt.</summary>
    public HPDBaseStoreRegistrationReceipt Receipt { get { ThrowIfCompleted(); return _receipt; } }
    internal void Complete() => _completed = true;
    private void ThrowIfCompleted() { if (_completed) throw new ObjectDisposedException(nameof(HPDBaseStoreInitializationContext)); }
}

/// <summary>Records the immutable identity of one configured store installation.</summary>
public sealed class HPDBaseStoreRegistrationReceipt
{
    private readonly ReadOnlyCollection<string> _requiredRoles;
    private readonly ReadOnlyCollection<string> _contributorIds;
    internal HPDBaseStoreRegistrationReceipt(string kind, int protocolVersion, string recordStoreRegistrationId, string[] requiredRoles, string[] contributorIds, string schemaDigest, Guid identity)
    {
        Kind = kind;
        ProtocolVersion = protocolVersion;
        RecordStoreRegistrationId = recordStoreRegistrationId;
        _requiredRoles = Array.AsReadOnly(requiredRoles.Select(static value => new string(value.AsSpan())).ToArray());
        _contributorIds = Array.AsReadOnly(contributorIds.Select(static value => new string(value.AsSpan())).ToArray());
        SchemaDigest = schemaDigest;
        Identity = identity;
    }
    /// <summary>Gets the selected provider kind.</summary>
    public string Kind { get; }
    /// <summary>Gets the provider protocol version.</summary>
    public int ProtocolVersion { get; }
    /// <summary>Gets the stable authoritative record-store registration identifier.</summary>
    public string RecordStoreRegistrationId { get; }
    /// <summary>Gets the frozen authoritative roles required from the selected bundle.</summary>
    public IReadOnlyList<string> RequiredRoles => _requiredRoles;
    /// <summary>Gets the frozen descriptor, health, diagnostic, and projection contributor identifiers.</summary>
    public IReadOnlyList<string> ContributorIds => _contributorIds;
    /// <summary>Gets the ordinal digest of the accepted schema feature set.</summary>
    public string SchemaDigest { get; }
    internal Guid Identity { get; }
}
