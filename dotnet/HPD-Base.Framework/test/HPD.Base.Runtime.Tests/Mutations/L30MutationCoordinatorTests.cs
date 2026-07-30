using System.Text.Json;
using HPD.Base.Events;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Events;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Stores;
using HPD.Base.Runtime.Tests.Operations;
using HPD.Base.Schema;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Mutations;

public sealed class L30MutationCoordinatorTests
{
    [Fact]
    public async Task SinglesAndStandaloneUpsertUseOnlySingleExecutionBoundary()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);
        var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

        var create = await runtime.CreateAsync(
            "items",
            Create("single"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create));
        var upsert = await runtime.UpsertAsync(
            "items",
            Upsert("upserted"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert));

        Assert.Equal(OperationStatus.Created, create.Status);
        Assert.Equal(RecordUpsertOutcome.Created, upsert.Value?.Outcome);
        Assert.Equal(2, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeOwnedIndependentModeWorksWithoutProviderNonAtomicModes(
        bool advertiseAtomic)
    {
        var store = new FakeRecordStore(
            "primary",
            includeAtomicBatchCapability: advertiseAtomic);
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.OrderedIndependent,
                CreateItem("one", "rec_one"),
                CreateItem("two", "rec_two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BaseRecordBatchOutcome.Committed, result.Value?.Outcome);
        Assert.All(result.Value!.Items, item =>
            Assert.Equal(BaseRecordBatchItemDisposition.Committed, item.Disposition));
        Assert.Equal(2, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task RuntimeOwnedStopModeStopsAfterFirstFailedCommit()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.OrderedStopOnFailure,
                CreateItem("one", "same"),
                CreateItem("two", "same"),
                CreateItem("three", "never")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(BaseRecordBatchOutcome.PartiallyCommitted, result.Value?.Outcome);
        Assert.Equal(
            [
                BaseRecordBatchItemDisposition.Committed,
                BaseRecordBatchItemDisposition.Failed,
                BaseRecordBatchItemDisposition.Skipped
            ],
            result.Value!.Items.Select(item => item.Disposition));
        Assert.Equal(2, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task AtomicModeUsesGroupedBoundaryAndReadsPriorProvisionalWrite()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "rec_atomic"),
                PatchItem("two", "rec_atomic", "updated")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(BaseRecordBatchOutcome.Committed, result.Value?.Outcome);
        Assert.All(result.Value!.Items, item =>
            Assert.Equal(BaseRecordBatchItemDisposition.Committed, item.Disposition));
        Assert.Equal(0, store.SingleExecutionCalls);
        Assert.Equal(1, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task DuplicateItemIdsFailBeforeProviderExecution()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("duplicate", "one"),
                CreateItem("duplicate", "two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchDuplicateItem, result.Error?.Code);
        Assert.Equal(0, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task AtomicPreflightUsesExactRegistrationAndStoreIdentity()
    {
        var first = new FakeRecordStore("same-id");
        var second = new FakeRecordStore("same-id");
        using var provider = BuildTwoCollectionProvider(first, second);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "one", "alpha"),
                CreateItem("two", "two", "beta")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch, "alpha"));

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchMultipleStores, result.Error?.Code);
        Assert.Equal(0, first.AtomicExecutionCalls);
        Assert.Equal(0, second.AtomicExecutionCalls);
    }

    [Fact]
    public async Task UpsertBranchesCommitThroughSameSingleProcessor()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);
        var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

        var created = await runtime.UpsertAsync(
            "items",
            Upsert("target"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert));
        var updated = await runtime.UpsertAsync(
            "items",
            Upsert("target") with { UpdatePayload = Payload(("title", "updated")) },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert));

        Assert.Equal(RecordUpsertOutcome.Created, created.Value?.Outcome);
        Assert.Equal(RecordUpsertOutcome.Updated, updated.Value?.Outcome);
        Assert.Equal("updated", updated.Value!.Record.Payload.Fields!["title"].GetString());
        Assert.Equal(2, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task UpsertPolicyDenialDoesNotRevealExistenceBranch()
    {
        var absent = new FakeRecordStore("absent");
        using var absentProvider = OperationTestServices.Build(absent, new DenyPolicyEvaluator());
        var absentResult = await absentProvider.GetRequiredService<IBaseRecordRuntime>().UpsertAsync(
            "items",
            Upsert("target") with { Condition = RecordUpsertExistenceCondition.UpdateOnly },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert));

        var present = new FakeRecordStore("present");
        present.AddRecord(Record("target", "existing"));
        using var presentProvider = OperationTestServices.Build(present, new DenyPolicyEvaluator());
        var presentResult = await presentProvider.GetRequiredService<IBaseRecordRuntime>().UpsertAsync(
            "items",
            Upsert("target") with { Condition = RecordUpsertExistenceCondition.CreateOnly },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert));

        Assert.Equal(OperationStatus.PolicyDenied, absentResult.Status);
        Assert.Equal(absentResult.Status, presentResult.Status);
        Assert.Equal("base.runtime.policy.denied", absentResult.Error?.Code);
        Assert.Equal(absentResult.Error?.Code, presentResult.Error?.Code);
        Assert.Equal(absentResult.Error?.Message, presentResult.Error?.Message);
    }

    [Fact]
    public async Task ExpectedRevisionAgainstAbsentUpsertIsConflict()
    {
        var store = RevisionStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().UpsertAsync(
            "items",
            Upsert("missing") with { ExpectedRevision = new RevisionToken("rev_1") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert));

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal(BaseMutationErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task IndeterminateAtomicResultIsFailedNullAndNeverObserved()
    {
        var store = new FakeRecordStore("primary")
        {
            ForcedOutcomeAfterProcessing = RecordMutationExecutionOutcome.Indeterminate
        };
        var observer = new CapturingObserver();
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "one")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(OperationStatus.StoreError, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchIndeterminate, result.Error?.Code);
        Assert.False(result.Error!.Store!.Retryable);
        Assert.Null(result.Value);
        Assert.Empty(observer.RecordIds);
    }

    [Fact]
    public async Task AggregateConflictRollsBackEveryItemAndNeverObservesProvisionalWrites()
    {
        var store = new FakeRecordStore("primary")
        {
            ForcedOutcomeAfterProcessing = RecordMutationExecutionOutcome.ConflictRollbackConfirmed,
            ForcedOutcomeError = new BaseError
            {
                Code = "provider.secret",
                Message = "provider-secret-value",
                Category = ErrorCategory.Conflict
            }
        };
        var observer = new CapturingObserver();
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "one"),
                CreateItem("two", "two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value?.Outcome);
        Assert.All(result.Value!.Items, item =>
            Assert.Equal(BaseRecordBatchItemDisposition.RolledBack, item.Disposition));
        Assert.Equal(BaseMutationErrorCodes.TransactionConflict, result.Value.Error?.Code);
        Assert.DoesNotContain("secret", result.Value.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(observer.RecordIds);
    }

    [Fact]
    public async Task ItemFailureRollsBackPriorItemsSkipsLaterItemsAndNeverNotifiesObservers()
    {
        var store = new FakeRecordStore("primary");
        var observer = new CapturingObserver();
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "same"),
                CreateItem("two", "same"),
                CreateItem("three", "later")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value?.Outcome);
        Assert.Equal(
            [
                BaseRecordBatchItemDisposition.RolledBack,
                BaseRecordBatchItemDisposition.Failed,
                BaseRecordBatchItemDisposition.Skipped
            ],
            result.Value!.Items.Select(item => item.Disposition));
        Assert.Empty(observer.RecordIds);
    }

    [Fact]
    public async Task PostCommitNotificationsStayInItemOrderAndContextsAreDistinct()
    {
        var store = new FakeRecordStore("primary");
        var observer = new CapturingObserver();
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "first"),
                CreateItem("two", "second")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch) with { CorrelationId = "aggregate" });

        Assert.Equal(BaseRecordBatchOutcome.Committed, result.Value?.Outcome);
        Assert.Equal(["first", "second"], observer.RecordIds);
        Assert.Equal(2, store.MutationContexts.Count);
        Assert.NotEqual(
            store.MutationContexts[0].CorrelationId,
            store.MutationContexts[1].CorrelationId);
        Assert.All(store.MutationContexts, context =>
            Assert.Equal(DateTimeOffset.UnixEpoch, context.Now));
    }

    [Fact]
    public async Task MalformedProviderFactRollsBackBeforeCommittedObservers()
    {
        var store = new FakeRecordStore("primary")
        {
            MutationFactTransform = fact => fact with
            {
                CommittedOperation = BaseCommittedRecordMutationKind.Delete
            }
        };
        var observer = new CapturingObserver();
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            Create("value"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create));

        Assert.Equal(OperationStatus.StoreError, result.Status);
        Assert.Equal("base.runtime.store.malformedMutationFact", result.Error?.Code);
        Assert.Empty(observer.RecordIds);
    }

    [Fact]
    public async Task ProviderMinimumTimeoutMismatchFailsBeforeWrite()
    {
        var store = new FakeRecordStore(
            "primary",
            minimumTimeout: TimeSpan.FromSeconds(1));
        using var provider = OperationTestServices.Build(
            store,
            configureRuntime: options =>
            {
                options.Mutations.StoreAcquisitionTimeout = TimeSpan.FromMilliseconds(100);
                options.Mutations.MaxTransactionDuration = TimeSpan.FromMilliseconds(100);
                options.Mutations.CommitCompletionTimeout = TimeSpan.FromMilliseconds(100);
            });

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                CreateItem("one", "one")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch));

        Assert.Equal(OperationStatus.CapabilityUnavailable, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchModeUnsupported, result.Error?.Code);
        Assert.Equal(0, store.AtomicExecutionCalls);
        Assert.Equal(0, store.CreateCalls);
    }

    private static FakeRecordStore RevisionStore(string id) => new(
        id,
        revision: new RevisionCapability
        {
            Supported = true,
            Guarantee = RevisionGuarantee.Store,
            Patch = true,
            Replace = true,
            Delete = true
        });

    private static BaseRecordBatchRequest Batch(
        BaseRecordBatchExecutionMode mode,
        params BaseRecordBatchItem[] operations) => new()
    {
        Mode = mode,
        Operations = operations
    };

    private static BaseRecordBatchItem CreateItem(
        string itemId,
        string recordId,
        string collectionId = "items") => new()
    {
        ItemId = itemId,
        CollectionId = collectionId,
        Kind = BaseRecordMutationKind.Create,
        Create = Create(recordId)
    };

    private static BaseRecordBatchItem PatchItem(
        string itemId,
        string recordId,
        string title) => new()
    {
        ItemId = itemId,
        CollectionId = "items",
        Kind = BaseRecordMutationKind.Patch,
        RecordId = new RecordId(recordId),
        Patch = new RecordPatchRequest { Patch = Payload(("title", title)) }
    };

    private static RecordCreateRequest Create(string requestedId) => new()
    {
        RequestedId = new RecordId(requestedId),
        Payload = Payload(("title", requestedId))
    };

    private static RecordUpsertRequest Upsert(string id) => new()
    {
        Id = new RecordId(id),
        CreatePayload = Payload(("title", "created")),
        UpdatePayload = Payload(("title", "updated")),
        UpdateMode = RecordUpsertUpdateMode.Patch,
        Condition = RecordUpsertExistenceCondition.Any
    };

    private static RecordEnvelope Record(string id, string title) => new()
    {
        CollectionId = "items",
        Id = new RecordId(id),
        Payload = Payload(("title", title)),
        Metadata = new RecordMetadata { Revision = new RevisionToken("rev_1") }
    };

    private static RecordPayload Payload(params (string Name, string Value)[] fields) =>
        new()
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = fields.ToDictionary(
                field => field.Name,
                field => Json(field.Value),
                StringComparer.Ordinal)
        };

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private static ServiceProvider BuildTwoCollectionProvider(
        IRecordStore first,
        IRecordStore second)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new TwoCollectionContributor());
        services.AddSingleton<IPolicyEvaluator>(new AllowPolicyEvaluator());
        services.AddHPDBaseRuntime();
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>()
            .RebuildAsync().AsTask().GetAwaiter().GetResult();
        var registry = provider.GetRequiredService<IRecordStoreRegistry>();
        registry.Add(new RecordStoreRegistration
        {
            StoreId = first.Capabilities.StoreId,
            Store = first,
            CollectionIds = ["alpha"]
        });
        registry.Add(new RecordStoreRegistration
        {
            StoreId = second.Capabilities.StoreId,
            Store = second,
            CollectionIds = ["beta"]
        });
        return provider;
    }

    private sealed class CapturingObserver : IBaseCommittedMutationObserver
    {
        public List<string> RecordIds { get; } = [];

        public ValueTask ObserveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordIds.Add(mutation.Resource.RecordId?.Value ?? string.Empty);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TwoCollectionContributor : IBaseDescriptorContributor
    {
        public string Id => "l30-two-collections";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(Collection("alpha"));
            builder.AddCollection(Collection("beta"));
        }

        private static CollectionDefinition Collection(string id) => new()
        {
            Id = id,
            Name = id,
            Kind = BaseCollectionKinds.Document,
            SchemaMode = SchemaMode.Loose,
            UnknownFields = UnknownFieldPolicy.Preserve,
            Operations = new CollectionOperationMatrix
            {
                List = true,
                Get = true,
                Create = true,
                Patch = true,
                Replace = true,
                Delete = true,
                Upsert = true
            }
        };
    }
}
