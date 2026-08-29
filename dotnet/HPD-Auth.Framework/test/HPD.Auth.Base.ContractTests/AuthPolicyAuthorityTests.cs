using System.Collections.Immutable;
using HPD.Auth.Base;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Base.ContractTests;

public sealed class AuthPolicyAuthorityTests
{
    [Fact]
    public void CanonicalGraphArtifactMatchesCommittedAuthorityAndRejectsMutation()
    {
        using ServiceProvider provider = CreateProvider();
        AuthBaseModuleOptions options = ModuleOptions();
        byte[] artifact = AuthBaseGraphArtifact.Create(
            provider.GetRequiredService<BaseLogicalSchema>(),
            provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(),
            options);
        string? explicitOutput = Environment.GetEnvironmentVariable("HPD_AUTH_GRAPH_OUTPUT");
        if (!string.IsNullOrWhiteSpace(explicitOutput))
            File.WriteAllBytes(explicitOutput, artifact);
        Assert.Equal(
            "93f129cb94c1b15dd176ed707242bf7dcf7c212ef3bc5d0f32b446b36e7a21f8",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(artifact)));
        byte[] committed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "auth-base-graph-v2.json"));
        AuthBaseGraphArtifact.Verify(committed,
            provider.GetRequiredService<BaseLogicalSchema>(),
            provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(), options);

        byte[] hostile = artifact.ToArray();
        hostile[^2] ^= 1;
        Assert.Throws<InvalidOperationException>(() => AuthBaseGraphArtifact.Verify(hostile,
            provider.GetRequiredService<BaseLogicalSchema>(),
            provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(), options));
    }

    [Fact]
    public async Task CompleteAuthGraphFinalizesWithSourceBoundCanonicalSettingsRead()
    {
        string dataSource = Path.Combine(Path.GetTempPath(), $"hpd-auth-base-contract-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider provider = CreateProvider(dataSource);
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "auth-contract-proof" });
            Assert.True(planned.IsSuccess(), $"{planned.Error?.Code}: {planned.Error?.Message}");
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(new BaseSchemaApplyRequest
            {
                ProtectedArtifact = planned.Value!.ProtectedArtifact,
            });
            Assert.True(applied.IsSuccess(), $"{applied.Error?.Code}: {applied.Error?.Message}");
            OperationResult<BaseApplicationReadiness> result = await provider
                .GetRequiredService<IHPDBaseApplication>().InitializeAsync();
            Assert.True(result.IsSuccess(), $"{result.Error?.Code}: {result.Error?.Message} ({result.Error?.Category})");

        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(dataSource)!)
                         .Where(file => Path.GetFileName(file).StartsWith(Path.GetFileName(dataSource), StringComparison.Ordinal)))
                File.Delete(file);
        }
    }

    [Fact]
    public async Task AuthServiceReceivesTenantConstrainedAuthority()
    {
        using ServiceProvider provider = CreateProvider();
        IBasePolicyOrchestrator policy = provider.GetRequiredService<IBasePolicyOrchestrator>();
        Guid tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        OperationResult<BasePolicyEvaluation> result = await policy.EvaluateReadAsync(Request(
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Service,
                SubjectKind = AccessSubjectKind.ServicePrincipal,
                SubjectId = "hpd.auth",
                CurrentTenantId = tenant.ToString("D"),
            }));

        Assert.True(result.IsSuccess());
        Assert.Equal(FilterNodeKind.Compare, result.Value!.EffectiveRecordFilter!.Kind);
        Assert.Equal("tenantId", result.Value.EffectiveRecordFilter.Field);
        Assert.Equal(tenant.ToString("D"), result.Value.EffectiveRecordFilter.Value!.Id);
        Assert.Equal("tenantId", result.Value.EffectiveWriteCheck!.Field);
        Assert.Contains(result.Value.Authority!.AdmittedGrants,
            static grant => grant.GrantId == "auth.identity.read");
    }

    [Theory]
    [InlineData(PrincipalAuthenticationState.Authenticated, AccessSubjectKind.User, "user-1")]
    [InlineData(PrincipalAuthenticationState.Service, AccessSubjectKind.ServicePrincipal, "another-module")]
    [InlineData(PrincipalAuthenticationState.Admin, AccessSubjectKind.Admin, "admin-1")]
    public async Task NonOwningPrincipalsReceiveNoAuthAuthority(
        PrincipalAuthenticationState authenticationState,
        AccessSubjectKind subjectKind,
        string subjectId)
    {
        using ServiceProvider provider = CreateProvider();
        OperationResult<BasePolicyEvaluation> result = await provider.GetRequiredService<IBasePolicyOrchestrator>()
            .EvaluateReadAsync(Request(new PrincipalContext
            {
                AuthenticationState = authenticationState,
                SubjectKind = subjectKind,
                SubjectId = subjectId,
                CurrentTenantId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            }));

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("auth.policy.authorityDenied", result.Error?.Code);
    }

    private static ServiceProvider CreateProvider(string dataSource = ":memory:")
    {
        var services = new ServiceCollection();
        services.AddLogging();
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
                options.DataSource = dataSource;
                options.StoreId = "auth-contract-proof";
            }));
            builder.Use(new AuthStorageProtectionProofExtension());
            builder.ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
            {
                HostMaxima = Limits(),
                MaximumReceiptIdentityBytes = 4_096,
                MaximumEvidenceTokenBytes = 4_096,
                MaximumRouteNameBytes = 128,
                MaximumRequestBodyBytes = 1_048_576,
            });
            AuthBaseModule.Install(builder, ModuleOptions());
            builder.SetSemanticActivationRestoreSelection(new BaseSemanticActivationRestoreSelection
            {
                LogicalStoreId = "auth-contract-proof",
                EnabledRestoreMode = BaseActivationRestoreMode.InPlaceRecovery,
                SelectionGeneration = 1,
                Identity = BaseMutationRequestIdentity.Create(
                    "hpd.auth.contract-tests",
                    "semantic-restore-selection",
                    "semantic-restore-selection-v1",
                    BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(
                        "hpd.auth.contract-tests.semantic-restore-selection.v1"u8))),
                Checksum = [],
            });
        });
        return services.BuildServiceProvider();
    }

    private static BasePolicyRequest Request(PrincipalContext principal) => new()
    {
        Principal = principal,
        Operation = new OperationContext
        {
            ApplicationId = "hpd.auth.identity.v1",
            Audience = HPDBaseEndpointAudience.Application,
            Operation = BaseOperationKind.Query,
            CollectionId = "auth.users",
            TenantId = principal.CurrentTenantId,
            Now = DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
        },
        Collection = new CollectionDefinition
        {
            Id = "auth.users", Name = "users", Kind = "record",
            SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject, Fields = [],
        },
        ResourceKind = PolicyResourceKind.Collection,
    };

    private static BaseSelectionOperationLimits Limits() => new()
    {
        MaximumQueryNodes = 24,
        MaximumQueryDepth = 8,
        MaximumLiteralValues = 32,
        MaximumSelectedRecords = 200,
        MaximumSelectedBytes = 1_048_576,
        MaximumProducedMutations = 200,
        MaximumQueryExecutions = 1,
        MaximumReadIntervals = 64,
        MaximumWrittenBytes = 1_048_576,
        MaximumFactBytes = 8_388_608,
        MaximumJournalBytes = 8_388_608,
        MaximumReceiptBytes = 8_388_608,
        MaximumRelationChecks = 400,
        MaximumUniqueConstraintChecks = 400,
        MaximumPreviousStateRequirements = 8,
        MaximumTransientBytes = 16_777_216,
        MaximumResultBytes = 32_768,
        AcquisitionTimeout = TimeSpan.FromSeconds(2),
        ExecutionTimeout = TimeSpan.FromSeconds(5),
        CallerCommitObservationTimeout = TimeSpan.FromSeconds(2),
    };

    private static BaseStorageProtectionRequirement StorageRequirement() => new()
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

    private static AuthBaseModuleOptions ModuleOptions() => new()
    {
        DataProtectionApplicationDiscriminatorDigest = BaseBinary.From(new byte[32]),
        StorageProtectionRequirement = StorageRequirement(),
    };

    private sealed class AuthStorageProtectionProofExtension : IHPDBaseBuilderExtension
    {
        public string Id => "hpd.auth.contract-tests.storage-protection";

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

        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
        }
    }
}
