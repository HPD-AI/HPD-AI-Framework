using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using HPD.Base.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Sqlite_executes_complete_semantic_activation_provider_matrix()
    {
        BaseSemanticActivationCertificationReport report = await BaseSemanticActivationProviderCertification.RunAsync(
            new SqliteSemanticCertificationFactory(), TimeSpan.FromSeconds(10));
        report.Passed.Should().BeTrue(string.Join("; ", report.Cases
            .Where(static item => item.Status != OperationStatus.Ok)
            .Select(static item => $"{item.Id}:{item.ObservedStatus}:{item.ObservedErrorCode}")));
        BaseSemanticActivationCertificationReport frozen = BaseSemanticActivationBuiltInCertification.LoadFrozenExecutedReport(
            report.Subject, BaseSemanticActivationCapabilityContract.BuiltIn(durable: true, maintenanceSupported: true));
        string mismatches = string.Join(",", report.Cases.Zip(frozen.Cases)
            .Where(static pair => !pair.First.EvidenceChecksum.AsSpan().SequenceEqual(pair.Second.EvidenceChecksum.AsSpan()))
            .Select(static pair => pair.First.Id));
        BaseSemanticActivationCertificationContract.ValidateReport(report).Should().BeTrue(
            $"actual={Convert.ToHexStringLower(report.Checksum.AsSpan())};frozen={Convert.ToHexStringLower(frozen.Checksum.AsSpan())};mismatches={mismatches}");
        report.Should().BeEquivalentTo(frozen, options => options.WithStrictOrdering());
    }

    private sealed class SqliteSemanticCertificationFactory : IBaseSemanticActivationCertificationFixtureFactory
    {
        public BaseSemanticActivationCertificationSubject Subject { get; }
        public SqliteSemanticCertificationFactory()
        {
            using SqliteCertificationStore adapter = Create();
            Subject = SqliteStore.CreateSemanticCertificationSubject(
                adapter.SemanticProvider.SemanticActivationCapability, adapter.ModuleMutationCapability,
                adapter.ActivationProvider.Descriptor.Capability);
        }
        public ValueTask<IBaseSemanticActivationCertificationFixture> CreateAsync(string caseId, int ordinal,
            DateTimeOffset deadlineUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IBaseSemanticActivationCertificationFixture>(
                new BaseInMemorySemanticActivationCertificationFixtureFactory.Fixture(
                    Subject, Create(caseId), caseId, ordinal, deadlineUtc));
        }
        private static SqliteCertificationStore Create(string? caseId = null)
        {
            string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-cert-{Guid.NewGuid():N}.db");
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseSemanticActivationKeyDefinition definition = BaseSemanticActivationCertificationProcessor.InstalledDefinition(limits);
            BaseSemanticActivationKeyDefinition definitionV2 = BaseSemanticActivationCertificationProcessor.InstalledDefinitionV2(limits);
            BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationCertificationProcessor.InstalledMigration(limits);
            BaseSemanticActivationRemovalAuthority removal = BaseSemanticActivationCertificationProcessor.InstalledRemoval(limits);
            var faults = new SemanticCertificationAdministrationFaults();
            bool needsGraphTransition = caseId is "maintenance-migrate" or "maintenance-remove";
            ImmutableArray<byte> definitionSetChecksum = caseId == "maintenance-remove"
                ? removal.ResultingDefinitionSetChecksum
                : BaseSemanticActivationCertificationProcessor.InstalledDefinitionSetChecksum;
            var time = new BaseTestTimeProvider(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
            int nonceOrdinal = 0;
            var tokenProtector = new BaseOpaqueTokenProtector(
                Microsoft.Extensions.Options.Options.Create(new HPDBaseTokenProtectionOptions
                {
                    ActiveKey = new BaseOpaqueTokenKey
                    {
                        Id = 1, Key = Enumerable.Repeat((byte)0x53, 32).ToArray(),
                        IssueNotBefore = DateTimeOffset.UnixEpoch,
                    },
                }), time, length => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    $"sqlite-semantic-certification-nonce:{nonceOrdinal++}"))[..length]);
            SqliteRecordStore store = SemanticStore(path, installedDefinition: definition,
                definitionSetChecksum: definitionSetChecksum,
                migrations: needsGraphTransition ? [migration] : [],
                removals: needsGraphTransition ? [removal] : [],
                additionalDefinitions: needsGraphTransition ? [definitionV2] : [],
                administrationEnabled: true, administrationOperations: faults,
                restoreStagingTimeout: TimeSpan.FromSeconds(1),
                suppliedTokenProtector: tokenProtector, suppliedTimeProvider: time,
                installCertificationSubjectLifecycle: true);
            if (caseId == "maintenance-remove")
                PrepareRemovalGraph(path, definition, definitionV2);
            Microsoft.Extensions.DependencyInjection.ServiceProvider services =
                CreateCertificationServices(store, tokenProtector, time);
            return new SqliteCertificationStore(
                path, store, faults, definition, migration, removal, time, services);
        }

        private static void PrepareRemovalGraph(
            string path, BaseSemanticActivationKeyDefinition removed,
            BaseSemanticActivationKeyDefinition retained)
        {
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE hpd_base_semantic_activation_definitions
                SET execution_enabled = CASE
                    WHEN definition_version = $retainedVersion
                     AND definition_checksum = $retainedChecksum THEN 1
                    ELSE 0 END
                WHERE definition_id = $id;
                """;
            command.Parameters.AddWithValue("$id", removed.Id);
            command.Parameters.AddWithValue("$retainedVersion", retained.Version);
            command.Parameters.Add("$retainedChecksum", SqliteType.Blob).Value = retained.Checksum.ToArray();
            if (command.ExecuteNonQuery() != 2)
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid:removal-graph");
        }

        private static Microsoft.Extensions.DependencyInjection.ServiceProvider CreateCertificationServices(
            SqliteRecordStore store, BaseOpaqueTokenProtector tokenProtector, BaseTestTimeProvider time)
        {
            BaseGeneratedSubjectRegistration subject =
                BaseSemanticActivationCertificationSubjectAuthority.Registration;
            BaseGeneratedSubjectLifecycleConsumerIdentity<BaseSemanticActivationCertificationLifecycleSubject> lifecycle =
                BaseSemanticActivationCertificationSubjectAuthority.Lifecycle;
            BaseGeneratedSubjectRetirementConsumerIdentity<BaseSemanticActivationCertificationLifecycleSubject> retirement =
                BaseSemanticActivationCertificationSubjectAuthority.Retirement;
            BaseGeneratedSubjectRetirementPolicyIdentity<BaseSemanticActivationCertificationLifecycleSubject> policy =
                BaseSemanticActivationCertificationSubjectAuthority.RetirementPolicy;
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton(tokenProtector);
            var testPolicy = new BaseTestPolicy();
            services.AddSingleton(testPolicy);
            services.AddHPDBaseRuntime();
            services.Configure<HPDBaseSchemaOptions>(options =>
                options.ApplicationId = "certification-application");
            var policyAuthority = new BasePolicyAuthorityBuilder();
            policyAuthority.AddPolicy(new BasePolicyAuthorityDefinition
            {
                Id = "certification.policy", Version = 1, OwningModuleId = "certification",
                EvaluatorContractId = "certification.policy-evaluator",
                EvaluatorContractVersion = 1, CompositionOrder = 0,
            }, new BaseTestPolicyEvaluator(testPolicy));
            foreach (AccessGrant grant in CertificationGrants())
                policyAuthority.AddStaticGrant(new BaseGrantAuthorityDefinition
                {
                    Id = grant.Id, Version = 1, OwningModuleId = "certification",
                    SourceContractId = "certification.static-grants", SourceContractVersion = 1,
                }, grant);
            services.AddSingleton(policyAuthority.Freeze("certification-application"));
            services.AddSingleton<IBaseDescriptorContributor>(new CertificationCollectionContributor());
            services.AddSingleton(new BaseCollectionRegistry(
                new Dictionary<string, CollectionDefinition>(StringComparer.Ordinal)
                {
                    [BaseSemanticActivationCertificationSubjectAuthority.CollectionId] =
                        BaseSemanticActivationCertificationSubjectAuthority.Collection,
                }));
            var subjectRegistry = new BaseSubjectContractRegistry([subject]);
            var lifecycleRegistry = new BaseSubjectLifecycleRegistry([lifecycle.Definition], subjectRegistry);
            services.AddSingleton(subjectRegistry);
            services.AddSingleton(lifecycleRegistry);
            services.AddSingleton(new BaseSubjectRetirementRegistry(
                [retirement.Definition], [policy.Policy], lifecycleRegistry));
            services.AddSingleton<IBaseSessionFactory, DefaultBaseSessionFactory>();
            Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync()
                .AsTask().GetAwaiter().GetResult();
            provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
            {
                StoreId = store.Capabilities.StoreId, Store = store,
                CollectionIds = [BaseSemanticActivationCertificationSubjectAuthority.CollectionId],
            });
            return provider;
        }

        private static IEnumerable<AccessGrant> CertificationGrants()
        {
            yield return RuntimeGrant("certification.subjects.runtime");
            foreach ((string id, string action) in new[]
            {
                ("base.subjectLifecycle.tombstone", "base.subjectLifecycle.tombstone"),
                ("certification.subject.lifecycle.read", "certification.subject.lifecycle"),
                ("base.subjectLifecycle.feed.read", "base.subjectLifecycle.feed.read"),
                ("base.subjectLifecycle.feed.checkpoint", "base.subjectLifecycle.feed.checkpoint"),
                ("certification.subject.retirement.acknowledge", "certification.subject.lifecycle"),
                ("base.subjectRetirement.acknowledge", "base.subjectRetirement.acknowledge"),
                ("base.subjectRetirement.purge", "base.subjectRetirement.purge"),
            })
                yield return SubjectGrant(id, action);
            yield return new AccessGrant
            {
                Id = "certification.subject.retirement.purge.source",
                ApplicationId = "certification-application", ModuleId = "certification",
                Audience = HPDBaseEndpointAudience.Application,
                Subject = CertificationAccessSubject(),
                Action = BaseSemanticActivationCertificationSubjectAuthority.CollectionId,
                Effect = GrantEffect.Allow,
                Scope = new ResourceScope
                {
                    Kind = ResourceScopeKind.Collection,
                    CollectionId = BaseSemanticActivationCertificationSubjectAuthority.CollectionId,
                },
            };
        }

        private static AccessGrant RuntimeGrant(string id) => new()
        {
            Id = id, ApplicationId = "certification-application", ModuleId = "certification",
            Audience = HPDBaseEndpointAudience.Application, Subject = CertificationAccessSubject(),
            Action = "*", Effect = GrantEffect.Allow,
            Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        };

        private static AccessGrant SubjectGrant(string id, string action) => new()
        {
            Id = id, ApplicationId = "certification-application", ModuleId = "certification",
            Audience = HPDBaseEndpointAudience.Application, Subject = CertificationAccessSubject(),
            Action = action, Effect = GrantEffect.Allow,
            Scope = new ResourceScope
            {
                Kind = ResourceScopeKind.SubjectContract,
                SubjectContractId = BaseSemanticActivationCertificationSubjectAuthority.ContractId,
                SubjectContractVersion = 1,
            },
        };

        private static AccessSubject CertificationAccessSubject() => new()
        {
            Kind = AccessSubjectKind.System, Id = "semantic-certification",
        };

        private sealed class CertificationCollectionContributor : IBaseDescriptorContributor
        {
            public string Id => "certification.subjects";
            public void Contribute(IBaseDescriptorContributionBuilder builder) =>
                builder.AddCollection(BaseSemanticActivationCertificationSubjectAuthority.Collection);
        }
    }

    private sealed class SqliteCertificationStore(
        string path, SqliteRecordStore store, SemanticCertificationAdministrationFaults faults,
        BaseSemanticActivationKeyDefinition installedDefinition,
        BaseSemanticActivationMigrationDefinition installedMigration,
        BaseSemanticActivationRemovalAuthority installedRemoval,
        BaseTestTimeProvider time,
        Microsoft.Extensions.DependencyInjection.ServiceProvider services)
        : IBaseSemanticActivationCertificationStore, IDisposable
    {
        private BaseBackupManifest? manifest;
        private SqliteRecordStore.SemanticRecoveryCertificationEvidence? recoveryFloor;
        private BlockingReadStream? retainedRestore;
        private bool recoveryFaultApplied;
        public string LogicalStoreId => "module-store";
        public TimeSpan NonCooperativeTransactionTimeout => TimeSpan.FromSeconds(1);
        public bool RecoveryFloorVerified { get; private set; }
        private BaseTestTimeProvider Time { get; } = time;
        private Microsoft.Extensions.DependencyInjection.ServiceProvider Services { get; } = services;
        public ValueTask InstallFaultAsync(BaseSemanticActivationCertificationFaultRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            faults.Install(request);
            return ValueTask.CompletedTask;
        }
        public ValueTask<bool> ReleaseLateWorkAsync(BaseSemanticActivationCertificationFault fault, int occurrence, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool released = faults.Release(fault, occurrence);
            if (fault == BaseSemanticActivationCertificationFault.NonCooperativeRestore && occurrence == 1)
                released |= retainedRestore?.Release() == true;
            return ValueTask.FromResult(released);
        }
        public IAtomicRecordStore AtomicStore => store;
        public IBaseActivationProvider ActivationProvider => store;
        public IBaseSemanticActivationCapabilityProvider SemanticProvider => store;
        public BaseModuleMutationCapability ModuleMutationCapability => store.Capabilities.ModuleMutation!;
        public IBaseSemanticActivationAdministration? SemanticAdministration => store;
        public ValueTask<(long Live, long Retired, long Absent, long Activations, long Receipts)> ObserveAsync(CancellationToken cancellationToken) => store.ObserveSemanticActivationCertificationStateAsync(cancellationToken);
        public ValueTask<ImmutableArray<byte>> ReadAuthorityAsync(CancellationToken cancellationToken) => store.ReadSemanticActivationCertificationAuthorityAsync(cancellationToken);
        public (int Active, int Quarantined, int Released, int RejectedLateCompletions) ObserveLateWork() => store.ObserveSemanticLateWorkCertificationState();
        public ValueTask CorruptAsync(bool compactedAbsence, BaseSemanticActivationDefinitionIdentity definition, CancellationToken cancellationToken) => store.CorruptSemanticActivationCertificationStateAsync(compactedAbsence, definition, cancellationToken);
        public async ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(Stream destination, BaseBackupRequest request, CancellationToken cancellationToken)
        {
            recoveryFloor = await store.CaptureSemanticActivationRecoveryFloorCertificationAsync(cancellationToken);
            if (!recoveryFaultApplied && faults.InstalledFault is BaseSemanticActivationCertificationFault.CorruptRecoveryEntry
                    or BaseSemanticActivationCertificationFault.RetentionOvertake)
            {
                recoveryFaultApplied = true;
                await store.CorruptSemanticActivationRecoveryFloorCertificationAsync(
                    faults.InstalledFault == BaseSemanticActivationCertificationFault.RetentionOvertake,
                    cancellationToken);
            }
            OperationResult<BaseBackupManifest> result = await store.CreateBackupAsync(destination, request, cancellationToken);
            if (result.IsSuccess()) manifest = result.Value;
            return result;
        }
        public ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(Stream source, BaseRestoreRequest request, CancellationToken cancellationToken)
        {
            BaseBackupManifest authority = manifest ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            return RestoreAndVerifyAsync(source, request with
            {
                ExpectedCurrentStoreIdentityDigest = authority.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = authority.StoreIdentityDigest,
            }, cancellationToken);
        }
        private async ValueTask<OperationResult<BaseRestoreResult>> RestoreAndVerifyAsync(
            Stream source, BaseRestoreRequest request, CancellationToken cancellationToken)
        {
            Stream effectiveSource = source;
            if (faults.InstalledFault == BaseSemanticActivationCertificationFault.NonCooperativeRestore)
                effectiveSource = retainedRestore = new BlockingReadStream(source);
            OperationResult<BaseRestoreResult> result = await store.RestoreAsync(effectiveSource, request, cancellationToken);
            if (result.IsSuccess() && recoveryFloor is { } expected)
            {
                SqliteRecordStore.SemanticRecoveryCertificationEvidence substituted = expected with
                {
                    InvariantChecksum = SHA256.HashData("substituted-semantic-floor"u8).ToImmutableArray(),
                };
                bool substitutionRejected = !await store.VerifySemanticActivationRecoveryFloorCertificationAsync(substituted, cancellationToken)
                    && !await store.VerifySemanticActivationRecoveryFloorCertificationAsync(
                        expected with { RestoreEpoch = checked(expected.RestoreEpoch + 1) }, cancellationToken)
                    && !await store.VerifySemanticActivationRecoveryFloorCertificationAsync(
                        expected with { AuthorityGeneration = checked(expected.AuthorityGeneration + 1) }, cancellationToken);
                RecoveryFloorVerified = substitutionRejected
                    && await store.ProveSemanticActivationHistoricalReceiptSubstitutionRejectedAsync(expected, cancellationToken)
                    && await store.VerifySemanticActivationRecoveryFloorCertificationAsync(expected, cancellationToken);
            }
            return result;
        }
        public async ValueTask<BaseSemanticActivationCertificationOperationInput?> CreateAdministrationInputAsync(
            BaseSemanticActivationCertificationOperation operation, string caseId, int ordinal,
            DateTimeOffset deadlineUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BaseSemanticActivationKeyDefinition definition = BaseSemanticActivationCertificationProcessor.InstalledDefinition(ExecutionLimits());
            var key = new BaseSemanticActivationDefinitionKey { Id = definition.Id, Version = definition.Version, Checksum = definition.Checksum };
            if (operation == BaseSemanticActivationCertificationOperation.Inspect)
            {
                var request = new BaseSemanticActivationProviderInspectionRequest
                {
                    ApplicationId = "certification-application", LogicalStoreId = "module-store", RestoreEpoch = 0,
                    ProviderIncarnation = store.ProviderIncarnation,
                    Definition = key, State = null, After = null, Take = 256, Limits = SqliteSemanticEnsureProbe.CreateLimits(),
                    RuntimeRequestAuthorityChecksum = [],
                };
                request = request with { RuntimeRequestAuthorityChecksum = BaseSemanticActivationInspectionContract.RequestChecksum(request) };
                return new() { Inspection = request };
            }
            if (operation == BaseSemanticActivationCertificationOperation.MaintenanceAuthority)
            {
                var request = new BaseSemanticActivationMaintenanceAuthorityRequest
                {
                    ApplicationId = "certification-application", LogicalStoreId = "module-store", RestoreEpoch = 0,
                    ProviderIncarnation = store.ProviderIncarnation,
                    Definition = key, SemanticAuthorityGeneration = 1, MaximumRows = 1,
                    MaximumBytes = 1_048_576, RuntimeRequestChecksum = [],
                };
                request = request with
                { RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(request) };
                return new() { MaintenanceAuthority = request };
            }
            if (operation == BaseSemanticActivationCertificationOperation.Maintain)
            {
                byte[] fingerprint = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(caseId));
                if (caseId is "maintenance-compact-multipage"
                    or "maintenance-progress-invisible"
                    or "maintenance-resume")
                {
                    BaseSemanticActivationMaintenanceAuthority authority = await SeedCompactionSlotsAsync(
                        caseId, deadlineUtc, cancellationToken).ConfigureAwait(false);
                    var inspection = new BaseSemanticActivationProviderInspectionRequest
                    {
                        ApplicationId = "certification-application", LogicalStoreId = LogicalStoreId,
                        RestoreEpoch = 0, ProviderIncarnation = store.ProviderIncarnation,
                        Definition = key, State = null, After = null, Take = 1,
                        Limits = SqliteSemanticEnsureProbe.CreateLimits(),
                        RuntimeRequestAuthorityChecksum = [],
                    };
                    inspection = inspection with
                    {
                        RuntimeRequestAuthorityChecksum =
                            BaseSemanticActivationInspectionContract.RequestChecksum(inspection),
                    };
                    return new()
                    {
                        Inspection = inspection,
                        Maintenance = new BaseSemanticActivationCompactRequest
                        {
                            Identity = MaintenanceIdentity("compact", caseId, fingerprint),
                            Definition = key, ProviderIncarnation = store.ProviderIncarnation,
                            ExpectedSemanticAuthorityGeneration = authority.SemanticAuthorityGeneration,
                            ExpectedRetiredCount = authority.RetiredCount,
                            ExpectedRetiredChecksum = authority.RetiredAuthorityChecksum,
                            Limits = new() { PageSize = 1, MaximumPages = 8, MaximumRows = 8,
                                MaximumBytes = 1_048_576, Deadline = TimeSpan.FromSeconds(5) },
                        },
                    };
                }
                if (caseId == "maintenance-migrate")
                {
                    await SeedLiveSlotsAsync(caseId, deadlineUtc, cancellationToken).ConfigureAwait(false);
                    return new()
                    {
                        Maintenance = new BaseSemanticActivationMigrateRequest
                        {
                            Identity = MaintenanceIdentity("migrate", caseId, fingerprint),
                            ProviderIncarnation = store.ProviderIncarnation,
                            Definition = installedMigration.From,
                            ExpectedSemanticAuthorityGeneration = 1,
                            Migration = installedMigration,
                            Limits = new() { PageSize = 1, MaximumPages = 16, MaximumRows = 512,
                                MaximumBytes = 1_048_576, Deadline = TimeSpan.FromSeconds(5) },
                        },
                    };
                }
                if (caseId == "maintenance-remove")
                {
                    return new()
                    {
                        Maintenance = new BaseSemanticActivationRemoveRequest
                        {
                            Identity = MaintenanceIdentity("remove", caseId, fingerprint),
                            ProviderIncarnation = store.ProviderIncarnation,
                            Definition = new() { Id = installedRemoval.From.Id,
                                Version = installedRemoval.From.Version,
                                Checksum = installedRemoval.From.Checksum },
                            ExpectedSemanticAuthorityGeneration = 1,
                            RemovalAuthority = installedRemoval,
                            ExpectedLiveCount = 0, ExpectedRetiredCount = 0, ExpectedAbsenceCount = 0,
                            ExpectedDefinitionStateChecksum = EmptyDefinitionStateChecksum(),
                            ExpectedAbsenceAuthorityChecksum = EmptyOrderedAuthoritiesChecksum(),
                            Limits = new() { PageSize = 256, MaximumPages = 1, MaximumRows = 512,
                                MaximumBytes = 1_048_576, Deadline = TimeSpan.FromSeconds(5) },
                        },
                    };
                }
                return new()
                {
                    Maintenance = new BaseSemanticActivationCompactRequest
                    {
                        Identity = MaintenanceIdentity("compact", caseId, fingerprint), Definition = key,
                        ProviderIncarnation = store.ProviderIncarnation,
                        ExpectedSemanticAuthorityGeneration = 1, ExpectedRetiredCount = 0,
                        ExpectedRetiredChecksum = EmptyOrderedAuthoritiesChecksum(),
                        Limits = new() { PageSize = 256, MaximumPages = 1, MaximumRows = 1,
                            MaximumBytes = 1_048_576, Deadline = TimeSpan.FromSeconds(5) },
                    },
                };
            }
            if (operation is BaseSemanticActivationCertificationOperation.BackupRestore or BaseSemanticActivationCertificationOperation.RecoveryFloor)
            {
                PrincipalContext principal = AdministrationPrincipal();
                return new()
                {
                    Backup = new BaseBackupRequest { StoreId = "module-store", Principal = principal },
                    Restore = new BaseRestoreRequest
                    {
                        StoreId = "module-store", Principal = principal,
                        ExpectedCurrentStoreIdentityDigest = "pending", ExpectedArtifactStoreIdentityDigest = "pending",
                        IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                        RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                        ConfirmDestructiveReplacement = true, ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
                    },
                };
            }
            return null;
        }

        private async ValueTask SeedLiveSlotsAsync(
            string caseId, DateTimeOffset deadlineUtc, CancellationToken cancellationToken)
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseAtomicMutationAuthorityRequirement authority = (await store
                .CaptureAtomicMutationAuthorityRequirementAsync(
                    "certification-application", [], limits, cancellationToken)
                .ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:authority");
            for (int index = 0; index < 2; index++)
            {
                string id = $"{caseId}:seed:{index}";
                var processor = new BaseSemanticActivationCertificationProcessor(
                    authority, limits, LogicalStoreId, id,
                    semanticKey: $"certification-subject-{index}",
                    installedDefinition: installedDefinition);
                RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(
                    processor, CertificationRequest(id, deadlineUtc), cancellationToken).ConfigureAwait(false);
                if (result.Outcome != RecordMutationExecutionOutcome.Committed)
                    throw new InvalidOperationException(
                        $"base.semanticActivation.certificationInvalid:seed:{result.Error?.Code}");
            }
        }

        private async ValueTask<BaseSemanticActivationMaintenanceAuthority> SeedCompactionSlotsAsync(
            string caseId, DateTimeOffset deadlineUtc, CancellationToken cancellationToken)
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            for (int index = 0; index < 2; index++)
            {
                BaseSubjectReference<BaseSemanticActivationCertificationLifecycleSubject> subject =
                    await CreateCertificationSubjectAsync(caseId, index, cancellationToken)
                        .ConfigureAwait(false);
                BaseSemanticActivationSubjectLifetimeBinding lifetime =
                    BaseSemanticActivationCertificationSubjectAuthority.Bind(subject);
                BaseAtomicMutationAuthorityRequirement authority = (await store
                    .CaptureAtomicMutationAuthorityRequirementAsync(
                        "certification-application", [], limits, cancellationToken)
                    .ConfigureAwait(false)).Value
                    ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:authority");
                string prefix = $"{caseId}:compact:{index}";
                var ensure = new BaseSemanticActivationCertificationProcessor(
                    authority, limits, LogicalStoreId, prefix + ":ensure",
                    acceptedTime: checked(100L + index * 86_402_000L), semanticKey: prefix,
                    subjectLifetime: lifetime, installedDefinition: installedDefinition);
                RecordMutationExecutionResult ensured = await store.ExecuteAtomicAsync(
                    ensure, CertificationRequest(prefix + ":ensure", deadlineUtc), cancellationToken)
                    .ConfigureAwait(false);
                if (ensured.Outcome != RecordMutationExecutionOutcome.Committed
                    || ensure.Provisional?.ActivationId is null)
                    throw new InvalidOperationException(
                        $"base.semanticActivation.certificationInvalid:ensure:{ensured.Error?.Code}");
                await CompleteAndDisposeCertificationActivationAsync(
                    ensure.Provisional.ActivationId, prefix, checked(100L + index * 86_402_000L),
                    cancellationToken).ConfigureAwait(false);
                authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "certification-application", [], limits, cancellationToken)
                    .ConfigureAwait(false)).Value
                    ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:authority");
                var retire = new BaseSemanticActivationCertificationProcessor(
                    authority, limits, LogicalStoreId, prefix + ":retire", retire: true,
                    acceptedTime: checked(104L + index * 86_402_000L), semanticKey: prefix,
                    subjectLifetime: lifetime, installedDefinition: installedDefinition);
                RecordMutationExecutionRequest retireRequest =
                    CertificationRequest(prefix + ":retire", deadlineUtc);
                retireRequest = retireRequest with
                {
                    AtomicRequest = retireRequest.AtomicRequest! with
                    {
                        ExpiresAt = Time.GetUtcNow().AddSeconds(1),
                    },
                };
                RecordMutationExecutionResult retired = await store.ExecuteAtomicAsync(
                    retire, retireRequest, cancellationToken)
                    .ConfigureAwait(false);
                if (retired.Outcome != RecordMutationExecutionOutcome.Committed)
                    throw new InvalidOperationException(
                        $"base.semanticActivation.certificationInvalid:retire:{retired.Error?.Code}");
                await RetireCertificationSubjectAsync(subject, prefix, cancellationToken)
                    .ConfigureAwait(false);
                await PruneCertificationActivationAsync(
                    prefix, checked(86_401_100L + index * 86_402_000L), cancellationToken)
                    .ConfigureAwait(false);
                Time.Advance(TimeSpan.FromSeconds(2));
                authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                    "certification-application", [], limits, cancellationToken)
                    .ConfigureAwait(false)).Value
                    ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:authority");
                var expiredReceiptReplay = new BaseSemanticActivationCertificationProcessor(
                    authority, limits, LogicalStoreId, prefix + ":retire", retire: true,
                    acceptedTime: checked(86_401_101L + index * 86_402_000L), semanticKey: prefix,
                    subjectLifetime: lifetime, installedDefinition: installedDefinition);
                RecordMutationExecutionResult replayed = await store.ExecuteAtomicAsync(
                    expiredReceiptReplay, retireRequest, cancellationToken).ConfigureAwait(false);
                if (replayed.Outcome != RecordMutationExecutionOutcome.Committed)
                    throw new InvalidOperationException(
                        $"base.semanticActivation.certificationInvalid:receipt-expiry:{replayed.Error?.Code}");
            }
            var request = new BaseSemanticActivationMaintenanceAuthorityRequest
            {
                ApplicationId = "certification-application", LogicalStoreId = LogicalStoreId,
                ProviderIncarnation = store.ProviderIncarnation, RestoreEpoch = 0,
                Definition = new() { Id = installedDefinition.Id, Version = installedDefinition.Version,
                    Checksum = installedDefinition.Checksum },
                SemanticAuthorityGeneration = 1, MaximumRows = 8, MaximumBytes = 1_048_576,
                RuntimeRequestChecksum = [],
            };
            request = request with
            {
                RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(request),
            };
            return (await store.InspectMaintenanceAuthorityAsync(request, cancellationToken)
                .ConfigureAwait(false)).RequireValue();
        }

        private async ValueTask<BaseSubjectReference<BaseSemanticActivationCertificationLifecycleSubject>>
            CreateCertificationSubjectAsync(
                string caseId, int index, CancellationToken cancellationToken)
        {
            string id = $"subject-{index}-{Convert.ToHexStringLower(SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(caseId)))[..12]}";
            PrincipalContext principal = CertificationPrincipal();
            var payload = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["active"] = ParseCanonicalJson("true"),
                    ["tombstoned"] = ParseCanonicalJson("false"),
                },
            };
            OperationResult<RecordEnvelope> created = await Services
                .GetRequiredService<IBaseRecordRuntime>()
                .CreateAsync(
                    BaseSemanticActivationCertificationSubjectAuthority.CollectionId,
                    new RecordCreateRequest { RequestedId = RecordId.Create(id), Payload = payload },
                    principal,
                    CertificationOperation(BaseOperationKind.Create,
                        BaseSemanticActivationCertificationSubjectAuthority.CollectionId),
                    cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess())
                throw new InvalidOperationException(
                    $"base.semanticActivation.certificationInvalid:subject-create:{created.Error?.Code}");

            OperationResult<BaseRelationalReadExecutionResult> acquired = await store.ExecuteReadAsync(new()
            {
                ApplicationId = "certification-application", LogicalStoreId = LogicalStoreId,
                LogicalSchemaChecksum = BaseSchemaAuthorityChecksum.Create(new byte[32]),
                Plan = new BaseRelationalReadPlan
                {
                    Id = "certification.subject.acquire", Topology = BaseRelationalReadTopology.Ordinary,
                    SchemaGeneration = 1,
                    Pagination = new BaseRegisteredReadPaginationAuthority
                    {
                        Mode = BaseRegisteredReadPaginationMode.PageOnly, MaximumOffset = 0,
                    },
                    Sources = [new BaseRelationalReadSource
                    {
                        Id = "subjects",
                        CollectionId = BaseSemanticActivationCertificationSubjectAuthority.CollectionId,
                    }],
                    Predicate = new BaseRelationalPredicate
                    {
                        Kind = FilterNodeKind.Compare, Operator = FilterOperator.Equal,
                        Left = new BaseRelationalOperand
                        {
                            Kind = BaseRelationalOperandKind.RecordId, SourceId = "subjects",
                            FieldId = "base.recordId",
                        },
                        Right = new BaseRelationalOperand
                        {
                            Kind = BaseRelationalOperandKind.Literal,
                            Literal = BaseQueryValue.From(id),
                        },
                    },
                    Projection = [new BaseRelationalReadProjection
                    {
                        FieldId = "reference", Operand = new BaseRelationalOperand
                        {
                            Kind = BaseRelationalOperandKind.SubjectReference, SourceId = "subjects",
                            SubjectContractId = BaseSemanticActivationCertificationSubjectAuthority.ContractId,
                            SubjectContractVersion = 1,
                        },
                    }],
                    Parameters = [], Budgets = new BaseRelationalReadBudgets
                    {
                        MaxResultRows = 1, MaxResultBytes = 4096, MaxOperations = 16,
                        MaxExecutionMilliseconds = 2_000, MaxCompoundBranches = 0,
                        MaxCompoundOperations = 0,
                    },
                },
                ParameterValues = [], SourcePolicies = [new BaseRelationalReadSourcePolicy
                {
                    SourceId = "subjects",
                    CollectionId = BaseSemanticActivationCertificationSubjectAuthority.CollectionId,
                }],
                Operation = CertificationOperation(BaseOperationKind.SubjectAcquire,
                    BaseSemanticActivationCertificationSubjectAuthority.CollectionId),
                AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1),
                MaxResultRows = 1, MaxResultBytes = 4096,
            }, cancellationToken).ConfigureAwait(false);
            if (!acquired.IsSuccess() || acquired.Value?.Result.Rows is not { Length: 1 } rows)
                throw new InvalidOperationException(
                    $"base.semanticActivation.certificationInvalid:subject-acquire:{acquired.Error?.Code}");
            QueryValue value = rows[0].Fields.Single().Value;
            return new BaseSubjectReference<BaseSemanticActivationCertificationLifecycleSubject>(
                BaseSubjectId.Create(value.SubjectId!, value.SubjectIdKind!.Value),
                BaseSubjectAuthorityEpoch.Parse(value.SubjectAuthorityEpoch!),
                BaseSubjectIncarnation.Parse(value.SubjectIncarnation!));
        }

        private async ValueTask RetireCertificationSubjectAsync(
            BaseSubjectReference<BaseSemanticActivationCertificationLifecycleSubject> subject,
            string identityPrefix, CancellationToken cancellationToken)
        {
            PrincipalContext principal = CertificationPrincipal();
            IBaseRecordRuntime records = Services.GetRequiredService<IBaseRecordRuntime>();
            RecordEnvelope current = (await records.GetAsync(
                BaseSemanticActivationCertificationSubjectAuthority.CollectionId,
                RecordId.Create(subject.SubjectId.Value), principal,
                CertificationOperation(BaseOperationKind.Get,
                    BaseSemanticActivationCertificationSubjectAuthority.CollectionId),
                cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException(
                    "base.semanticActivation.certificationInvalid:subject-missing");
            BaseSession session = Services.GetRequiredService<IBaseSessionFactory>().For(principal);
            BaseExportedSubjectContract<BaseSemanticActivationCertificationLifecycleSubject> exporter =
                session.GetExportedSubjectContract<BaseSemanticActivationCertificationLifecycleSubject>(
                    BaseSemanticActivationCertificationSubjectAuthority.Registration);
            BaseSubjectTombstoneResult<BaseSemanticActivationCertificationLifecycleSubject> tombstone =
                (await exporter.TombstoneAsync(new()
                {
                    Subject = subject, ExpectedPrivateRevision = current.Metadata.Revision!.Value,
                    Identity = LifecycleIdentity(identityPrefix + ":tombstone"),
                }, cancellationToken).ConfigureAwait(false)).RequireValue();
            BaseInstalledSubjectRetirementConsumer<BaseSemanticActivationCertificationLifecycleSubject> consumer =
                session.SubjectRetirements.Get(
                    BaseSemanticActivationCertificationSubjectAuthority.Retirement);
            await using IAsyncEnumerator<BaseSubjectRequiredLifecycleDelivery<BaseSemanticActivationCertificationLifecycleSubject>> deliveries =
                consumer.ReadRequiredAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            if (!await deliveries.MoveNextAsync().ConfigureAwait(false))
                throw new InvalidOperationException(
                    "base.semanticActivation.certificationInvalid:retirement-delivery");
            BaseSubjectRequiredLifecycleDelivery<BaseSemanticActivationCertificationLifecycleSubject> delivery =
                deliveries.Current;
            BaseSubjectAcknowledgementResult acknowledgement = (await consumer.AcknowledgeAsync(
                delivery.Acknowledgement, BaseSubjectAcknowledgementDisposition.Completed,
                delivery.AcknowledgementIdentity, cancellationToken: cancellationToken)
                .ConfigureAwait(false)).RequireValue();
            BaseInstalledSubjectLifecycleConsumer<BaseSemanticActivationCertificationLifecycleSubject> lifecycle =
                session.SubjectLifecycle.Get(BaseSemanticActivationCertificationSubjectAuthority.Lifecycle);
            _ = (await lifecycle.AdvanceAsync(
                delivery.Lifecycle.Checkpoint, delivery.Lifecycle.AdvanceIdentity,
                cancellationToken: cancellationToken).ConfigureAwait(false)).RequireValue();
            _ = (await session.SubjectRetirements.PurgeAsync(new()
            {
                ContractId = BaseSemanticActivationCertificationSubjectAuthority.ContractId,
                ContractVersion = 1, SubjectId = subject.SubjectId,
                AuthorityEpoch = subject.AuthorityEpoch, Incarnation = subject.Incarnation,
                ExpectedTombstoneSequence = tombstone.Fact.Fact.SubjectSequence,
                ExpectedPrivateRevision = tombstone.PrivateRevision,
                ExpectedBarrierGeneration = acknowledgement.BarrierGeneration
                    ?? throw new InvalidOperationException(
                        "base.semanticActivation.certificationInvalid:barrier-generation"),
                ExpectedBarrierChecksum = acknowledgement.BarrierChecksum
                    ?? throw new InvalidOperationException(
                        "base.semanticActivation.certificationInvalid:barrier-checksum"),
                Identity = LifecycleIdentity(identityPrefix + ":purge"),
            }, cancellationToken: cancellationToken).ConfigureAwait(false)).RequireValue();
        }

        private static PrincipalContext CertificationPrincipal() => new()
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System, SubjectId = "semantic-certification",
        };

        private static OperationContext CertificationOperation(
            BaseOperationKind kind, string collectionId) => new()
        {
            ApplicationId = "certification-application", Operation = kind,
            CollectionId = collectionId, Audience = HPDBaseEndpointAudience.Application,
            Mode = OperationMode.System,
        };

        private static BaseMutationRequestIdentity LifecycleIdentity(string value) =>
            BaseMutationRequestIdentity.Create(
                "semantic-certification", "subject-lifecycle", value,
                BaseMutationRequestFingerprint.Create(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value))));

        private static JsonElement ParseCanonicalJson(string value)
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }

        private async ValueTask CompleteAndDisposeCertificationActivationAsync(
            string activationId, string prefix, long acceptedBase, CancellationToken cancellationToken)
        {
            BaseActivationExecutionLimits limits = CertificationActivationLimits();
            BaseOwnedScopeSeekAuthority scope = CertificationScope();
            BaseActivationDueObservation observation = (await store.ObserveDueAsync(new()
            {
                ApplicationId = "certification-application", WorkerModuleId = "certification",
                Definitions = [installedDefinition.Activation], Scope = scope,
                AcceptedTime = CertificationAcceptedTime(checked(acceptedBase + 1)),
                MaximumCandidates = 8, Limits = limits,
            }, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:observe");
            var worker = new BaseActivationWorkerAuthority
            {
                ApplicationId = "certification-application", ModuleId = "certification",
                WorkerIdentity = "sqlite-semantic-certification",
                Definitions = [installedDefinition.Activation], Scope = scope,
                Checksum = new byte[32].ToImmutableArray(),
            };
            BaseActivationClaimResult claimResult = (await store.TryClaimNextAsync(new()
            {
                Observation = observation.Token, Worker = worker,
                AcceptedTime = CertificationAcceptedTime(checked(acceptedBase + 1)),
                LeaseMilliseconds = 1_000, Identity = CertificationIdentity(prefix + ":claim"), Limits = limits,
            }, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:claim");
            BaseActivationClaimedResult claimed = claimResult as BaseActivationClaimedResult
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:not-claimed");
            if (!string.Equals(claimed.Claim.ActivationId, activationId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid:activation-mismatch");
            byte[] result = "certification-complete"u8.ToArray();
            BaseActivationTransitionResult completed = (await store.TransitionAsync(new BaseActivationCompleteRequest
            {
                ActivationId = activationId, Claim = claimed.Claim,
                CanonicalResult = result.ToImmutableArray(),
                ResultChecksum = SHA256.HashData(result).ToImmutableArray(),
                AcceptedTime = CertificationAcceptedTime(checked(acceptedBase + 2)),
                Identity = CertificationIdentity(prefix + ":complete"), Limits = limits,
            }, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:complete");
            _ = (await store.TransitionAsync(new BaseActivationDisposeRequest
            {
                ActivationId = activationId, ExpectedGeneration = completed.Generation,
                AcceptedTime = CertificationAcceptedTime(checked(acceptedBase + 3)),
                Identity = CertificationIdentity(prefix + ":dispose"), Limits = limits,
            }, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid:dispose");
        }

        private async ValueTask PruneCertificationActivationAsync(
            string prefix, long acceptedTime, CancellationToken cancellationToken)
        {
            OperationResult<BaseActivationPrunePage> result = await store.PruneAsync(new()
            {
                ApplicationId = "certification-application", Scope = CertificationScope(),
                Definition = installedDefinition.Activation, Take = 8,
                AcceptedTime = CertificationAcceptedTime(acceptedTime),
                Identity = CertificationIdentity(prefix + ":prune"),
                Limits = CertificationActivationLimits(),
            }, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value?.Items.Length != 1)
                throw new InvalidOperationException(
                    $"base.semanticActivation.certificationInvalid:prune:{result.Error?.Code}:{result.Value?.Items.Length}");
        }

        private static BaseMutationRequestIdentity MaintenanceIdentity(
            string operation, string caseId, byte[] fingerprint) =>
            BaseMutationRequestIdentity.Create(
                "semantic-certification", operation, caseId,
                BaseMutationRequestFingerprint.Create(fingerprint));

        private static RecordMutationExecutionRequest CertificationRequest(
            string id, DateTimeOffset deadlineUtc)
        {
            byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                "base.semanticActivation.sqliteCertificationRequest.v1\0" + id));
            return new()
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                CommitCompletionTimeout = TimeSpan.FromSeconds(5), AtomicRequest = new()
                {
                    Identity = BaseMutationRequestIdentity.Create(
                        "semantic-certification", "semantic.ensure", id,
                        BaseMutationRequestFingerprint.Create(digest)),
                    StructuralDigest = digest, ExpiresAt = deadlineUtc.AddMinutes(5),
                    MaxReceiptBytes = 1_048_576,
                },
            };
        }

        private static ImmutableArray<byte> EmptyOrderedAuthoritiesChecksum()
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
            return hash.GetHashAndReset().ToImmutableArray();
        }

        private static ImmutableArray<byte> EmptyDefinitionStateChecksum()
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("base.semanticActivation.definitionState.v1\0"u8);
            return hash.GetHashAndReset().ToImmutableArray();
        }

        private static BaseOwnedScopeSeekAuthority CertificationScope() => new()
        {
            Kind = BaseSubjectScopeKind.Global,
            ProtectedIndexDigest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
        };

        private static BaseActivationExecutionLimits CertificationActivationLimits() => new()
        {
            MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
            MaximumEvidenceBytes = 4096, MaximumTransientBytes = 16384,
            MaximumReadIntervals = 8, MaximumIndexOperations = 64,
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        };

        private static BaseMutationRequestIdentity CertificationIdentity(string key)
        {
            byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
            return BaseMutationRequestIdentity.Create(
                "semantic-certification", "activation.transition", key,
                BaseMutationRequestFingerprint.Create(digest));
        }

        private static BaseAcceptedTimeReceipt CertificationAcceptedTime(long milliseconds)
        {
            const string application = "certification-application";
            const long generation = 1, skew = 30_000;
            long sequence = checked(milliseconds + 1);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendCertification(hash, "base.activation.acceptedTime.v2\0");
            AppendCertification(hash, application); AppendCertification(hash, generation);
            AppendCertification(hash, milliseconds); AppendCertification(hash, milliseconds);
            AppendCertification(hash, sequence); AppendCertification(hash, skew);
            return new BaseAcceptedTimeReceipt(
                application, generation, milliseconds, milliseconds, sequence, skew, hash.GetHashAndReset());
        }

        private static void AppendCertification(IncrementalHash hash, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
            hash.AppendData(length); hash.AppendData(bytes);
        }

        private static void AppendCertification(IncrementalHash hash, long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            hash.AppendData(bytes);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await store.DisposeAsync();
            Delete();
        }
        public void Dispose()
        {
            Services.Dispose();
            store.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Delete();
        }
        private void Delete()
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private sealed class SemanticCertificationAdministrationFaults : ISqliteAdministrationOperationController
    {
        private BaseSemanticActivationCertificationFaultRequest? installed;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Install(BaseSemanticActivationCertificationFaultRequest request) => installed = request;
        internal BaseSemanticActivationCertificationFault? InstalledFault => installed?.Fault;
        internal bool Release(BaseSemanticActivationCertificationFault fault, int occurrence) =>
            installed is { } value && value.Fault == fault && value.Occurrence == occurrence
            && release.TrySetResult();

        public ValueTask BeforePhaseAsync(string phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (installed is not { Occurrence: 1 } value) return ValueTask.CompletedTask;
            if (string.Equals(phase, "postInstallValidation", StringComparison.Ordinal)
                && value.Fault == BaseSemanticActivationCertificationFault.InterruptRestorePublication)
                return ValueTask.FromException(new InvalidDataException(BaseSemanticActivationErrorCodes.MaintenanceIndeterminate));
            if (!string.Equals(phase, "semanticMaintenanceBeforePublication", StringComparison.Ordinal))
                return ValueTask.CompletedTask;
            return value.Fault switch
            {
                BaseSemanticActivationCertificationFault.NonCooperativeMaintenance => new ValueTask(release.Task),
                BaseSemanticActivationCertificationFault.InterruptMaintenancePublication =>
                    ValueTask.FromException(new InvalidDataException(BaseSemanticActivationErrorCodes.MaintenanceIndeterminate)),
                _ => ValueTask.CompletedTask,
            };
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    private sealed class BlockingReadStream(Stream inner) : Stream
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Release() => release.TrySetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await release.Task.ConfigureAwait(false);
            return await inner.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
