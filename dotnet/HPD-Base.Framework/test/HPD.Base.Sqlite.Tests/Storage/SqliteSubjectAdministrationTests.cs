using System.Text.Json;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteSubjectAdministrationTests
{
    [Fact]
    public async Task LoweringReceiptUsesExactInstalledPlanStoreAndSchemaAuthority()
    {
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        BaseExportedSubjectDefinition definition = SubjectDefinition(checksum);
        await using var store = new SqliteRecordStore(new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-subject-receipts",
            DataSource = ":memory:",
            ExportedSubjects = [definition],
        }, NullLoggerFactory.Instance);
        await store.InitializeUnacceptedSchemaForTestsAsync();

        OperationResult<BaseSubjectValidationPlanReceipt[]> result =
            await store.ReadSubjectValidationPlanReceiptsAsync();

        result.IsSuccess().Should().BeTrue();
        BaseSubjectValidationPlanReceipt receipt = result.Value!.Should().ContainSingle().Subject;
        receipt.PlanId.Should().Be(definition.ValidationPlan.Id);
        receipt.PlanVersion.Should().Be(definition.ValidationPlan.Version);
        receipt.PlanChecksum.Should().Be(BaseSubjectContractNormalizer.NormalizePlan(definition.ValidationPlan).Checksum);
        receipt.StoreInstanceId.Should().Be("sqlite-subject-receipts");
        receipt.SchemaGeneration.Should().Be(store.VectorSchemaGeneration);
        receipt.Access.Should().Be(BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys);
        receipt.LoweringFormatVersion.Should().Be(1);
    }

    [Fact]
    public async Task RestorePublishesFreshEpochAndMaxPlusOneGenerationWhilePreservingHistoricalJournal()
    {
        string path = Path.Combine(AdministrationTempDirectory(), $"hpd-base-subject-restore-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 31,
                Key = Enumerable.Repeat((byte)0x31, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        }));
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "sqlite-subject-restore",
            DataSource = path,
            EnableWal = false,
            AdministrationEnabled = true,
            Collections = [collection],
            ExportedSubjects = [SubjectDefinition(checksum)],
            MaxBackupArtifactBytes = 16 * 1024 * 1024,
        };
        try
        {
            await using var store = new SqliteRecordStore(
                options,
                NullLoggerFactory.Instance,
                TimeProvider.System,
                tokenProtector: protector);
            store.AdministrationCapability.Backup.Should().BeTrue(options.DataSource);
            await store.InitializeUnacceptedSchemaForTestsAsync();
            InitializeAuthorityMetadata(path);
            byte[] artifactEpoch = await ReadEpochAsync(path);
            JsonElement reference = Reference(artifactEpoch, 9);
            (await store.CreateAsync(
                collection,
                Create("consumer-one", reference),
                Operation(BaseOperationKind.Create, collection.Id))).IsSuccess().Should().BeTrue();

            var destination = new MemoryStream();
            OperationResult<BaseBackupManifest> backup = await store.CreateBackupAsync(
                destination,
                new BaseBackupRequest { StoreId = options.StoreId, Principal = SystemPrincipal() });
            backup.IsSuccess().Should().BeTrue(backup.Error?.Code);
            BaseBackupManifest manifest = backup.Value!;
            byte[] artifact = destination.ToArray();

            (await store.RotateEpochAsync(Rotation(1))).IsSuccess().Should().BeTrue();
            byte[] rotatedEpoch = await ReadEpochAsync(path);
            rotatedEpoch.Should().NotEqual(artifactEpoch);

            OperationResult<BaseRestoreResult> restored = await store.RestoreAsync(
                new MemoryStream(artifact),
                new BaseRestoreRequest
                {
                    StoreId = options.StoreId,
                    Principal = SystemPrincipal(),
                    ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest,
                    ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest,
                    IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity,
                    RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore,
                    ConfirmDestructiveReplacement = true,
                });

            restored.IsSuccess().Should().BeTrue(restored.Error?.Code);
            byte[] restoredEpoch = await ReadEpochAsync(path);
            restoredEpoch.Should().NotEqual(artifactEpoch);
            restoredEpoch.Should().NotEqual(rotatedEpoch);
            (long generation, long previous, int kind) = await ReadPublicationStateAsync(path);
            generation.Should().Be(3);
            previous.Should().Be(2);
            kind.Should().Be((int)BaseSubjectAuthorityPublicationKind.RestoreTransformation);
            RecordEnvelope current = (await store.GetAsync(
                collection,
                new RecordId("consumer-one"),
                Operation(BaseOperationKind.Get, collection.Id))).Value!;
            current.Payload.Fields!["owner"].GetProperty("authorityEpoch").GetString().Should().Be(Encode(restoredEpoch));
            current.Payload.Fields!["owner"].GetProperty("incarnation").GetString().Should().Be(Encode(Enumerable.Repeat((byte)9, 16).ToArray()));
            current.Metadata.Revision!.Value.Value.Should().NotBe("sqlite:1");
            BaseMutationJournalPage journal = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 16 });
            journal.Entries.Should().HaveCount(3);
            journal.Entries[0].SubjectAuthorityPublication!.Kind.Should().Be(BaseSubjectAuthorityPublicationKind.InitialInstallation);
            journal.Entries[1].Kind.Should().Be(BaseMutationJournalEntryKind.RecordMutation);
            journal.Entries[2].SubjectAuthorityPublication!.Kind.Should().Be(BaseSubjectAuthorityPublicationKind.RestoreTransformation);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
                File.Delete(candidate);
        }
    }

    [Fact]
    public async Task RotationRewritesCurrentReferencesAndPublishesOneAtomicGeneration()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-subject-{Guid.NewGuid():N}.db");
        const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        CollectionDefinition collection = ConsumerCollection(checksum);
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"subject-{Guid.NewGuid():N}",
            DataSource = path,
            EnableWal = false,
            AdministrationEnabled = true,
            Collections = [collection],
            ExportedSubjects = [SubjectDefinition(checksum)],
        };
        try
        {
            await using var store = new SqliteRecordStore(options, NullLoggerFactory.Instance);
            await store.InitializeUnacceptedSchemaForTestsAsync();
            byte[] initialEpoch = await ReadEpochAsync(path);
            JsonElement reference = Reference(initialEpoch, 7);
            OperationResult<RecordEnvelope> created = await store.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("consumer-one"),
                    Payload = new RecordPayload
                    {
                        Kind = RecordPayloadKind.FieldMap,
                        Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["owner"] = reference },
                    },
                },
                new OperationContext { Operation = BaseOperationKind.Create, CollectionId = collection.Id });
            created.IsSuccess().Should().BeTrue();

            OperationResult<BaseSubjectEpochRotationResult> rotated = await store.RotateEpochAsync(
                new BaseSubjectEpochRotationRequest
                {
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ExpectedStateGeneration = 1,
                    DestructiveIntent = "rotate-subject-authority-epoch",
                });

            rotated.IsSuccess().Should().BeTrue();
            rotated.Value!.PreviousStateGeneration.Should().Be(1);
            rotated.Value.PublishedStateGeneration.Should().Be(2);
            rotated.Value.ExaminedRecords.Should().Be(1);
            rotated.Value.RewrittenReferences.Should().Be(1);
            RecordEnvelope current = (await store.GetAsync(
                collection,
                new RecordId("consumer-one"),
                new OperationContext { Operation = BaseOperationKind.Get, CollectionId = collection.Id })).Value!;
            current.Metadata.Revision!.Value.Value.Should().Be("sqlite:2");
            JsonElement rewritten = current.Payload.Fields!["owner"];
            rewritten.GetProperty("authorityEpoch").GetString().Should().NotBe(Encode(initialEpoch));
            rewritten.GetProperty("subjectId").GetString().Should().Be("subject-one");
            rewritten.GetProperty("incarnation").GetString().Should().Be(Encode(Enumerable.Repeat((byte)7, 16).ToArray()));
            BaseMutationJournalPage journal = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 16 });
            journal.Entries.Select(static entry => entry.Kind).Should().Equal(
                BaseMutationJournalEntryKind.SubjectAuthorityPublication,
                BaseMutationJournalEntryKind.RecordMutation,
                BaseMutationJournalEntryKind.SubjectAuthorityPublication);
            journal.Entries[^1].SubjectAuthorityPublication!.PublishedStateGeneration.Should().Be(2);

            OperationResult<BaseSubjectEpochRotationResult> stale = await store.RotateEpochAsync(
                new BaseSubjectEpochRotationRequest
                {
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ExpectedStateGeneration = 1,
                    DestructiveIntent = "rotate-subject-authority-epoch",
                });
            stale.Status.Should().Be(OperationStatus.Conflict);
            stale.Error!.Code.Should().Be(BaseSubjectErrorCodes.SchemaGenerationChanged);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static async ValueTask<byte[]> ReadEpochAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT authority_epoch FROM hpd_base_subject_contracts WHERE contract_id='example.subject' AND contract_version=1;";
        return (byte[])(await command.ExecuteScalarAsync())!;
    }

    private static async ValueTask<(long Generation, long Previous, int Kind)> ReadPublicationStateAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT state_generation,publication_previous_generation,publication_kind FROM hpd_base_subject_contracts WHERE contract_id='example.subject' AND contract_version=1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2));
    }

    private static void InitializeAuthorityMetadata(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
INSERT OR IGNORE INTO hpd_base_schema_identity(singleton,store_instance_id) VALUES (1,'subject-restore-instance');
INSERT OR IGNORE INTO hpd_base_schema_baseline(application_id,store_instance_id,baseline_id,checksum,generation,last_plan_id,applied_at)
VALUES ('subject-restore','subject-restore-instance','baseline-1','checksum-1',1,'plan-1','2026-08-13T00:00:00Z');
""";
        command.ExecuteNonQuery();
    }

    private static JsonElement Reference(byte[] epoch, byte incarnation) => JsonSerializer.Deserialize<JsonElement>($$"""
{"subjectId":"subject-one","authorityEpoch":"{{Encode(epoch)}}","incarnation":"{{Encode(Enumerable.Repeat(incarnation, 16).ToArray())}}"}
""");

    private static RecordCreateRequest Create(string id, JsonElement reference) => new()
    {
        RequestedId = new RecordId(id),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["owner"] = reference },
        },
    };

    private static BaseSubjectEpochRotationRequest Rotation(long generation) => new()
    {
        ContractId = "example.subject",
        ContractVersion = 1,
        ExpectedStateGeneration = generation,
        DestructiveIntent = "rotate-subject-authority-epoch",
    };

    private static OperationContext Operation(BaseOperationKind operation, string collectionId) => new()
    {
        Operation = operation,
        CollectionId = collectionId,
    };

    private static PrincipalContext SystemPrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
    };

    private static string AdministrationTempDirectory()
    {
        string path = Path.GetFullPath(Path.GetTempPath());
        return OperatingSystem.IsMacOS() && path.StartsWith("/var/", StringComparison.Ordinal)
            ? "/private" + path
            : path;
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static CollectionDefinition ConsumerCollection(string checksum) => new()
    {
        Id = "consumer.records",
        Name = "consumer.records",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition
            {
                Id = "consumer.owner",
                ApplicationName = "owner",
                WireName = "owner",
                Type = BaseFieldTypes.Object,
                Required = true,
                Nullable = false,
                SubjectReference = new BaseSubjectReferenceDefinition
                {
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ContractChecksum = checksum,
                    Requirement = BaseSubjectReferenceRequirement.Exists,
                    Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot,
                },
            },
        ],
    };

    private static BaseExportedSubjectDefinition SubjectDefinition(string checksum) => new()
    {
        Id = "example.subject",
        Version = 1,
        OwningModuleId = "example.module",
        SubjectIdKind = BaseSubjectIdKind.OrdinalString,
        MaximumSubjectIdUtf8Bytes = 64,
        Scope = BaseSubjectScopeKind.Global,
        AcquisitionGrantId = "example.subject.acquire",
        ValidationGrantId = "example.subject.validate",
        AdministrationGrantId = "example.subject.admin",
        Audiences = [HPDBaseEndpointAudience.Application],
        ValidationPlan = new BaseSubjectValidationPlanDefinition
        {
            Id = "example.subject.plan",
            Version = 1,
            ContractId = "example.subject",
            ContractVersion = 1,
            ContractChecksum = checksum,
            PrivateCollectionId = "private.subjects",
            SubjectId = BaseSubjectIdBinding.RecordId,
            Active = new BaseSubjectActiveBinding { Kind = BaseSubjectActiveBindingKind.NotDeclared },
            Scope = new BaseSubjectScopeBinding { Kind = BaseSubjectScopeBindingKind.Global },
            Access = BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,
            Limits = BaseSubjectValidationLimits.Default,
        },
    };
}
