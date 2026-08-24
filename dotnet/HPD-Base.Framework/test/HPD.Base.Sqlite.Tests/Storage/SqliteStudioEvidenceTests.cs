using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteStudioEvidenceTests
{
    [Fact]
    public async Task Collection_evidence_uses_exact_scope_position_seek_and_honest_page_accounting()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-evidence-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = database,
                Collections = [SqliteTestFactory.Collection("orders")] });
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync(); await using SqliteTransaction transaction = connection.BeginTransaction();
                for (int index = 1; index <= 200; index++)
                    await InsertAsync(connection, transaction, index, BaseSubjectScopeKind.Tenant, "foreign", "orders", $"foreign-{index}");
                await InsertAsync(connection, transaction, 201, BaseSubjectScopeKind.Project, "project-a", "orders", "wanted-a");
                await InsertAsync(connection, transaction, 202, BaseSubjectScopeKind.Project, "project-a", "orders", "wanted-b");
                await transaction.CommitAsync();
            }

            BaseStudioEvidenceRequirement requirement = Requirement(BaseSubjectScopeKind.Project, "project-a");
            OperationResult<BaseStudioEvidencePage> first = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(store, requirement,
                Scope(requirement), new BaseStudioEvidencePageRequest { Take = 1 });
            first.IsSuccess().Should().BeTrue(); first.Value!.Accounting.RowsRead.Should().Be(2);
            first.Value.Items.Should().ContainSingle().Which.Should().BeOfType<BaseStudioRecordMutationEvidenceItem>()
                .Which.EvidenceId.Should().Be("wanted-a");
            first.Value.Next.Should().NotBeNull();

            OperationResult<BaseStudioEvidencePage> second = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(store, requirement,
                Scope(requirement), new BaseStudioEvidencePageRequest { Take = 1, After = first.Value.Next });
            second.IsSuccess().Should().BeTrue(); second.Value!.Accounting.RowsRead.Should().Be(1);
            second.Value.Items.Should().ContainSingle().Which.Should().BeOfType<BaseStudioRecordMutationEvidenceItem>()
                .Which.EvidenceId.Should().Be("wanted-b");

            await using var verify = new SqliteConnection($"Data Source={database}"); await verify.OpenAsync();
            await using SqliteCommand indexes = verify.CreateCommand();
            indexes.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('ix_hpd_base_mutation_journal_scope_collection_position','ix_hpd_base_mutation_journal_scope_record_position');";
            Convert.ToInt32(await indexes.ExecuteScalarAsync()).Should().Be(2);
        }
        finally { File.Delete(database); }
    }

    [Theory]
    [InlineData(BaseSubjectScopeKind.Global, null, "global")]
    [InlineData(BaseSubjectScopeKind.Tenant, "tenant-a", "tenant")]
    [InlineData(BaseSubjectScopeKind.Project, "project-a", "project")]
    public async Task Evidence_publication_and_access_are_exact_for_each_scope(BaseSubjectScopeKind kind, string? value, string expected)
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-evidence-scope-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = database,
                Collections = [SqliteTestFactory.Collection("orders")] });
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync(); await using SqliteTransaction transaction = connection.BeginTransaction();
                await InsertAsync(connection, transaction, 1, BaseSubjectScopeKind.Global, null, "orders", "global");
                await InsertAsync(connection, transaction, 2, BaseSubjectScopeKind.Tenant, "tenant-a", "orders", "tenant");
                await InsertAsync(connection, transaction, 3, BaseSubjectScopeKind.Project, "project-a", "orders", "project");
                await transaction.CommitAsync();
            }
            BaseStudioEvidenceRequirement requirement = Requirement(kind, value);
            OperationResult<BaseStudioEvidencePage> result = await new DefaultBaseStudioEvidenceRuntime().ReadPageAsync(store, requirement,
                Scope(requirement), new BaseStudioEvidencePageRequest { Take = 2 });
            result.IsSuccess().Should().BeTrue();
            result.Value!.Items.Should().ContainSingle().Which.Should().BeOfType<BaseStudioRecordMutationEvidenceItem>()
                .Which.EvidenceId.Should().Be(expected);
        }
        finally { File.Delete(database); }
    }

    private static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, int position,
        BaseSubjectScopeKind kind, string? value, string collection, string eventId)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO hpd_base_mutation_journal(position,entry_kind,event_id,event_type,schema_version,occurred_at,scope_kind,scope_value,operation,visibility,collection_id,record_id) VALUES($position,0,$event,'base.record.updated','1',$at,$kind,$value,1,0,$collection,$record);";
        command.Parameters.AddWithValue("$position", position); command.Parameters.AddWithValue("$event", eventId);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UnixEpoch.AddSeconds(position).ToString("O")); command.Parameters.AddWithValue("$kind", (int)kind);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value); command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$record", "record-" + position); await command.ExecuteNonQueryAsync();
    }

    private static BaseStudioEvidenceRequirement Requirement(BaseSubjectScopeKind kind, string? value) => new()
    {
        ApplicationId = "app", Kind = BaseStudioEvidenceKind.RecordMutation, Scope = new() { Kind = kind, Value = value },
        Parent = new BaseStudioCollectionEvidenceSubject { CollectionId = "orders", InstalledCollectionChecksum = ImmutableArray.CreateRange(new byte[32]) },
        ProtectedScopeSeekChecksum = ImmutableArray.CreateRange(Enumerable.Repeat((byte)7, 32)),
        Limits = new() { MaximumItems = 2, MaximumRowsRead = 3, MaximumIntervals = 1, MaximumEvidenceBytes = 4096,
            MaximumTransientBytes = 4096, AcquisitionDeadline = TimeSpan.FromSeconds(2), SessionDeadline = TimeSpan.FromSeconds(4), PageDeadline = TimeSpan.FromSeconds(2) },
    };
    private static BaseOwnedScopeSeekAuthority Scope(BaseStudioEvidenceRequirement requirement) => new()
    { Kind = requirement.Scope.Kind, ProtectedIndexDigest = requirement.ProtectedScopeSeekChecksum };
}
