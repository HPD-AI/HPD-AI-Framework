using FluentAssertions;
using System.Collections.Immutable;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteStudioControlInspectionTests
{
    [Fact]
    public async Task Activation_receipts_are_exact_bounded_and_canonically_paged()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-studio-{Guid.NewGuid():N}.db");
        try
        {
            var options = new HPDBaseSqliteOptions { DataSource = database, Collections = [SqliteTestFactory.Collection()] };
            await using var store = SqliteTestFactory.Create(options);
            await using (var connection = new SqliteConnection($"Data Source={database}"))
            {
                await connection.OpenAsync();
                for (int index = 1; index <= 3; index++)
                {
                    await using SqliteCommand insert = connection.CreateCommand();
                    insert.CommandText = """
                        INSERT INTO hpd_base_activation_instance_receipts(
                            receipt_key,operation_kind,activation_id,definition_id,definition_version,definition_checksum,
                            receipt_format_version,receipt_duplicate_lifetime_ms,receipt_backup_coverage,
                            fingerprint,result_json,result_checksum,authority_checksum,committed_at,duplicate_resolve_until,
                            receipt_sequence,prior_ordered_checksum,ordered_checksum)
                        VALUES($key,$kind,$subject,'sample.activation',1,$definitionChecksum,1,86400000,1,
                            $fingerprint,$result,$checksum,$authority,1000,86401000,$sequence,$prior,$ordered);
                        """;
                    insert.Parameters.AddWithValue("$key", $"receipt-{index}"); insert.Parameters.AddWithValue("$kind", "activation-completed");
                    insert.Parameters.AddWithValue("$fingerprint", Enumerable.Repeat((byte)index, 32).ToArray());
                    insert.Parameters.AddWithValue("$result", Array.Empty<byte>()); insert.Parameters.AddWithValue("$checksum", Enumerable.Repeat((byte)(index + 3), 32).ToArray());
                    insert.Parameters.AddWithValue("$subject", $"activation-{index}");
                    insert.Parameters.AddWithValue("$definitionChecksum", Enumerable.Repeat((byte)11, 32).ToArray());
                    insert.Parameters.AddWithValue("$authority", Enumerable.Repeat((byte)(index + 7), 32).ToArray());
                    insert.Parameters.AddWithValue("$sequence", index);
                    insert.Parameters.AddWithValue("$prior", Enumerable.Repeat((byte)(index - 1), 32).ToArray());
                    insert.Parameters.AddWithValue("$ordered", Enumerable.Repeat((byte)(index + 12), 32).ToArray());
                    await insert.ExecuteNonQueryAsync();
                }
            }

            IBaseStudioControlInspectionStore inspection = store;
            BaseStudioControlInspectionRequest request = Request(BaseStudioControlFactKind.ActivationReceipt, take: 2);
            OperationResult<BaseStudioControlInspectionPage> first = await inspection.ReadStudioControlFactsAsync(request);
            first.IsSuccess().Should().BeTrue();
            first.Value!.Items.Select(static value => value.Identity).Should().Equal("receipt-1", "receipt-2");
            first.Value.NextIdentity.Should().Be("receipt-2");
            first.Value.Items.Should().OnlyContain(static value => value.FactChecksum.Length == 32);
            BaseStudioControlInspectionContract.IsValidResult(request, first.Value).Should().BeTrue();

            OperationResult<BaseStudioControlInspectionPage> second = await inspection.ReadStudioControlFactsAsync(request with { AfterIdentity = first.Value.NextIdentity });
            second.Value!.Items.Select(static value => value.Identity).Should().Equal("receipt-3");
            second.Value.NextIdentity.Should().BeNull();

            OperationResult<BaseStudioControlInspectionPage> exact = await inspection.ReadStudioControlFactsAsync(request with { Identity = "receipt-2", Take = 1 });
            exact.Value!.Items.Should().ContainSingle().Which.As<BaseStudioActivationReceiptFact>().TransitionKind.Should().Be("activation-completed");
            OperationResult<BaseStudioControlInspectionPage> history = await inspection.ReadStudioControlFactsAsync(request with
            { SubjectKind = "activation", SubjectIdentity = "activation-2" });
            history.Value!.Items.Should().ContainSingle().Which.Identity.Should().Be("receipt-2");
            history.Value.RowsRead.Should().Be(1);

            long exactBytes = exact.Value.EvidenceBytes;
            (await inspection.ReadStudioControlFactsAsync((request with { Identity = "receipt-2", Take = 1,
                Limits = request.Limits with { MaximumEvidenceBytes = exactBytes, MaximumTransientBytes = exactBytes } }))).IsSuccess().Should().BeTrue();
            (await inspection.ReadStudioControlFactsAsync((request with { Identity = "receipt-2", Take = 1,
                Limits = request.Limits with { MaximumEvidenceBytes = exactBytes - 1 } }))).IsSuccess().Should().BeFalse();
        }
        finally { if (File.Exists(database)) File.Delete(database); }
    }

    [Fact]
    public async Task Invalid_or_ambiguous_bounds_fail_before_provider_read()
    {
        await using var store = SqliteTestFactory.Create(); IBaseStudioControlInspectionStore inspection = store;
        (await inspection.ReadStudioControlFactsAsync(Request(BaseStudioControlFactKind.Activation) with { Identity = "one", AfterIdentity = "zero" }))
            .Status.Should().Be(OperationStatus.ValidationFailed);
        (await inspection.ReadStudioControlFactsAsync(Request(BaseStudioControlFactKind.Activation) with { Take = 3,
            Limits = Limits() with { MaximumItems = 2 } })).Status.Should().Be(OperationStatus.ValidationFailed);
        (await inspection.ReadStudioControlFactsAsync(Request(BaseStudioControlFactKind.Activation) with { ProtectedScopeChecksum = [1] }))
            .Status.Should().Be(OperationStatus.ValidationFailed);
    }

    [Theory]
    [InlineData(BaseStudioControlFactKind.LifecycleConsumer)]
    [InlineData(BaseStudioControlFactKind.LifecycleCheckpoint)]
    [InlineData(BaseStudioControlFactKind.RetirementBarrier)]
    public async Task Lifecycle_and_retirement_inspection_families_are_bounded_and_canonical(BaseStudioControlFactKind kind)
    {
        await using var store = SqliteTestFactory.Create(); IBaseStudioControlInspectionStore inspection = store;
        BaseStudioControlInspectionRequest request = Request(kind);
        OperationResult<BaseStudioControlInspectionPage> result = await inspection.ReadStudioControlFactsAsync(request);
        result.IsSuccess().Should().BeTrue("the provider returned {0}", result.Error?.Code); result.Value.Should().NotBeNull();
        BaseStudioControlInspectionContract.IsValidResult(request, result.Value).Should().BeTrue();
    }

    [Fact]
    public void Compound_identity_codec_is_canonical_closed_and_application_bound()
    {
        string atomic = BaseStudioControlInspectionContract.AtomicIdentity("tenant-a", "users.rotate", "request-1");
        BaseStudioControlInspectionContract.TryDecodeAtomicIdentity(atomic, out string scope, out string operation, out string key).Should().BeTrue();
        (scope, operation, key).Should().Be(("tenant-a", "users.rotate", "request-1"));
        BaseStudioControlInspectionContract.TryDecodeAtomicIdentity(atomic + "=", out _, out _, out _).Should().BeFalse();

        string executor = BaseStudioControlInspectionContract.ExecutorIdentity("sample.application", "host-a", "process-a");
        BaseStudioControlInspectionContract.IsValid(Request(BaseStudioControlFactKind.Executor) with { Identity = executor }).Should().BeTrue();
        BaseStudioControlInspectionContract.IsValid(Request(BaseStudioControlFactKind.Executor) with
        { Identity = BaseStudioControlInspectionContract.ExecutorIdentity("other.application", "host-a", "process-a") }).Should().BeFalse();
    }

    [Fact]
    public async Task Dependency_injection_exposes_only_the_provider_neutral_inspection_seam()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddHPDBaseSqliteStore();
        await using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseStudioControlInspectionStore>().Should().BeSameAs(provider.GetRequiredService<SqliteRecordStore>());
    }

    private static BaseStudioControlInspectionRequest Request(BaseStudioControlFactKind kind, int take = 1) => new()
    { ApplicationId = "sample.application", Kind = kind, Take = take, ProtectedScopeChecksum = Enumerable.Repeat((byte)7, 32).ToImmutableArray(), Limits = Limits() };
    private static BaseStudioControlInspectionLimits Limits() => new()
    { MaximumItems = 16, MaximumRowsRead = 17, MaximumEvidenceBytes = 65_536, MaximumTransientBytes = 65_536, Deadline = TimeSpan.FromSeconds(2) };
}
