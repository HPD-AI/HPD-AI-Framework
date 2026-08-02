using System.Text.Json;
using FluentAssertions;
using HPD.Base.Tests.Application.Generation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.Application.Hosting;

public sealed class SchemaLifecycleAdversarialTests
{
    [Fact]
    public async Task ProviderCannotInventARefinementOrAcceptAnInapplicableAttestation()
    {
        var store = new HostileSchemaStore();
        byte[] key = Enumerable.Repeat((byte)0x64, 32).ToArray();
        await using ServiceProvider provider = Host(store, key);
        IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();

        store.RefinedClassification = BaseSchemaPlanClassification.DataMigrationRequired;
        OperationResult<BaseSchemaPlan> invented = await manager.PlanAsync(new BaseSchemaPlanRequest
        {
            StoreId = store.Capabilities.StoreId,
        });
        invented.Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);

        store.RefinedClassification = null;
        OperationResult<BaseSchemaPlan> inapplicable = await manager.PlanAsync(new BaseSchemaPlanRequest
        {
            StoreId = store.Capabilities.StoreId,
            ExternalMigrationAttestation = new BaseExternalMigrationAttestation
            {
                AttestationId = "attestation",
                ApplicationId = "hostile-schema-app",
                SignerId = "operator",
                StoreId = store.Capabilities.StoreId,
                SourceChecksum = "not-applicable",
                TargetChecksum = "not-applicable",
                CompletedAt = DateTimeOffset.UtcNow,
                Tool = "test",
                ToolVersion = "1",
                AuthenticationTag = new byte[32],
            },
        });
        inapplicable.Error!.Code.Should().Be(BaseSchemaErrorCodes.PlanInvalid);
    }

    [Fact]
    public async Task InvalidAndIndeterminateProviderApplyResultsFailClosedAndRestartRecoversFromObservedState()
    {
        var store = new HostileSchemaStore();
        byte[] key = Enumerable.Repeat((byte)0x63, 32).ToArray();
        await using ServiceProvider provider = Host(store, key);
        IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
        BaseSchemaPlan plan = (await manager.PlanAsync(new BaseSchemaPlanRequest { StoreId = store.Capabilities.StoreId })).Value!;

        store.ReturnInvalidResult = true;
        OperationResult<BaseSchemaApplyResult> invalid = await manager.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact });
        invalid.Error!.Code.Should().Be(BaseSchemaErrorCodes.MigrationFailed);
        invalid.Error.Message.Should().NotContain("secret-provider-value");
        store.State.Generation.Should().Be(0);
        store.State.AcceptedBaselineId.Should().BeNull();

        store.ReturnInvalidResult = false;
        store.BlockApply = true;
        Task<OperationResult<BaseSchemaApplyResult>> pending = manager.ApplyAsync(
            new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact }).AsTask();
        await store.ApplyStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        OperationResult<BaseSchemaApplyResult> indeterminate = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        indeterminate.Error!.Code.Should().Be(BaseSchemaErrorCodes.MigrationIndeterminate);
        store.State.Generation.Should().Be(0);
        store.State.AcceptedBaselineId.Should().BeNull("an indeterminate call cannot publish a guessed baseline");

        store.CompleteApply();
        await store.ApplyCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        OperationResult<BaseSchemaObservedState> recovered = await manager.VerifyAsync(new BaseSchemaVerifyRequest { StoreId = store.Capabilities.StoreId });
        recovered.Value!.Generation.Should().Be(1);
        recovered.Value.AcceptedChecksum.Should().Be(plan.TargetChecksum);

        await using ServiceProvider restarted = Host(store, key);
        OperationResult<BaseApplicationReadiness> readiness = await restarted.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        readiness.Value!.State.Should().Be(BaseApplicationReadinessState.Ready);
        readiness.Value.SchemaGeneration.Should().Be(1);
    }

    private static ServiceProvider Host(HostileSchemaStore store, byte[] key)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .ConfigureSchema(options =>
            {
                options.ApplicationId = "hostile-schema-app";
                options.PlanProtectionKey = key;
                options.MaxApplyDuration = TimeSpan.FromSeconds(1);
                options.CommitCompletionTimeout = TimeSpan.FromMilliseconds(10);
            })
            .AddCollection(GeneratedProject.Collection)
            .Use(new HostileSchemaInstaller(store)));
        return services.BuildServiceProvider();
    }
}

internal sealed class HostileSchemaInstaller(HostileSchemaStore store) : IHPDBaseBuilderExtension
{
    public string Id => store.Capabilities.StoreId;
    public bool IsRecordProvider => true;
    public bool SupportsRequiredIndexes => true;
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
    {
        services.AddSingleton(store);
        services.AddSingleton<IBaseDescriptorContributor>(new HostileSchemaDescriptorContributor(collections, Id));
    }
    public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        services.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = Id, Store = store, CollectionIds = [GeneratedProject.Collection.Id],
        });
        return ValueTask.CompletedTask;
    }
}

internal sealed class HostileSchemaDescriptorContributor(IReadOnlyList<CollectionDefinition> collections, string storeId) : IBaseDescriptorContributor
{
    public string Id => "hostile-schema-descriptor";
    public void Contribute(IBaseDescriptorContributionBuilder builder)
    {
        foreach (CollectionDefinition collection in collections)
            builder.AddCollection(collection with { Store = new StoreAnnotation { StoreId = storeId, Owner = EnforcementOwner.Store } });
    }
}

internal sealed class HostileSchemaStore() : FakeRecordStore("hostile-schema"), IBaseSchemaStore
{
    private readonly TaskCompletionSource<OperationResult<BaseSchemaApplyResult>> _apply =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BaseSchemaProviderApplyRequest? _request;

    public BaseSchemaExecutionCapability SchemaExecution { get; } = new()
    {
        Inspect = true, Prepare = true, Apply = true, History = true,
        Classifications = [BaseSchemaPlanClassification.SafeStructural, BaseSchemaPlanClassification.NoChanges],
    };
    public BaseSchemaObservedState State { get; private set; } = new()
    {
        StoreId = "hostile-schema", Generation = 0, Compatibility = BaseSchemaCompatibility.MigrationRequired,
        Assets = [], MigrationState = BaseSchemaMigrationState.None,
    };
    public bool ReturnInvalidResult { get; set; }
    public BaseSchemaPlanClassification? RefinedClassification { get; set; }
    public bool BlockApply { get; set; }
    public TaskCompletionSource ApplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ApplyCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<OperationResult<BaseSchemaObservedState>> InspectSchemaAsync(
        BaseSchemaInspectionRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OperationResults.Ok(State));

    public ValueTask<OperationResult<BaseSchemaPreparedPlan>> PrepareSchemaPlanAsync(
        BaseSchemaPreparationRequest request, CancellationToken cancellationToken = default)
    {
        byte[] artifact = "secret-provider-ddl"u8.ToArray();
        return ValueTask.FromResult(OperationResults.Ok(new BaseSchemaPreparedPlan
        {
            RefinedClassification = RefinedClassification,
            SafePhysicalSummary = request.LogicalDelta.Select(operation => new BaseSchemaSafePhysicalSummary
            {
                LogicalId = operation.LogicalId, Summary = "prepared",
            }).ToArray(),
            ProviderId = "hostile", ProviderVersion = "1", PlannerVersion = "1",
            PersistedStoreInstanceId = "hostile-instance", ProviderApplyArtifact = artifact,
            ProviderApplyArtifactDigest = DefaultBaseSchemaPlanProtector.Digest(artifact),
        }));
    }

    public ValueTask<OperationResult<BaseSchemaApplyResult>> ApplySchemaAsync(
        BaseSchemaProviderApplyRequest request, CancellationToken cancellationToken = default)
    {
        _request = request;
        ApplyStarted.TrySetResult();
        if (ReturnInvalidResult)
            return ValueTask.FromResult(OperationResults.Ok(new BaseSchemaApplyResult
            {
                Outcome = BaseSchemaApplyOutcome.Applied, Generation = 99, BaselineId = "wrong",
                Checksum = "secret-provider-value", State = BaseSchemaMigrationState.Ready,
            }));
        return BlockApply
            ? new ValueTask<OperationResult<BaseSchemaApplyResult>>(_apply.Task)
            : ValueTask.FromResult(Complete());
    }

    public void CompleteApply()
    {
        _apply.TrySetResult(Complete());
        ApplyCompleted.TrySetResult();
    }

    private OperationResult<BaseSchemaApplyResult> Complete()
    {
        BaseSchemaProviderApplyRequest request = _request!;
        BaseSchemaProviderVerifiedEnvelope envelope = JsonSerializer.Deserialize(
            request.VerifiedPlanEnvelope, HPDBaseJsonSerializerContext.Default.BaseSchemaProviderVerifiedEnvelope)!;
        State = new BaseSchemaObservedState
        {
            StoreId = Capabilities.StoreId, PersistedStoreInstanceId = "hostile-instance",
            AcceptedBaselineId = envelope.TargetBaselineId, AcceptedChecksum = request.ExpectedTargetChecksum,
            Generation = request.ExpectedGeneration + 1, Compatibility = BaseSchemaCompatibility.Compatible,
            Assets = [], MigrationState = BaseSchemaMigrationState.Ready, LastAppliedPlanId = envelope.PlanId,
        };
        return OperationResults.Ok(new BaseSchemaApplyResult
        {
            Outcome = BaseSchemaApplyOutcome.Applied, Generation = State.Generation,
            BaselineId = envelope.TargetBaselineId, Checksum = request.ExpectedTargetChecksum,
            State = BaseSchemaMigrationState.Ready,
        });
    }

    public ValueTask<OperationResult<BaseSchemaHistoryPage>> ReadSchemaHistoryAsync(
        BaseSchemaHistoryRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OperationResults.Ok(new BaseSchemaHistoryPage { Items = [] }));
}
