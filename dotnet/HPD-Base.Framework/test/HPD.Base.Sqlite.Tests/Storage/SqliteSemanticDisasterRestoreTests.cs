using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed partial class SqliteModuleMutationTests
{
    [Fact]
    public async Task Semantic_recovery_preflight_uses_exact_migration_control_receipt_authority()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l76-migrated-preflight-{Guid.NewGuid():N}.db");
        try
        {
            BaseAtomicMutationExecutionLimits mutationLimits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path, administrationEnabled: true);
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation-test", [], mutationLimits)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, mutationLimits, "migrated-preflight-live");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            string activationId = ensure.Provisional!.ActivationId!;
            BaseActivationDefinitionKey sourceDefinition = ActivationDefinition();
            BaseActivationExecutionLimits activationLimits = ActivationLimits();
            BaseActivationMigrationCandidate candidate = (await store.ReadMigrationCandidateAsync(
                new BaseActivationMigrationCandidateRequest
                {
                    ApplicationId = "activation-test", Scope = ActivationScope(), SourceDefinition = sourceDefinition,
                    ActivationId = activationId, ExpectedGeneration = 1, AcceptedTime = AcceptedTime(10),
                    Limits = activationLimits,
                })).Value!;
            byte[] replacementInput = "replacement"u8.ToArray();
            var targetDefinition = new BaseActivationDefinitionKey
            {
                Id = "test.activation.replacement", Version = 2,
                Checksum = SHA256.HashData("test.activation.replacement.v2"u8).ToImmutableArray(),
            };
            BaseActivationMigrationResult migrated = (await store.MigrateAsync(new BaseActivationMigrationRequest
            {
                ApplicationId = "activation-test", Scope = ActivationScope(), SourceDefinition = sourceDefinition,
                SourceActivationId = activationId, ExpectedSourceGeneration = 1,
                ExpectedSourceInputChecksum = candidate.InputChecksum,
                ReplacementActivationId = "migrated-preflight-replacement",
                Replacement = new BaseActivationCreateIntent
                {
                    Ordinal = 0, Definition = targetDefinition, ReceiptRetention = DefaultReceiptRetention(),
                    CanonicalInput = replacementInput.ToImmutableArray(),
                    InputChecksum = SHA256.HashData(replacementInput).ToImmutableArray(),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                    RequestedDueAt = 11, EffectiveDueAt = 11, Priority = 0, OverlapKey = [],
                    OverlapPolicy = BaseScheduleOverlapPolicy.Allow, InitiallyEligible = true, MaximumYields = 0,
                    Identity = ActivationIdentity("migrated-preflight-replacement"),
                },
                MigrationId = "test.activation.migration", MigrationVersion = 1,
                MigrationChecksum = SHA256.HashData("test.activation.migration.v1"u8).ToImmutableArray(),
                AcceptedTime = AcceptedTime(11), Identity = ActivationIdentity("migrated-preflight"),
                Limits = activationLimits,
            })).Value!;

            BaseSemanticRecoveryPreflightEvidence preflight = (await store.PreflightSemanticRecoveryAsync(
                PreflightRequest(authority))).Value!;
            preflight.ActivationState.Should().Be(BaseActivationState.Migrated);
            preflight.TerminalReceipt.Kind.Should().Be(BaseSemanticRecoveryTerminalReceiptKind.Migration);
            preflight.TerminalReceipt.Instance.Should().BeNull();
            preflight.TerminalReceipt.Migration!.Result.Should().BeEquivalentTo(migrated);
            preflight.TerminalReceipt.AuthorityChecksum.Should().Equal(preflight.ActivationTerminalReceiptChecksum);
            BaseSemanticRecoveryAuthorityContract.TerminalActivationIsValid(preflight.TerminalActivation).Should().BeTrue();
            BaseSemanticActivationEvidenceContract.RecoveryPreflightIsValid(
                PreflightRequest(authority), preflight).Should().BeTrue();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Semantic_disaster_suffix_dominates_live_and_pre_creation_artifacts_without_rematerialization(
        bool artifactContainsLive, bool receiptExpired)
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-l53-disaster-{Guid.NewGuid():N}.db");
        await using BaseSemanticRecoveryAuthorityRegistry registry = SqliteSemanticRecoveryTestAuthority.Create().Registry;
        SqliteSemanticRecoveryTestAuthority external = (SqliteSemanticRecoveryTestAuthority)registry.Find("module-store")!.Value.Instance;
        BaseSemanticRecoveryAuthorityDefinition recoveryDefinition = registry.Find("module-store")!.Value.Definition;
        try
        {
            BaseAtomicMutationExecutionLimits limits = ExecutionLimits();
            await using SqliteRecordStore store = SemanticStore(path, administrationEnabled: true);
            var artifact = new MemoryStream();
            BaseBackupManifest? backup = null;
            if (!artifactContainsLive)
                backup = (await store.CreateBackupAsync(artifact, new BaseBackupRequest
                { StoreId = "module-store", Principal = AdministrationPrincipal() })).Value!;
            BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
                "activation-test", [], limits)).Value!;
            var ensure = new SqliteSemanticEnsureProbe(authority, limits, "disaster-live");
            (await store.ExecuteAtomicAsync(ensure, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            await CompleteSemanticActivationAsync(store, "disaster-terminal");

            BaseSemanticRecoveryPreflightEvidence preflight = (await store.PreflightSemanticRecoveryAsync(
                PreflightRequest(authority))).Value!;
            if (artifactContainsLive)
                backup = (await store.CreateBackupAsync(artifact, new BaseBackupRequest
                { StoreId = "module-store", Principal = AdministrationPrincipal() })).Value!;

            authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync("activation-test", [], limits)).Value!;
            var retire = new SqliteSemanticEnsureProbe(authority, limits, "disaster-retire", retire: true);
            (await store.ExecuteAtomicAsync(retire, ExecutionRequest())).Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            BaseSemanticActivationRetirementAuthority retired = await ReadRetiredAsync(path);
            BaseSemanticRecoveryPublicationEntry publication = CreatePublication(
                external, recoveryDefinition, preflight, retired, retire.RecoveryReceiptJson!, receiptExpired);
            BaseAtomicReceiptWire envelopeWire = JsonSerializer.Deserialize(publication.LocalReceipt.ReceiptBytes.AsSpan(),
                HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!;
            BaseAtomicReceiptResult envelopeResult = envelopeWire.Materialize();
            envelopeResult.ModuleMutation!.SemanticActivation!.RecoveryPublication.Should().NotBeNull();
            JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(envelopeResult),
                HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).Should().Equal(publication.LocalReceipt.ReceiptBytes);
            BaseSemanticRecoveryAuthorityContract.LocalReceiptEnvelopeIsValid(publication.LocalReceipt).Should().BeTrue();
            BaseSemanticRecoveryAuthorityContract.TerminalActivationIsValid(publication.Entry.TerminalActivation).Should().BeTrue();
            BaseSemanticRecoveryAuthorityContract.PublicationCorrespondenceIsValid(recoveryDefinition,
                "activation-test", "module-store", publication).Should().BeTrue();
            BaseSemanticRecoveryLocalReceiptEnvelope wrongIdentity = publication.LocalReceipt with
            {
                Identity = BaseMutationRequestIdentity.Create("semantic", "other", "disaster-retire",
                    publication.LocalReceipt.Identity.Fingerprint), Checksum = [],
            };
            wrongIdentity = wrongIdentity with
            { Checksum = BaseSemanticRecoveryAuthorityContract.LocalReceiptEnvelopeChecksum(wrongIdentity) };
            BaseSemanticRecoveryAuthorityContract.PublicationCorrespondenceIsValid(recoveryDefinition,
                "activation-test", "module-store", publication with { LocalReceipt = wrongIdentity }).Should().BeFalse();
            BaseSemanticRecoveryLocalReceiptEnvelope wrongStructural = publication.LocalReceipt with
            { StructuralDigest = SHA256.HashData("substituted-structural"u8).ToImmutableArray(), Checksum = [] };
            wrongStructural = wrongStructural with
            { Checksum = BaseSemanticRecoveryAuthorityContract.LocalReceiptEnvelopeChecksum(wrongStructural) };
            BaseSemanticRecoveryAuthorityContract.PublicationCorrespondenceIsValid(recoveryDefinition,
                "activation-test", "module-store", publication with { LocalReceipt = wrongStructural }).Should().BeFalse();
            external.Publish(publication);

            long acceptedNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var headRequest = new BaseSemanticRecoveryHeadRequest
            {
                ApplicationId = "activation-test", LogicalStoreId = "module-store",
                ArtifactId = backup!.ProviderPayloadSha256,
                ArtifactChecksum = Convert.FromHexString(backup.ProviderPayloadSha256).ToImmutableArray(),
                Limits = recoveryDefinition.Limits,
            };
            BaseSemanticRecoveryPublishedHead directHead = ((BaseSuccess<BaseSemanticRecoveryPublishedHead>)
                await external.ReadHeadAsync(headRequest, default)).Value;
            BaseSemanticRecoveryAuthorityContract.PublishedHeadIsValid(recoveryDefinition,
                headRequest.ApplicationId, headRequest.LogicalStoreId,
                BaseSemanticRecoveryAuthorityContract.HeadRequestChecksum(headRequest), directHead).Should().BeTrue();
            var directPageRequest = new BaseSemanticRecoveryPageRequest
            { Head = directHead, AfterSequence = 0, Take = 1, Limits = recoveryDefinition.Limits };
            BaseSemanticRecoveryPublicationPage directPage = ((BaseSuccess<BaseSemanticRecoveryPublicationPage>)
                await external.ReadPageAsync(directPageRequest, default)).Value;
            BaseSemanticRecoveryAuthorityContract.PublicationPageIsValid(directPageRequest, directPage).Should().BeTrue();
            BaseResult<BaseSemanticRecoveryPublishedHead> routedHead = await registry.InvokeAsync("module-store",
                recoveryDefinition.Limits.ResolutionDeadline, headRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryHeadRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublishedHead,
                static (instance, value, token) => instance.ReadHeadAsync(value, token), default);
            routedHead.Should().BeOfType<BaseSuccess<BaseSemanticRecoveryPublishedHead>>();
            BaseResult<BaseSemanticRecoveryPublicationPage> routedPage = await registry.InvokeAsync("module-store",
                recoveryDefinition.Limits.ResolutionDeadline, directPageRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPageRequest,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublicationPage,
                static (instance, value, token) => instance.ReadPageAsync(value, token), default);
            routedPage.Should().BeOfType<BaseSuccess<BaseSemanticRecoveryPublicationPage>>();
            long directBytes = JsonSerializer.SerializeToUtf8Bytes(publication,
                HPDBaseJsonSerializerContext.Default.BaseSemanticRecoveryPublicationEntry).LongLength;
            var directAuthority = new BaseSemanticRecoveryRestoreAuthority
            {
                Definition = recoveryDefinition, AcceptedNow = acceptedNow, PageCount = 1,
                CanonicalBytes = directBytes, TransientBytes = directBytes, Limits = recoveryDefinition.Limits,
                ArtifactSequence = backup.SemanticTerminalPublicationSequence,
                ArtifactOrderedChecksum = backup.SemanticTerminalPublicationChecksum,
                HeadRequest = headRequest, Head = directHead, Publications = [publication], Checksum = [],
            };
            directAuthority = directAuthority with
            { Checksum = BaseSemanticRecoveryAuthorityContract.RestoreAuthorityChecksum(directAuthority) };
            BaseSemanticRecoveryAuthorityContract.RestoreAuthorityIsValid(recoveryDefinition, directAuthority).Should().BeTrue();
            BaseSemanticRecoveryPublishedHead routedHeadValue = ((BaseSuccess<BaseSemanticRecoveryPublishedHead>)routedHead).Value;
            BaseSemanticRecoveryPublicationPage routedPageValue = ((BaseSuccess<BaseSemanticRecoveryPublicationPage>)routedPage).Value;
            var routedAuthority = directAuthority with
            { Head = routedHeadValue, Publications = routedPageValue.Entries, Checksum = [] };
            routedAuthority = routedAuthority with
            { Checksum = BaseSemanticRecoveryAuthorityContract.RestoreAuthorityChecksum(routedAuthority) };
            BaseSemanticRecoveryAuthorityContract.RestoreAuthorityIsValid(recoveryDefinition, routedAuthority).Should().BeTrue();
            BaseResult<BaseSemanticRecoveryRestoreAuthority> suffix = await DefaultHPDBaseAdministration.ReadSemanticRestoreAuthorityAsync(
                registry, "activation-test", "module-store", backup, acceptedNow, default);
            BaseSemanticRecoveryRestoreAuthority restoreAuthority = suffix.Should()
                .BeOfType<BaseSuccess<BaseSemanticRecoveryRestoreAuthority>>().Subject.Value;
            artifact.Position = 0;
            OperationResult<BaseRestoreResult> restored = await store.RestoreAsync(artifact, new BaseRestoreRequest
            {
                StoreId = "module-store", Principal = AdministrationPrincipal(),
                ExpectedCurrentStoreIdentityDigest = backup.StoreIdentityDigest,
                ExpectedArtifactStoreIdentityDigest = backup.StoreIdentityDigest,
                IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                ConfirmDestructiveReplacement = true, ScheduleRestoreDomain = BaseScheduleRestoreDomain.InPlaceRecovery,
                RecoveryAcceptedNow = acceptedNow, RecoveryApplicationId = "activation-test",
                SemanticRecoveryAuthority = restoreAuthority,
            });
            restored.IsSuccess().Should().BeTrue($"{restored.Error?.Code}:{restored.Error?.Message}:{restored.Error?.Detail}");

            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT s.state,
                  (SELECT COUNT(*) FROM hpd_base_activations a WHERE a.activation_id=json_extract(s.authority_json,'$.activationId')),
                  COALESCE((SELECT a.state FROM hpd_base_activations a WHERE a.activation_id=json_extract(s.authority_json,'$.activationId')),-1),
                  COALESCE((SELECT a.generation FROM hpd_base_activations a WHERE a.activation_id=json_extract(s.authority_json,'$.activationId')),-1),
                  (SELECT COUNT(*) FROM hpd_base_operation_receipts WHERE idempotency_key='disaster-retire')
                FROM hpd_base_semantic_activation_slots s;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt32(0).Should().Be((int)BaseSemanticActivationSlotState.Retired);
            reader.GetInt64(1).Should().Be(artifactContainsLive ? 1 : 0);
            reader.GetInt32(2).Should().Be(artifactContainsLive ? (int)preflight.TerminalActivation.State : -1);
            reader.GetInt64(3).Should().Be(artifactContainsLive ? preflight.TerminalActivation.Generation : -1);
            reader.GetInt64(4).Should().Be(receiptExpired ? 0 : 1);
            await reader.DisposeAsync();
            RecordMutationExecutionResult replay = await store.ResolveAtomicReceiptAsync(
                retire, publication.LocalReceipt.Identity, TimeSpan.FromSeconds(5));
            if (receiptExpired)
                replay.ReceiptResolution.Should().Be(BaseAtomicReceiptResolutionDisposition.ConfirmedMissing);
            else
            {
                replay.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
                replay.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
                replay.ReceiptResolution.Should().Be(BaseAtomicReceiptResolutionDisposition.Found);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static BaseSemanticRecoveryPreflightRequest PreflightRequest(BaseAtomicMutationAuthorityRequirement authority)
    {
        BaseSemanticActivationKeyDefinition installed = SemanticDefinition();
        byte[] key = Encoding.UTF8.GetBytes("auth-user-42");
        return new BaseSemanticRecoveryPreflightRequest
        {
            Definition = new BaseSemanticActivationDefinitionIdentity
            {
                Id = installed.Id, Version = installed.Version, Checksum = installed.Checksum,
                OwnerGeneration = 1, OwningModuleId = installed.OwningModuleId,
                RetirementOperation = installed.RetirementOperation,
            },
            CanonicalKey = key.ToImmutableArray(), KeyPreimageChecksum = SHA256.HashData(key).ToImmutableArray(),
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
            MaximumCanonicalKeyBytes = installed.Limits.MaximumCanonicalKeyBytes,
            StoreAuthority = authority.SemanticActivation!, Limits = installed.Limits.Execution,
            Deadline = TimeSpan.FromSeconds(5),
        };
    }

    private static async Task<BaseSemanticActivationRetirementAuthority> ReadRetiredAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT authority_json FROM hpd_base_semantic_activation_slots WHERE state=2;";
        byte[] json = (byte[])(await command.ExecuteScalarAsync())!;
        return JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)!;
    }

    private static BaseSemanticRecoveryPublicationEntry CreatePublication(
        SqliteSemanticRecoveryTestAuthority external, BaseSemanticRecoveryAuthorityDefinition definition,
        BaseSemanticRecoveryPreflightEvidence preflight, BaseSemanticActivationRetirementAuthority retired,
        byte[] priorReceiptBytes, bool receiptExpired)
    {
        BaseSemanticActivationModuleOperationIdentity retirementOperation = SemanticDefinition().RetirementOperation;
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("semantic", "retire", "disaster-retire",
            BaseMutationRequestFingerprint.Create(SHA256.HashData("disaster-retire"u8)));
        ImmutableArray<byte> structuralDigest = SHA256.HashData("base.semanticRecovery.test.runtimeStructural.v1\0"u8).ToImmutableArray();
        var intent = new BaseSemanticRecoveryPendingTerminalIntent
        {
            Boundary = new BaseSemanticActivationRecoveryBoundary
            { DefinitionId = retired.Definition.Id, ScopeBindingId = preflight.ScopeBinding.BindingId, Key = retired.KeyDigest },
            RetirementOperationFingerprint = BaseSemanticRecoveryAuthorityContract
                .RetirementOperationFingerprint(retirementOperation),
            SubjectLifetime = retired.SubjectLifetime, Checksum = [],
        };
        intent = intent with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingIntentChecksum(intent) };
        var pending = new BaseSemanticRecoveryPendingPublication
        {
            Sequence = 1, TicketNonce = "ticket-one", IntentChecksum = intent.Checksum,
            SigningKeyId = "signing", SigningKeyVersion = 1,
            CancellationEligibleAt = DateTimeOffset.UnixEpoch.AddSeconds(1), Checksum = [], Signature = [],
        };
        pending = pending with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingChecksum(pending) };
        pending = pending with { Signature = external.Sign("base.semanticRecovery.pendingSignature.v1\0", pending.Checksum) };
        var pendingAuthority = new BaseSemanticRecoveryPendingCommitAuthority
        {
            ApplicationId = "activation-test", LogicalStoreId = "module-store",
            LocalScope = identity.Scope, LocalOperation = identity.Operation,
            LocalIdempotencyKey = identity.IdempotencyKey,
            LocalFingerprint = identity.Fingerprint.ToArray().ToImmutableArray(),
            LocalStructuralDigest = structuralDigest,
            AuthorityId = definition.Id, AuthorityVersion = definition.Version,
            AuthorityChecksum = definition.ContractChecksum, Intent = intent, Pending = pending, Checksum = [],
        };
        pendingAuthority = pendingAuthority with { Checksum = BaseSemanticRecoveryAuthorityContract.PendingCommitChecksum(pendingAuthority) };
        byte[] authorityBytes = JsonSerializer.SerializeToUtf8Bytes(retired,
            HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority);
        var entry = new BaseSemanticActivationRecoveryEntry
        {
            Boundary = intent.Boundary, ScopeBinding = preflight.ScopeBinding,
            TerminalActivation = preflight.TerminalActivation, RetirementOperation = retirementOperation,
            Definition = retired.Definition,
            State = BaseSemanticActivationSlotState.Retired, SlotGeneration = retired.SlotGeneration,
            AuthorityBytes = authorityBytes.ToImmutableArray(), Checksum = [],
        };
        entry = entry with { Checksum = BaseSemanticRecoveryAuthorityContract.RecoveryEntryChecksum(entry) };
        var local = new BaseSemanticRecoveryLocalReceiptAuthority
        { PendingAuthority = pendingAuthority, FinalEntry = entry, Checksum = [] };
        local = local with { Checksum = BaseSemanticRecoveryAuthorityContract.LocalReceiptAuthorityChecksum(local) };

        BaseAtomicReceiptResult prior = JsonSerializer.Deserialize(priorReceiptBytes,
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!.Materialize();
        BaseSemanticActivationReceiptEvidence semantic = prior.ModuleMutation!.SemanticActivation! with { RecoveryPublication = local };
        semantic = semantic with { Checksum = BaseSemanticActivationEvidenceContract.ReceiptChecksum(semantic) };
        BaseAtomicReceiptResult complete = prior with
        { ModuleMutation = prior.ModuleMutation with { SemanticActivation = semantic } };
        byte[] receiptBytes = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(complete),
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        var envelope = new BaseSemanticRecoveryLocalReceiptEnvelope
        {
            Identity = identity, StructuralDigest = structuralDigest,
            ReceiptBytes = receiptBytes.ToImmutableArray(), ReceiptChecksum = SHA256.HashData(receiptBytes).ToImmutableArray(),
            ReceiptFormatVersion = 2, SchemaGeneration = retired.StoreAuthority.Requirement.SchemaGeneration,
            StoreInstanceId = retired.StoreAuthority.Requirement.StoreInstanceId,
            CommittedAt = receiptExpired ? DateTimeOffset.UtcNow.AddDays(-2) : DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = receiptExpired ? DateTimeOffset.UtcNow.AddDays(-1) : DateTimeOffset.UtcNow.AddDays(1),
            CommitObservationChecksum = semantic.CommitEvidenceChecksum, Checksum = [],
        };
        envelope = envelope with { Checksum = BaseSemanticRecoveryAuthorityContract.LocalReceiptEnvelopeChecksum(envelope) };
        var publication = new BaseSemanticRecoveryPublicationEntry
        {
            Sequence = 1, Entry = entry, LocalReceipt = envelope,
            CommitObservationChecksum = semantic.CommitEvidenceChecksum, Checksum = [],
        };
        return publication with { Checksum = BaseSemanticRecoveryAuthorityContract.PublicationEntryChecksum(publication) };
    }
}
