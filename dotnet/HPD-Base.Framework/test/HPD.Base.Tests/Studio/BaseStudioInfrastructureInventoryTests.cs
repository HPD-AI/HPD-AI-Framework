using System.Collections.Immutable;
using Xunit;

namespace HPD.Base.Tests.Studio;

public sealed class BaseStudioInfrastructureInventoryTests
{
    [Fact]
    public async Task InMemory_authority_is_exact_provider_bound_and_single_use()
    {
        var first = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "infra" });
        var second = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "infra" });
        OperationResult<BaseCapturedStudioInfrastructureAuthority> captured =
            await first.CaptureInfrastructureAuthorityAsync(Requirement());

        Assert.True(captured.IsSuccess());
        Assert.Equal(OperationStatus.PolicyDenied, (await second.OpenInfrastructureSessionAsync(captured.Value!)).Status);
        Assert.True((await first.OpenInfrastructureSessionAsync(captured.Value!)).IsSuccess());
        Assert.Equal(OperationStatus.PolicyDenied, (await first.OpenInfrastructureSessionAsync(captured.Value!)).Status);
    }

    [Fact]
    public async Task InMemory_rejects_substituted_authority_and_cursor_kind()
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions { StoreId = "infra" });
        Assert.Equal(OperationStatus.PolicyDenied,
            (await store.CaptureInfrastructureAuthorityAsync(Requirement() with { RestoreEpoch = 1 })).Status);

        var captured = await store.CaptureInfrastructureAuthorityAsync(Requirement());
        var opened = await store.OpenInfrastructureSessionAsync(captured.Value!);
        await using IBaseStudioInfrastructureInventorySession session = opened.Value!;
        OperationResult<BaseStudioInfrastructurePage> page = await session.ReadPageAsync(new()
        {
            Take = 1,
            After = new BaseStudioInfrastructureBoundary
            {
                Kind = BaseStudioInfrastructureInventoryKind.Backup,
                Sequence = 1,
                Checksum = new byte[32].ToImmutableArray(),
            },
        });
        Assert.Equal(OperationStatus.PolicyDenied, page.Status);
    }

    private static BaseStudioInfrastructureInventoryRequirement Requirement() => new()
    {
        ApplicationId = "app", StoreId = "infra", StoreInstanceId = "infra", RestoreEpoch = 0, SchemaGeneration = 1,
        Kind = BaseStudioInfrastructureInventoryKind.SchemaGeneration,
        Limits = new BaseStudioInfrastructureInventoryLimits
        {
            MaximumItems = 1, MaximumRowsRead = 2, MaximumEvidenceBytes = 4096, MaximumTransientBytes = 4096,
            AcquisitionDeadline = TimeSpan.FromSeconds(1), SessionDeadline = TimeSpan.FromSeconds(2), PageDeadline = TimeSpan.FromSeconds(1),
        },
    };
}
