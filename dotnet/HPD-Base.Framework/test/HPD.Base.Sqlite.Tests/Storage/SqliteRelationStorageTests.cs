using System.Text.Json;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteRelationStorageTests
{
    [Fact]
    public async Task ManyRelationRowsTrackOwningMutationAndRestrictTargetDelete()
    {
        var path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-relations-" + Guid.NewGuid().ToString("N") + ".db");
        CollectionDefinition target = Collection("users") with
        {
            Fields = [new FieldDefinition { Id = "user.name", ApplicationName = "name", WireName = "name", Type = BaseFieldTypes.String, Required = true, Nullable = false }]
        };
        CollectionDefinition source = Collection("projects") with
        {
            Fields =
            [
                new FieldDefinition
                {
                    Id = "project.members", ApplicationName = "members", WireName = "members", Type = BaseFieldTypes.Array,
                    Relation = new RelationDefinition
                    {
                        Id = "project-members", SourceCollectionId = "projects", SourceFieldId = "project.members",
                        TargetCollectionId = "users", LocalMultiplicity = BaseRelationMultiplicity.Many,
                        InverseMultiplicity = BaseRelationMultiplicity.Many, Ordered = true,
                        DeleteBehavior = BaseRelationDeleteBehavior.Restrict
                    }
                }
            ]
        };
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [source, target] });
            (await store.CreateAsync(target, new RecordCreateRequest { RequestedId = new RecordId("u1"), Payload = EmptyPayload() }, Operation(BaseOperationKind.Create, "users", "u1"))).Status.Should().Be(OperationStatus.Created);
            (await store.CreateAsync(target, new RecordCreateRequest { RequestedId = new RecordId("u2"), Payload = EmptyPayload() }, Operation(BaseOperationKind.Create, "users", "u2"))).Status.Should().Be(OperationStatus.Created);
            (await store.CreateAsync(source, new RecordCreateRequest { RequestedId = new RecordId("p1"), Payload = Members("u1", "u2") }, Operation(BaseOperationKind.Create, "projects", "p1"))).Status.Should().Be(OperationStatus.Created);

            (await ReadLinksAsync(path)).Should().Equal(("p1", "u1", 0L), ("p1", "u2", 1L));

            (await store.PatchAsync(source, new RecordId("p1"), new RecordPatchRequest { Patch = Members("u2") }, Operation(BaseOperationKind.Patch, "projects", "p1"))).Status.Should().Be(OperationStatus.Updated);
            (await ReadLinksAsync(path)).Should().Equal(("p1", "u2", 0L));

            var restricted = await store.DeleteAsync(target, new RecordId("u2"), new RecordDeleteRequest(), Operation(BaseOperationKind.Delete, "users", "u2"));
            restricted.Status.Should().Be(OperationStatus.Conflict);
            restricted.Error!.Code.Should().Be("base.relation.deleteRestricted");

            (await store.DeleteAsync(source, new RecordId("p1"), new RecordDeleteRequest(), Operation(BaseOperationKind.Delete, "projects", "p1"))).Status.Should().Be(OperationStatus.Deleted);
            (await ReadLinksAsync(path)).Should().BeEmpty();
            (await store.DeleteAsync(target, new RecordId("u2"), new RecordDeleteRequest(), Operation(BaseOperationKind.Delete, "users", "u2"))).Status.Should().Be(OperationStatus.Deleted);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    [Fact]
    public async Task FailedAtomicRelationWorkLeavesNoRecordsLinksOrJournalEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-sqlite-relation-rollback-" + Guid.NewGuid().ToString("N") + ".db");
        CollectionDefinition target = Collection("users") with
        {
            Fields = [new FieldDefinition { Id = "user.name", ApplicationName = "name", WireName = "name", Type = BaseFieldTypes.String, Required = true, Nullable = false }],
        };
        CollectionDefinition source = Collection("projects") with
        {
            Fields = [new FieldDefinition
            {
                Id = "project.members", ApplicationName = "members", WireName = "members", Type = BaseFieldTypes.Array,
                Relation = new RelationDefinition
                {
                    Id = "project-members", SourceCollectionId = "projects", SourceFieldId = "project.members",
                    TargetCollectionId = "users", LocalMultiplicity = BaseRelationMultiplicity.Many,
                    InverseMultiplicity = BaseRelationMultiplicity.Many, Ordered = true,
                    DeleteBehavior = BaseRelationDeleteBehavior.Restrict,
                },
            }],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = path, Collections = [source, target] });
            RecordMutationExecutionResult result = await store.ExecuteAtomicAsync(
                new RollbackRelationProcessor(source, target),
                new RecordMutationExecutionRequest
                {
                    AcquisitionTimeout = TimeSpan.FromSeconds(1), TransactionTimeout = TimeSpan.FromSeconds(1),
                    CommitCompletionTimeout = TimeSpan.FromSeconds(1),
                });

            result.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
            (await ReadLinksAsync(path)).Should().BeEmpty();
            (await CountAsync(path, PhysicalTable("users"))).Should().Be(0);
            (await CountAsync(path, PhysicalTable("projects"))).Should().Be(0);
            (await CountAsync(path, "hpd_base_mutation_journal")).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static CollectionDefinition Collection(string id) => new()
    {
        Id = id, Name = id, Kind = BaseCollectionKinds.Document, SchemaMode = SchemaMode.Strict,
        UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable
    };

    private static RecordPayload EmptyPayload()
    {
        using JsonDocument document = JsonDocument.Parse("""{"name":"user"}""");
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }
    private static RecordPayload Members(params string[] ids)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { members = ids }));
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }

    private static OperationContext Operation(BaseOperationKind kind, string collection, string record) => new() { Operation = kind, CollectionId = collection, RecordId = record, Now = DateTimeOffset.UnixEpoch };
    private static string RelationTable => "b_r_" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("project-members")))[..32];
    private static string PhysicalTable(string collectionId) => "b_c_" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(collectionId)))[..32];

    private static async Task<long> CountAsync(string path, string table)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<(string Source, string Target, long Ordinal)[]> ReadLinksAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT source_record_id, target_record_id, ordinal FROM {RelationTable} ORDER BY source_record_id, ordinal;";
        var rows = new List<(string, string, long)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        return rows.ToArray();
    }

    private sealed class RollbackRelationProcessor(CollectionDefinition source, CollectionDefinition target) : IAtomicMutationProcessor
    {
        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session, CancellationToken cancellationToken = default)
        {
            OperationResult<RecordMutationSessionResult> user = await session.CreateAsync(
                target,
                new RecordCreateRequest { RequestedId = new RecordId("u1"), Payload = EmptyPayload() },
                Context(BaseRecordMutationKind.Create, "user-event", target.Id), cancellationToken);
            user.IsSuccess().Should().BeTrue(user.Error?.Code);
            OperationResult<RecordMutationSessionResult> project = await session.CreateAsync(
                source,
                new RecordCreateRequest { RequestedId = new RecordId("p1"), Payload = Members("u1") },
                Context(BaseRecordMutationKind.Create, "project-event", source.Id), cancellationToken);
            project.IsSuccess().Should().BeTrue(project.Error?.Code);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed, [],
                new BaseError { Code = "test.rollback", Message = "Rollback requested.", Category = ErrorCategory.Conflict });
        }

        private static RecordMutationSessionContext Context(BaseRecordMutationKind kind, string eventId, string collectionId) => new()
        {
            RequestedOperation = kind, EventId = eventId,
            Operation = new OperationContext { Operation = BaseOperationKind.Create, CollectionId = collectionId, Now = DateTimeOffset.UnixEpoch },
        };
    }
}
