using System.Collections.Immutable;
using System.Security.Cryptography;
using FluentAssertions;
using HPD.Base.Testing;
using Microsoft.Data.Sqlite;

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
            report.Subject, BaseSemanticActivationCapabilityContract.BuiltIn(durable: true));
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
                    Subject, Create(), caseId, ordinal, deadlineUtc));
        }
        private static SqliteCertificationStore Create()
        {
            string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-cert-{Guid.NewGuid():N}.db");
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            BaseSemanticActivationKeyDefinition definition = BaseSemanticActivationCertificationProcessor.InstalledDefinition(limits);
            var faults = new SemanticCertificationAdministrationFaults();
            return new SqliteCertificationStore(path, SemanticStore(path, installedDefinition: definition,
                definitionSetChecksum: BaseSemanticActivationCertificationProcessor.InstalledDefinitionSetChecksum,
                administrationEnabled: true, administrationOperations: faults,
                restoreStagingTimeout: TimeSpan.FromSeconds(1)), faults);
        }
    }

    private sealed class SqliteCertificationStore(
        string path, SqliteRecordStore store, SemanticCertificationAdministrationFaults faults)
        : IBaseSemanticActivationCertificationStore, IDisposable
    {
        private BaseBackupManifest? manifest;
        private SqliteRecordStore.SemanticRecoveryCertificationEvidence? recoveryFloor;
        private BlockingReadStream? retainedRestore;
        private bool recoveryFaultApplied;
        public string LogicalStoreId => "module-store";
        public TimeSpan NonCooperativeTransactionTimeout => TimeSpan.FromSeconds(1);
        public bool RecoveryFloorVerified { get; private set; }
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
        public ValueTask<BaseSemanticActivationCertificationOperationInput?> CreateAdministrationInputAsync(
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
                    Definition = key, State = null, After = null, Take = 256, Limits = SqliteSemanticEnsureProbe.CreateLimits(),
                    RuntimeRequestAuthorityChecksum = [],
                };
                request = request with { RuntimeRequestAuthorityChecksum = BaseSemanticActivationInspectionContract.RequestChecksum(request) };
                return ValueTask.FromResult<BaseSemanticActivationCertificationOperationInput?>(new() { Inspection = request });
            }
            if (operation == BaseSemanticActivationCertificationOperation.MaintenanceAuthority)
            {
                var request = new BaseSemanticActivationMaintenanceAuthorityRequest
                {
                    ApplicationId = "certification-application", LogicalStoreId = "module-store", RestoreEpoch = 0,
                    Definition = key, SemanticAuthorityGeneration = 1, MaximumRows = 1,
                    MaximumBytes = 1_048_576, RuntimeRequestChecksum = [],
                };
                request = request with
                { RuntimeRequestChecksum = BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(request) };
                return ValueTask.FromResult<BaseSemanticActivationCertificationOperationInput?>(new()
                { MaintenanceAuthority = request });
            }
            if (operation == BaseSemanticActivationCertificationOperation.Maintain)
            {
                byte[] fingerprint = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(caseId));
                return ValueTask.FromResult<BaseSemanticActivationCertificationOperationInput?>(new()
                {
                    Maintenance = new BaseSemanticActivationCompactRequest
                    {
                        Identity = BaseMutationRequestIdentity.Create("semantic-certification", "compact", caseId,
                            BaseMutationRequestFingerprint.Create(fingerprint)), Definition = key,
                        ExpectedSemanticAuthorityGeneration = 1, ExpectedRetiredCount = 0,
                        ExpectedRetiredChecksum = OrderedSemanticAuthoritiesChecksum([]).ToImmutableArray(),
                        Limits = new() { PageSize = 256, MaximumPages = 1, MaximumRows = 1,
                            MaximumBytes = 1_048_576, Deadline = TimeSpan.FromSeconds(5) },
                    },
                });
            }
            if (operation is BaseSemanticActivationCertificationOperation.BackupRestore or BaseSemanticActivationCertificationOperation.RecoveryFloor)
            {
                PrincipalContext principal = AdministrationPrincipal();
                return ValueTask.FromResult<BaseSemanticActivationCertificationOperationInput?>(new()
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
                });
            }
            return ValueTask.FromResult<BaseSemanticActivationCertificationOperationInput?>(null);
        }
        public async ValueTask DisposeAsync()
        {
            await store.DisposeAsync();
            Delete();
        }
        public void Dispose()
        {
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
