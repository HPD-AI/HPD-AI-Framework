using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Base.ContractTests;

public sealed class AuthPolicyAuthorityTests
{
    [Fact]
    public void Confidential_operation_dto_safe_display_never_contains_secret_values()
    {
        const string passwordSentinel = "PASSWORD-HASH-MUST-NOT-APPEAR";
        const string stampSentinel = "SECURITY-STAMP-MUST-NOT-APPEAR";
        var request = new AuthChangePasswordV1
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ExpectedRevision = new RevisionToken("revision"),
            PasswordHash = passwordSentinel,
            SecurityStamp = stampSentinel,
            ConcurrencyStamp = "concurrency",
            OperationTime = DateTimeOffset.UnixEpoch,
        };

        string display = request.ToString();

        Assert.DoesNotContain(passwordSentinel, display, StringComparison.Ordinal);
        Assert.DoesNotContain(stampSentinel, display, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySecretBearingAuthDtoHasAnExplicitSafeDisplayBoundary()
    {
        Type[] secretBearingTypes = typeof(AuthBaseModule).Assembly.GetTypes()
            .Where(static type => type.GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(static property => property
                    .GetCustomAttribute<BaseFieldConfidentialityAttribute>()?
                    .Confidentiality == BaseFieldConfidentiality.Secret))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(secretBearingTypes);
        foreach (Type type in secretBearingTypes)
        {
            MethodInfo? display = type.GetMethod(
                nameof(ToString),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            Assert.NotNull(display);
            Assert.Equal(typeof(string), display!.ReturnType);
            DebuggerDisplayAttribute? debugger = type.GetCustomAttribute<DebuggerDisplayAttribute>();
            Assert.NotNull(debugger);
            Assert.Equal("{ToString(),nq}", debugger!.Value);
        }
    }

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
            "29ad6ef7df31b5469116d13a6b490c1f28d7c73df8c9537f4cff302d636f23ee",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(artifact)));
        byte[] committed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "auth-base-graph-v2.json"));
        AuthBaseGraphArtifact.Verify(committed,
            provider.GetRequiredService<BaseLogicalSchema>(),
            provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(), options);
        using JsonDocument graph = JsonDocument.Parse(committed);
        JsonElement root = graph.RootElement;
        Assert.Equal(20, root.GetProperty("logicalSchema").GetProperty("collections").GetArrayLength());
        Assert.Equal(109, root.GetProperty("definitions").GetArrayLength());
        Assert.Equal(30, root.GetProperty("moduleMutations").GetArrayLength());
        Assert.Equal(16, root.GetProperty("selectionProfiles").GetArrayLength());
        Assert.Equal(2, root.GetProperty("semanticActivations").GetArrayLength());
        Assert.Equal(11, root.GetProperty("activations").GetArrayLength());
        Assert.Equal(5, root.GetProperty("schedules").GetArrayLength());
        Assert.DoesNotContain("cleanupJobs", System.Text.Encoding.UTF8.GetString(committed),
            StringComparison.Ordinal);
        Assert.DoesNotContain("legacy", System.Text.Encoding.UTF8.GetString(committed),
            StringComparison.OrdinalIgnoreCase);

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
    public async Task InMemoryAndSqliteFinalizeTheSameCompleteAuthGraph()
    {
        string dataSource = Path.Combine(Path.GetTempPath(),
            $"hpd-auth-base-parity-{Guid.NewGuid():N}.db");
        try
        {
            await using ServiceProvider sqlite = CreateProvider(dataSource);
            await using ServiceProvider memory = CreateProvider(useInMemory: true);

            IBaseSchemaManager schemas = sqlite.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(
                new BaseSchemaPlanRequest { StoreId = "auth-contract-proof" });
            Assert.True(planned.IsSuccess(), planned.Error?.Code);
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
                new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact });
            Assert.True(applied.IsSuccess(), applied.Error?.Code);
            Assert.True((await sqlite.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
            Assert.True((await memory.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());

            AuthBaseModuleOptions options = ModuleOptions();
            byte[] sqliteGraph = AuthBaseGraphArtifact.Create(
                sqlite.GetRequiredService<BaseLogicalSchema>(),
                sqlite.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(), options);
            byte[] memoryGraph = AuthBaseGraphArtifact.Create(
                memory.GetRequiredService<BaseLogicalSchema>(),
                memory.GetRequiredService<HPDBaseStudioAuthoritySnapshot>(), options);

            Assert.Equal(sqliteGraph, memoryGraph);
            Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "auth-base-graph-v2.json")), memoryGraph);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dataSource))
                File.Delete(dataSource);
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

    [Theory]
    [InlineData("hpd.auth.user-subject")]
    [InlineData("hpd.auth.role-subject")]
    public async Task RetirementInspectionAuthorityIsScopedToTheExactSubjectContract(
        string contractId)
    {
        using ServiceProvider provider = CreateProvider();
        PrincipalContext principal = new()
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = "hpd.auth.cleanup",
            CurrentTenantId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        };
        OperationResult<BasePolicyEvaluation> result = await provider
            .GetRequiredService<IBasePolicyOrchestrator>()
            .EvaluateReadAsync(new BasePolicyRequest
            {
                Principal = principal,
                Operation = new OperationContext
                {
                    ApplicationId = "hpd.auth.identity.v1",
                    Audience = HPDBaseEndpointAudience.Application,
                    Operation = BaseOperationKind.SubjectRetirementInspect,
                    CollectionId = contractId,
                    TenantId = principal.CurrentTenantId,
                    Mode = OperationMode.System,
                    Now = DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
                },
                Collection = new CollectionDefinition
                {
                    Id = "base.subjectRetirement.barrier.inspect",
                    Name = "base.subjectRetirement.barrier.inspect",
                    Kind = BaseCollectionKinds.Custom,
                    SchemaMode = SchemaMode.Strict,
                    UnknownFields = UnknownFieldPolicy.Reject,
                    System = true,
                    SystemOwnerModuleId = "hpd.auth",
                    Fields = [],
                },
                ResourceKind = PolicyResourceKind.SubjectLifecycle,
            });

        Assert.True(result.IsSuccess(), result.Error?.Code);
        Assert.Single(result.Value!.Authority!.AdmittedGrants,
            static candidate => candidate.GrantId == "base.subjectRetirement.barrier.inspect");
    }

    [Fact]
    public void AuthGraphKeepsPrivateStateInSystemCollectionsWithExactConfidentiality()
    {
        using ServiceProvider provider = CreateProvider();
        BaseLogicalSchema schema = provider.GetRequiredService<BaseLogicalSchema>();

        Assert.NotEmpty(schema.Collections);
        Assert.All(schema.Collections, collection =>
        {
            Assert.True(collection.System, collection.Id);
            Assert.Equal("hpd.auth", collection.SystemOwnerModuleId);
        });

        IReadOnlyDictionary<string, BaseLogicalField> fields = schema.Fields
            .ToDictionary(static field => field.Id, StringComparer.Ordinal);
        string[] secretFields =
        [
            "auth.dataProtectionKeys.canonicalXml",
            "auth.passkeys.attestationObject",
            "auth.passkeys.clientDataJson",
            "auth.passkeys.credentialId",
            "auth.passkeys.publicKey",
            "auth.recoveryCodes.codeDigest",
            "auth.refreshTokenDeliveries.protectedToken",
            "auth.ssoProviders.clientSecret",
            "auth.ssoProviders.signingCertificate",
            "auth.userIdentities.providerTokens",
            "auth.userTokens.value",
            "auth.users.authenticatorKey",
            "auth.users.passwordHash",
            "auth.users.securityStamp",
        ];
        Assert.Equal(
            secretFields.Order(StringComparer.Ordinal),
            schema.Fields
                .Where(static field => field.Confidentiality == BaseFieldConfidentiality.Secret)
                .Select(static field => field.Id)
                .Order(StringComparer.Ordinal));
        Assert.All(secretFields, fieldId =>
        {
            Assert.True(fields.TryGetValue(fieldId, out BaseLogicalField? field), fieldId);
            Assert.Equal(BaseFieldConfidentiality.Secret, field!.Confidentiality);
            Assert.Equal(BaseRecordDisclosure.Omit, field.Disclosure.RecordRead);
            Assert.Equal(BaseProjectionDisclosure.Omit, field.Disclosure.Event);
            Assert.Equal(BaseProjectionDisclosure.Omit, field.Disclosure.Diagnostic);
            Assert.Equal(BaseAuthoritativeBackupProtection.PreserveAuthoritativeValue,
                field.Disclosure.AuthoritativeBackup);
            Assert.Equal(BaseProjectionDisclosure.Omit,
                field.Disclosure.AdministrativeDataExport);
            Assert.Equal(BaseProjectionDisclosure.Omit,
                field.Disclosure.OrdinaryDataExport);
        });

        Assert.Equal(BaseFieldConfidentiality.Confidential,
            fields["auth.refreshTokens.tokenDigest"].Confidentiality);
        Assert.Equal(BaseFieldConfidentiality.Confidential,
            fields["auth.refreshTokens.securityStampDigest"].Confidentiality);
        Assert.Equal(BaseFieldConfidentiality.Confidential,
            fields["auth.cleanupWork.lastChildReceiptScope"].Confidentiality);
        Assert.Equal(BaseFieldConfidentiality.Confidential,
            fields["auth.maintenanceCursors.lastPageDigest"].Confidentiality);
        Assert.Equal(BaseFieldConfidentiality.Confidential,
            fields["auth.maintenanceRuns.activationId"].Confidentiality);
        Assert.DoesNotContain(schema.Fields,
            static field => field.Confidentiality == BaseFieldConfidentiality.Public);
        Assert.All(schema.Fields, field =>
        {
            Assert.Equal(BaseFieldDisclosurePolicies.For(field.Confidentiality),
                field.Disclosure);
        });
    }

    [Fact]
    public async Task PublicDescriptorProjectionCannotDiscoverAuthPrivateAuthority()
    {
        await using ServiceProvider provider = CreateProvider(useInMemory: true);
        OperationResult<BaseApplicationReadiness> ready = await provider
            .GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        Assert.True(ready.IsSuccess(), ready.Error?.Code);
        await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        OperationResult<SchemaMetadata> projected = await provider
            .GetRequiredService<IBaseSchemaProvider>()
            .GetSchemaAsync(
                new PrincipalContext
                {
                    AuthenticationState = PrincipalAuthenticationState.Anonymous,
                    SubjectKind = AccessSubjectKind.Anonymous,
                },
                new OperationContext
                {
                    ApplicationId = "hpd.auth.identity.v1",
                    Audience = HPDBaseEndpointAudience.Public,
                    Operation = BaseOperationKind.SchemaRead,
                    CollectionId = "base.schema",
                    Mode = OperationMode.User,
                    Now = DateTimeOffset.UnixEpoch,
                },
                VisibilityLevel.Public);

        Assert.True(projected.IsSuccess(), projected.Error?.Code);
        Assert.DoesNotContain(projected.Value!.Collections ?? [], static collection =>
            collection.Id.StartsWith("auth.", StringComparison.Ordinal));
        string json = JsonSerializer.Serialize(projected.Value);
        Assert.DoesNotContain("hpd.auth.semantic", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hpd.auth.cleanup", json, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", json, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateProvider(
        string dataSource = ":memory:",
        bool useInMemory = false)
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
            if (!useInMemory)
            {
                builder.UseStore(SqliteStore.Configure(options =>
                {
                    options.DataSource = dataSource;
                    options.StoreId = "auth-contract-proof";
                }));
            }
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
                LogicalStoreId = useInMemory ? "inmemory" : "auth-contract-proof",
                EnabledRestoreMode = useInMemory ? null : BaseActivationRestoreMode.InPlaceRecovery,
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
