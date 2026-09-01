using System.Collections.Immutable;
using FluentAssertions;
using HPD.Base;
using Microsoft.Data.Sqlite;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteStudioInfrastructureInventoryTests
{
    [Fact]
    public async Task Authoritative_maintenance_publication_is_visible_with_exact_state_and_identity()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-infra-publication-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = database, StoreId = "infra" });
            _ = await store.CaptureInfrastructureAuthorityAsync(Requirement());
            await store.PublishInfrastructureMaintenanceAsync("vectorRebuild", "collection:index:2",
                BaseStudioInfrastructureState.Completed, 10_000, CancellationToken.None);
            BaseStudioInfrastructureInventoryRequirement requirement = Requirement() with
            { Kind = BaseStudioInfrastructureInventoryKind.Maintenance };
            OperationResult<BaseCapturedStudioInfrastructureAuthority> captured = await store.CaptureInfrastructureAuthorityAsync(requirement);
            captured.IsSuccess().Should().BeTrue(captured.Error?.Code);
            OperationResult<IBaseStudioInfrastructureInventorySession> opened = await store.OpenInfrastructureSessionAsync(captured.Value!);
            await using IBaseStudioInfrastructureInventorySession session = opened.Value!;
            BaseStudioInfrastructurePage page = (await session.ReadPageAsync(new() { Take = 1 })).Value!;
            BaseStudioMaintenanceItem item = page.Items.Should().ContainSingle().Which.Should().BeOfType<BaseStudioMaintenanceItem>().Subject;
            item.MaintenanceKind.Should().Be("vectorRebuild"); item.OperationIdentity.Should().Be("collection:index:2");
            item.State.Should().Be(BaseStudioInfrastructureState.Completed); item.ProgressBasisPoints.Should().Be(10_000);
        }
        finally { File.Delete(database); }
    }

    [Fact]
    public async Task Schema_inventory_is_durable_indexed_and_cursor_substitution_is_rejected()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-infra-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = database, StoreId = "infra" });
            OperationResult<BaseCapturedStudioInfrastructureAuthority> captured = await store.CaptureInfrastructureAuthorityAsync(Requirement());
            captured.IsSuccess().Should().BeTrue(captured.Error?.Code);
            OperationResult<IBaseStudioInfrastructureInventorySession> opened = await store.OpenInfrastructureSessionAsync(captured.Value!);
            await using IBaseStudioInfrastructureInventorySession session = opened.Value!;
            OperationResult<BaseStudioInfrastructurePage> page = await session.ReadPageAsync(new() { Take = 1 });
            page.IsSuccess().Should().BeTrue();
            page.Value!.Items.Should().ContainSingle().Which.Should().BeOfType<BaseStudioSchemaGenerationItem>();

            OperationResult<BaseStudioInfrastructurePage> hostile = await session.ReadPageAsync(new()
            {
                Take = 1,
                After = new BaseStudioInfrastructureBoundary { Kind = BaseStudioInfrastructureInventoryKind.Backup, Sequence = 1, Checksum = ImmutableArray.CreateRange(new byte[32]) },
            });
            hostile.Status.Should().Be(OperationStatus.PolicyDenied);

            await using var connection = new SqliteConnection($"Data Source={database}"); await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_hpd_base_studio_infrastructure_inventory_kind_sequence';";
            Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
        }
        finally { File.Delete(database); }
    }

    [Fact]
    public async Task Migration_inventory_decodes_persisted_hex_checksum_to_exact_digest_bytes()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-infra-migration-{Guid.NewGuid():N}.db");
        try
        {
            await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions { DataSource = database, StoreId = "infra" });
            _ = await store.CaptureInfrastructureAuthorityAsync(Requirement());
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync(); await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
INSERT INTO hpd_base_schema_history
(application_id,generation,baseline_id,checksum,plan_id,classification,outcome,provider_version,structural_verification,external_data_migration,semantic_conversion,external_attestation_id,external_signer_id,applied_at)
VALUES('app',1,'baseline','0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef','plan-1',0,1,'test',1,0,0,NULL,NULL,'2026-01-01T00:00:00.0000000Z');
""";
                _ = await command.ExecuteNonQueryAsync();
            }
            BaseStudioInfrastructureInventoryRequirement requirement = Requirement() with { Kind = BaseStudioInfrastructureInventoryKind.Migration, SchemaGeneration = 0 };
            OperationResult<BaseCapturedStudioInfrastructureAuthority> captured = await store.CaptureInfrastructureAuthorityAsync(requirement);
            captured.IsSuccess().Should().BeTrue(captured.Error?.Code);
            OperationResult<IBaseStudioInfrastructureInventorySession> opened = await store.OpenInfrastructureSessionAsync(captured.Value!);
            await using IBaseStudioInfrastructureInventorySession session = opened.Value!;
            BaseStudioMigrationItem item = (await session.ReadPageAsync(new() { Take = 1 })).Value!.Items.Should().ContainSingle()
                .Which.Should().BeOfType<BaseStudioMigrationItem>().Subject;
            item.PlanChecksum.Should().Equal(Convert.FromHexString("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
        }
        finally { File.Delete(database); }
    }

    private static BaseStudioInfrastructureInventoryRequirement Requirement() => new()
    {
        ApplicationId = "app", StoreId = "infra", StoreInstanceId = "infra", RestoreEpoch = 0, SchemaGeneration = 0,
        Kind = BaseStudioInfrastructureInventoryKind.SchemaGeneration,
        Limits = new BaseStudioInfrastructureInventoryLimits
        {
            MaximumItems = 1, MaximumRowsRead = 2, MaximumEvidenceBytes = 4096, MaximumTransientBytes = 4096,
            AcquisitionDeadline = TimeSpan.FromSeconds(2), SessionDeadline = TimeSpan.FromSeconds(4), PageDeadline = TimeSpan.FromSeconds(2),
        },
    };
}
