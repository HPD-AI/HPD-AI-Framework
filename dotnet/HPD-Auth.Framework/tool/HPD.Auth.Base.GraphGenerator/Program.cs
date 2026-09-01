using System.Collections.Immutable;
using HPD.Auth.Base;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;

bool verify = args is ["--verify", _];
if (args.Length != 1 && !verify)
    throw new ArgumentException("Specify an output path, or --verify followed by the committed artifact path.");
string artifactPath = Path.GetFullPath(args[verify ? 1 : 0]);

BaseStorageProtectionRequirement requirement = StorageRequirement();
var moduleOptions = new AuthBaseModuleOptions
{
    DataProtectionApplicationDiscriminatorDigest = BaseBinary.From(new byte[32]),
    StorageProtectionRequirement = requirement,
};
var services = new ServiceCollection();
services.AddSingleton(TimeProvider.System);
services.AddHPDBase(builder =>
{
    builder.ConfigureSchema(options =>
    {
        options.ApplicationId = "hpd.auth.identity.v1";
        options.PlanProtectionKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
    });
    builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
    {
        Id = 1,
        Key = Enumerable.Repeat((byte)0x41, 32).ToArray(),
        IssueNotBefore = DateTimeOffset.UnixEpoch,
    });
    builder.UseStore(SqliteStore.Configure(options =>
    {
        options.DataSource = ":memory:";
        options.StoreId = "auth-graph-generator";
    }));
    builder.Use(new GraphStorageProtectionCapability());
    builder.ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
    {
        HostMaxima = SelectionLimits(),
        MaximumReceiptIdentityBytes = 4_096,
        MaximumEvidenceTokenBytes = 4_096,
        MaximumRouteNameBytes = 128,
        MaximumRequestBodyBytes = 1_048_576,
    });
    AuthBaseModule.Install(builder, moduleOptions);
    builder.SetSemanticActivationRestoreSelection(new BaseSemanticActivationRestoreSelection
    {
        LogicalStoreId = "auth-graph-generator",
        EnabledRestoreMode = BaseActivationRestoreMode.InPlaceRecovery,
        SelectionGeneration = 1,
        Identity = BaseMutationRequestIdentity.Create(
            "hpd.auth.graph-generator", "restore-selection", "restore-selection-v1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                "hpd.auth.graph-generator.restore-selection.v1"u8))),
        Checksum = [],
    });
});

await using ServiceProvider provider = services.BuildServiceProvider();
BaseLogicalSchema schema = provider.GetRequiredService<BaseLogicalSchema>();
HPDBaseStudioAuthoritySnapshot authority = provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>();
if (verify)
    AuthBaseGraphArtifact.Verify(await File.ReadAllBytesAsync(artifactPath), schema, authority, moduleOptions);
else
    await File.WriteAllBytesAsync(artifactPath, AuthBaseGraphArtifact.Create(schema, authority, moduleOptions));

static BaseStorageProtectionRequirement StorageRequirement() => new()
{
    OwningModuleId = "hpd.auth",
    PermittedGuarantees = [BaseStorageEncryptionGuarantee.ProviderDeclared],
    Coverage = new BaseStorageProtectionCoverageRequirement
    {
        AuthoritativeRecords = [BaseStorageProtectionState.Protected],
        Journal = [BaseStorageProtectionState.Protected],
        Receipts = [BaseStorageProtectionState.Protected],
        ProviderState = [BaseStorageProtectionState.Protected],
        Indexes = [BaseStorageProtectionState.Protected],
        TemporaryFiles = [BaseStorageProtectionState.Protected],
        AuthoritativeBackups = [BaseStorageProtectionState.Protected],
        AdministrativeExports = [BaseStorageProtectionState.Protected],
        OrdinaryExports = [BaseStorageProtectionState.NotRetained],
        ExternalFilesAndBlobs = [BaseStorageProtectionState.NotApplicable],
    },
    PermittedKeyOwners = [BaseStorageKeyOwner.Provider],
    RequiredRotation = BaseStorageRotationSupport.Online,
    MinimumVerification = BaseStorageVerificationStatus.ConfigurationValidated,
};

static BaseSelectionOperationLimits SelectionLimits() => new()
{
    MaximumQueryNodes = 24, MaximumQueryDepth = 8, MaximumLiteralValues = 32,
    MaximumSelectedRecords = 200, MaximumSelectedBytes = 1_048_576,
    MaximumProducedMutations = 200, MaximumQueryExecutions = 1,
    MaximumReadIntervals = 64, MaximumWrittenBytes = 1_048_576,
    MaximumFactBytes = 8_388_608, MaximumJournalBytes = 8_388_608,
    MaximumReceiptBytes = 8_388_608, MaximumRelationChecks = 400,
    MaximumUniqueConstraintChecks = 400, MaximumPreviousStateRequirements = 8,
    MaximumTransientBytes = 16_777_216, MaximumResultBytes = 32_768,
    AcquisitionTimeout = TimeSpan.FromSeconds(2), ExecutionTimeout = TimeSpan.FromSeconds(5),
    CallerCommitObservationTimeout = TimeSpan.FromSeconds(2),
};

sealed class GraphStorageProtectionCapability : IHPDBaseBuilderExtension
{
    public string Id => "hpd.auth.graph-generator.storage-protection";
    public ImmutableArray<BaseStorageProtectionCapability> StorageProtectionCapabilities =>
    [new BaseStorageProtectionCapability
    {
        OwningModuleId = "hpd.auth",
        Guarantee = BaseStorageEncryptionGuarantee.ProviderDeclared,
        Coverage = new BaseStorageProtectionCoverage
        {
            AuthoritativeRecords = BaseStorageProtectionState.Protected,
            Journal = BaseStorageProtectionState.Protected,
            Receipts = BaseStorageProtectionState.Protected,
            ProviderState = BaseStorageProtectionState.Protected,
            Indexes = BaseStorageProtectionState.Protected,
            TemporaryFiles = BaseStorageProtectionState.Protected,
            AuthoritativeBackups = BaseStorageProtectionState.Protected,
            AdministrativeExports = BaseStorageProtectionState.Protected,
            OrdinaryExports = BaseStorageProtectionState.NotRetained,
            ExternalFilesAndBlobs = BaseStorageProtectionState.NotApplicable,
        },
        KeyOwner = BaseStorageKeyOwner.Provider,
        Rotation = BaseStorageRotationSupport.Online,
        Verification = BaseStorageVerificationStatus.ConfigurationValidated,
    }];
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
}
