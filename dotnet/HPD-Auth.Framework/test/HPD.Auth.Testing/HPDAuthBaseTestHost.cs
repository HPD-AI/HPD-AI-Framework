using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Auth.Base;
using HPD.Auth.Builder;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Auth.Testing;

/// <summary>Composes an isolated real HPD Base authority graph for Auth tests.</summary>
public static class HPDAuthBaseTestHost
{
    private const string TestStoreId = "hpd.auth.testing.sqlite";
    /// <summary>Adds one isolated SQLite Base store and the complete Auth graph.</summary>
    /// <param name="services">The test service collection.</param>
    /// <param name="applicationName">The unique normalized test application name.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHPDAuthBaseTestHost(
        this IServiceCollection services,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        string storeId = TestStoreId;
        string dataSource = Path.Combine(Path.GetTempPath(), $"hpd-auth-test-{Guid.NewGuid():N}.db");
        services.AddSingleton(_ => new TestDatabaseCleanup(dataSource));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IAuthRefreshTokenDigestKeyRing, TestRefreshKeyRing>();
        services.TryAddSingleton<IAuthRecoveryCodeDigestKeyRing, TestRecoveryKeyRing>();
        services.TryAddSingleton<IAuthTokenDeliveryProtector, TestDeliveryProtector>();
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options =>
            {
                options.ApplicationId = "hpd.auth.identity.v1";
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x42, 32).ToArray();
            });
            builder.ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1, Key = Enumerable.Repeat((byte)0x41, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            });
            builder.UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = storeId;
                options.DataSource = dataSource;
                options.AdministrationEnabled = true;
            }));
            builder.Use(new TestStorageProtectionExtension());
            builder.ConfigureSelectionMutations(new HPDBaseSelectionMutationOptions
            {
                HostMaxima = SelectionLimits(), MaximumReceiptIdentityBytes = 4_096,
                MaximumEvidenceTokenBytes = 4_096, MaximumRouteNameBytes = 128,
                MaximumRequestBodyBytes = 1_048_576,
            });
            AuthBaseModule.Install(builder, new AuthBaseModuleOptions
            {
                DataProtectionApplicationDiscriminatorDigest = BaseBinary.From(
                    SHA256.HashData(Encoding.UTF8.GetBytes(applicationName))),
                StorageProtectionRequirement = StorageRequirement(),
            });
            builder.SetSemanticActivationRestoreSelection(new BaseSemanticActivationRestoreSelection
            {
                LogicalStoreId = storeId,
                EnabledRestoreMode = BaseActivationRestoreMode.InPlaceRecovery,
                SelectionGeneration = 1,
                Identity = BaseMutationRequestIdentity.Create(
                    "hpd.auth.testing", "restore", storeId,
                    BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes(storeId)))),
                Checksum = [],
            });
        });
        return services;
    }

    /// <summary>Applies the test graph and initializes Base readiness.</summary>
    /// <param name="services">The built test service provider.</param>
    /// <param name="applicationName">The same application name used during registration.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    public static async Task InitializeHPDAuthBaseTestHostAsync(
        this IServiceProvider services,
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.GetRequiredService<TestDatabaseCleanup>();
        IBaseSchemaManager schemas = services.GetRequiredService<IBaseSchemaManager>();
        OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(
            new BaseSchemaPlanRequest { StoreId = TestStoreId }, cancellationToken).ConfigureAwait(false);
        if (!planned.IsSuccess())
            throw Failure("plan", planned.Error);
        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
            new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact }, cancellationToken)
            .ConfigureAwait(false);
        if (!applied.IsSuccess())
            throw Failure("apply", applied.Error);
        OperationResult<BaseApplicationReadiness> ready = await services.GetRequiredService<IHPDBaseApplication>()
            .InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!ready.IsSuccess())
            throw Failure("initialize", ready.Error);
    }

    private static InvalidOperationException Failure(string phase, BaseError? error) =>
        new($"HPD Auth Base test {phase} failed: {error?.Code ?? "unknown"}.");

    private static BaseSelectionOperationLimits SelectionLimits() => new()
    {
        MaximumQueryNodes = 24, MaximumQueryDepth = 8, MaximumLiteralValues = 32,
        MaximumSelectedRecords = 200, MaximumSelectedBytes = 1_048_576,
        MaximumProducedMutations = 200, MaximumQueryExecutions = 1,
        MaximumReadIntervals = 64, MaximumWrittenBytes = 1_048_576,
        MaximumFactBytes = 2_097_152, MaximumJournalBytes = 2_621_440,
        MaximumReceiptBytes = 2_621_440, MaximumRelationChecks = 400,
        MaximumUniqueConstraintChecks = 400, MaximumPreviousStateRequirements = 8,
        MaximumTransientBytes = 8_388_608, MaximumResultBytes = 32_768,
        AcquisitionTimeout = TimeSpan.FromSeconds(2), ExecutionTimeout = TimeSpan.FromSeconds(5),
        CallerCommitObservationTimeout = TimeSpan.FromSeconds(2),
    };

    private static BaseStorageProtectionRequirement StorageRequirement() => new()
    {
        OwningModuleId = "hpd.auth", PermittedGuarantees = [BaseStorageEncryptionGuarantee.ProviderDeclared],
        Coverage = new BaseStorageProtectionCoverageRequirement
        {
            AuthoritativeRecords = [BaseStorageProtectionState.Protected], Journal = [BaseStorageProtectionState.Protected],
            Receipts = [BaseStorageProtectionState.Protected], ProviderState = [BaseStorageProtectionState.Protected],
            Indexes = [BaseStorageProtectionState.Protected], TemporaryFiles = [BaseStorageProtectionState.Protected],
            AuthoritativeBackups = [BaseStorageProtectionState.Protected], AdministrativeExports = [BaseStorageProtectionState.Protected],
            OrdinaryExports = [BaseStorageProtectionState.NotRetained], ExternalFilesAndBlobs = [BaseStorageProtectionState.NotApplicable],
        },
        PermittedKeyOwners = [BaseStorageKeyOwner.Provider], RequiredRotation = BaseStorageRotationSupport.Online,
        MinimumVerification = BaseStorageVerificationStatus.ConfigurationValidated,
    };

    private sealed class TestRefreshKeyRing : IAuthRefreshTokenDigestKeyRing
    {
        private static readonly byte[] Key = Enumerable.Repeat((byte)0x73, 32).ToArray();
        public AuthRefreshDigestKeyRingCapability Capability { get; } = new()
        {
            ModuleId = "hpd.auth.testing.refresh-keys", ActiveIssuanceVersion = 1,
            ValidationVersions = [1], Ownership = AuthDigestKeyOwnership.Host, IsReady = true,
            LastVerifiedAt = DateTimeOffset.UnixEpoch,
        };
        public AuthAuthorityResult<AuthRefreshDigestKey> GetActiveIssuanceKey() => GetValidationKey(1);
        public AuthAuthorityResult<AuthRefreshDigestKey> GetValidationKey(int version) => version == 1
            ? AuthAuthorityResult<AuthRefreshDigestKey>.Available(new AuthRefreshDigestKey
            { Version = 1, KeyMaterial = AuthOwnedSecretBytes.From(Key) })
            : AuthAuthorityResult<AuthRefreshDigestKey>.Unavailable();
    }

    private sealed class TestRecoveryKeyRing : IAuthRecoveryCodeDigestKeyRing
    {
        private static readonly byte[] Key = Enumerable.Repeat((byte)0x37, 32).ToArray();
        public AuthRecoveryCodeDigestKeyRingCapability Capability { get; } = new()
        {
            ModuleId = "hpd.auth.testing.recovery-keys", ActiveIssuanceVersion = 1,
            ValidationVersions = [1], Ownership = AuthDigestKeyOwnership.Host, IsReady = true,
            LastVerifiedAt = DateTimeOffset.UnixEpoch,
        };
        public AuthAuthorityResult<AuthRecoveryCodeDigestKey> GetActiveIssuanceKey() => GetValidationKey(1);
        public AuthAuthorityResult<AuthRecoveryCodeDigestKey> GetValidationKey(int version) => version == 1
            ? AuthAuthorityResult<AuthRecoveryCodeDigestKey>.Available(new AuthRecoveryCodeDigestKey
            { Version = 1, KeyMaterial = AuthOwnedSecretBytes.From(Key) })
            : AuthAuthorityResult<AuthRecoveryCodeDigestKey>.Unavailable();
    }

    private sealed class TestDeliveryProtector : IAuthTokenDeliveryProtector
    {
        private static readonly byte[] Key = Enumerable.Repeat((byte)0x51, 32).ToArray();
        public AuthTokenDeliveryProtectorCapability Capability { get; } = new()
        {
            ModuleId = "hpd.auth.testing.delivery", ActiveVersion = 1, ValidationVersions = [1],
            Ownership = AuthDigestKeyOwnership.Host, AuthenticatedEncryption = true,
            SupportsRotation = true, IsReady = true, LastVerifiedAt = DateTimeOffset.UnixEpoch,
        };
        public AuthAuthorityResult<AuthProtectedTokenEnvelope> Protect(
            AuthOwnedSecretBytes plaintext, AuthOwnedEnvelopeBytes associatedData)
        {
            byte[] clear = new byte[plaintext.Length]; plaintext.CopyTo(clear);
            byte[] nonce = RandomNumberGenerator.GetBytes(12), ciphertext = new byte[clear.Length], tag = new byte[16];
            try
            {
                using var aes = new AesGcm(Key, 16);
                aes.Encrypt(nonce, clear, ciphertext, tag, associatedData.ToArray());
                return AuthAuthorityResult<AuthProtectedTokenEnvelope>.Available(new AuthProtectedTokenEnvelope
                { ProtectorVersion = 1, Ciphertext = AuthOwnedEnvelopeBytes.From([.. nonce, .. tag, .. ciphertext]) });
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        public AuthAuthorityResult<AuthOwnedSecretBytes> Unprotect(
            int protectorVersion, AuthOwnedEnvelopeBytes ciphertext, AuthOwnedEnvelopeBytes associatedData)
        {
            byte[] envelope = ciphertext.ToArray();
            if (protectorVersion != 1 || envelope.Length < 29)
                return AuthAuthorityResult<AuthOwnedSecretBytes>.Unavailable();
            byte[] clear = new byte[envelope.Length - 28];
            try
            {
                using var aes = new AesGcm(Key, 16);
                aes.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), clear, associatedData.ToArray());
                return AuthAuthorityResult<AuthOwnedSecretBytes>.Available(AuthOwnedSecretBytes.From(clear));
            }
            catch (CryptographicException) { return AuthAuthorityResult<AuthOwnedSecretBytes>.Unavailable(); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
    }

    private sealed class TestStorageProtectionExtension : IHPDBaseBuilderExtension
    {
        public string Id => "hpd.auth.testing.storage";
        public ImmutableArray<BaseStorageProtectionCapability> StorageProtectionCapabilities =>
        [new BaseStorageProtectionCapability
        {
            OwningModuleId = "hpd.auth", Guarantee = BaseStorageEncryptionGuarantee.ProviderDeclared,
            Coverage = new BaseStorageProtectionCoverage
            {
                AuthoritativeRecords = BaseStorageProtectionState.Protected, Journal = BaseStorageProtectionState.Protected,
                Receipts = BaseStorageProtectionState.Protected, ProviderState = BaseStorageProtectionState.Protected,
                Indexes = BaseStorageProtectionState.Protected, TemporaryFiles = BaseStorageProtectionState.Protected,
                AuthoritativeBackups = BaseStorageProtectionState.Protected, AdministrativeExports = BaseStorageProtectionState.Protected,
                OrdinaryExports = BaseStorageProtectionState.NotRetained, ExternalFilesAndBlobs = BaseStorageProtectionState.NotApplicable,
            },
            KeyOwner = BaseStorageKeyOwner.Provider, Rotation = BaseStorageRotationSupport.Online,
            Verification = BaseStorageVerificationStatus.ConfigurationValidated,
        }];
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
    }

    private sealed class TestDatabaseCleanup(string dataSource) : IDisposable
    {
        public void Dispose()
        {
            Delete(dataSource);
            Delete(dataSource + "-wal");
            Delete(dataSource + "-shm");
        }

        private static void Delete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Adds a Base test host while preserving Auth builder chaining.</summary>
    /// <param name="builder">The Auth builder under test.</param>
    /// <param name="applicationName">The unique normalized test application name.</param>
    /// <returns>The same Auth builder.</returns>
    public static IHPDAuthBuilder UseBaseTestHost(
        this IHPDAuthBuilder builder,
        string applicationName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHPDAuthBaseTestHost(applicationName);
        return builder;
    }

    /// <summary>Adds a uniquely isolated Base test host while preserving Auth builder chaining.</summary>
    /// <param name="builder">The Auth builder under test.</param>
    /// <returns>The same Auth builder.</returns>
    public static IHPDAuthBuilder UseBaseTestHost(this IHPDAuthBuilder builder) =>
        UseBaseTestHost(builder, $"hpd-auth-test-{Guid.NewGuid():N}");

    /// <summary>Initializes a Base test host when its application-name digest is not needed by the caller.</summary>
    /// <param name="services">The built test service provider.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    public static Task InitializeHPDAuthBaseTestHostAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        InitializeHPDAuthBaseTestHostAsync(services, "hpd-auth-test", cancellationToken);
}
