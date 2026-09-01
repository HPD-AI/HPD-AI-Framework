using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Operations;

public sealed class L30MutationPipelineTests
{
    [Fact]
    public async Task OrderedIndependentContinuesAfterFailure()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(Record("items", "duplicate", "existing"));
        using var provider = OperationTestServices.Build(store);

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.OrderedIndependent,
                Create("first", "items", "duplicate", "conflict"),
                Create("second", "items", "created", "second")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BaseRecordBatchOutcome.PartiallyCommitted, result.Value!.Outcome);
        Assert.Equal(
            [BaseRecordBatchItemDisposition.Failed, BaseRecordBatchItemDisposition.Committed],
            result.Value.Items.Select(static item => item.Disposition));
        Assert.Equal(2, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
        Assert.Equal(OperationStatus.Ok, (await Get(store, "created")).Status);
    }

    [Fact]
    public async Task OrderedStopOnFailureSkipsLaterItems()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(Record("items", "duplicate", "existing"));
        using var provider = OperationTestServices.Build(store);

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.OrderedStopOnFailure,
                Create("first", "items", "duplicate", "conflict"),
                Create("second", "items", "not-created", "second")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(BaseRecordBatchOutcome.Failed, result.Value!.Outcome);
        Assert.Equal(
            [BaseRecordBatchItemDisposition.Failed, BaseRecordBatchItemDisposition.Skipped],
            result.Value.Items.Select(static item => item.Disposition));
        Assert.Equal(BaseMutationErrorCodes.BatchSkipped, result.Value.Items[1].Error!.Code);
        Assert.Equal(1, store.SingleExecutionCalls);
        Assert.Equal(OperationStatus.NotFound, (await Get(store, "not-created")).Status);
    }

    [Fact]
    public async Task AtomicModeUsesOneAtomicBoundaryAndPreservesOrder()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                Create("first", "items", "a", "one"),
                Create("second", "items", "b", "two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(BaseRecordBatchOutcome.Committed, result.Value!.Outcome);
        Assert.All(result.Value.Items, item =>
            Assert.Equal(BaseRecordBatchItemDisposition.Committed, item.Disposition));
        Assert.Equal(["first", "second"], result.Value.Items.Select(static item => item.ItemId));
        Assert.Equal(1, store.AtomicExecutionCalls);
        Assert.Equal(0, store.SingleExecutionCalls);
        BaseAtomicMutationProjectionRequest projection = Assert.IsType<BaseAtomicMutationProjectionRequest>(store.LastProjectionRequest);
        Assert.Equal(2, projection.Mutations.Length);
        Assert.Equal(["first", "second"], projection.Mutations.Select(static mutation => mutation.ItemId));
    }

    [Fact]
    public async Task AtomicItemFailureMapsPriorProvisionalAndLaterUnexecutedItems()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(Record("items", "duplicate", "existing"));
        var observer = new CapturingObserver();
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                Create("first", "items", "provisional", "one"),
                Create("second", "items", "duplicate", "conflict"),
                Create("third", "items", "never-run", "three")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value!.Outcome);
        Assert.Equal(
            [
                BaseRecordBatchItemDisposition.RolledBack,
                BaseRecordBatchItemDisposition.Failed,
                BaseRecordBatchItemDisposition.Skipped
            ],
            result.Value.Items.Select(static item => item.Disposition));
        Assert.Equal(BaseMutationErrorCodes.BatchRolledBack, result.Value.Items[0].Error!.Code);
        Assert.Equal(BaseMutationErrorCodes.BatchSkipped, result.Value.Items[2].Error!.Code);
        Assert.Empty(observer.Events);
        Assert.Equal(OperationStatus.NotFound, (await Get(store, "provisional")).Status);
    }

    [Fact]
    public async Task DuplicateItemIdsFailPreflightWithoutEnteringStore()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                Create("same", "items", "a", "one"),
                Create("same", "items", "b", "two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchDuplicateItem, result.Error!.Code);
        Assert.Null(result.Value);
        Assert.Equal(0, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task AtomicCrossStoreRequestFailsExactRoutingPreflight()
    {
        var first = new FakeRecordStore("first");
        var second = new FakeRecordStore("second");
        using var provider = BuildTwoStoreProvider(first, second);

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                Create("one", "first-items", "a", "one"),
                Create("two", "second-items", "b", "two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch, string.Empty),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchMultipleStores, result.Error!.Code);
        Assert.Equal(0, first.AtomicExecutionCalls);
        Assert.Equal(0, second.AtomicExecutionCalls);
    }

    [Fact]
    public async Task UpsertReportsCreateThenPatchBranchesThroughSingleBoundary()
    {
        var store = RevisionStore();
        using var provider = OperationTestServices.Build(store);

        var created = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("upserted", "created", "updated"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);
        var updated = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("upserted", "unused", "patched"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);

        Assert.Equal(RecordUpsertOutcome.Created, created.Value!.Outcome);
        Assert.Equal(RecordUpsertOutcome.Updated, updated.Value!.Outcome);
        Assert.Equal("patched", updated.Value.Record.Payload.Fields!["title"].GetString());
        Assert.Equal(2, store.SingleExecutionCalls);
        Assert.Equal(0, store.AtomicExecutionCalls);
    }

    [Fact]
    public async Task UpsertEnforcesExistenceAndRevisionPreconditions()
    {
        var store = RevisionStore();
        store.AddRecord(Record("items", "present", "existing", "rev_1"));
        using var provider = OperationTestServices.Build(store);

        var createOnly = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("present", "create", "update") with
            {
                Condition = RecordUpsertExistenceCondition.CreateOnly
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);
        var updateOnly = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("absent", "create", "update") with
            {
                Condition = RecordUpsertExistenceCondition.UpdateOnly
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);
        var revision = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("present", "create", "update") with
            {
                ExpectedRevision = new RevisionToken("stale")
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Conflict, createOnly.Status);
        Assert.Equal(BaseMutationErrorCodes.UpsertPreconditionFailed, createOnly.Error!.Code);
        Assert.Equal(OperationStatus.NotFound, updateOnly.Status);
        Assert.Equal(BaseMutationErrorCodes.UpsertPreconditionFailed, updateOnly.Error!.Code);
        Assert.Equal(OperationStatus.Conflict, revision.Status);
        Assert.Equal(BaseMutationErrorCodes.RevisionConflict, revision.Error!.Code);
    }

    [Fact]
    public async Task UpsertPolicyDenialDoesNotRevealExistenceBranch()
    {
        var store = RevisionStore();
        store.AddRecord(Record("items", "present", "existing", "rev_1"));
        using var provider = OperationTestServices.Build(store, new DenyPolicyEvaluator());

        var absent = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("absent", "create", "update"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);
        var present = await Runtime(provider).UpsertAsync(
            "items",
            Upsert("present", "create", "update"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);

        Assert.Equal(absent.Status, present.Status);
        Assert.Equal(OperationStatus.PolicyDenied, absent.Status);
        Assert.Equal(absent.Error!.Code, present.Error!.Code);
        Assert.Equal(absent.Error.Message, present.Error.Message);
        Assert.Null(absent.Value);
        Assert.Null(present.Value);
    }

    [Fact]
    public async Task IndeterminateAtomicCommitIsNonRetryableOuterFailureWithoutValue()
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

        var result = await Runtime(provider).BatchAsync(
            Batch(BaseRecordBatchExecutionMode.Atomic, Create("one", "items", "uncertain", "one")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.StoreError, result.Status);
        Assert.Equal(BaseMutationErrorCodes.BatchIndeterminate, result.Error!.Code);
        Assert.False(result.Error.Store!.Retryable);
        Assert.Null(result.Value);
        Assert.Empty(observer.Events);
        Assert.Equal(OperationStatus.NotFound, (await Get(store, "uncertain")).Status);
    }

    [Fact]
    public async Task AggregateGenerationConflictMapsEveryItemRolledBack()
    {
        var store = new FakeRecordStore("primary")
        {
            ForcedOutcomeAfterProcessing = RecordMutationExecutionOutcome.ConflictRollbackConfirmed
        };
        using var provider = OperationTestServices.Build(store);

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                Create("one", "items", "a", "one"),
                Create("two", "items", "b", "two")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Ok, result.Status);
        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value!.Outcome);
        Assert.Equal(BaseMutationErrorCodes.TransactionConflict, result.Value.Error!.Code);
        Assert.All(result.Value.Items, item =>
            Assert.Equal(BaseRecordBatchItemDisposition.RolledBack, item.Disposition));
        Assert.All(result.Value.Items, item =>
            Assert.Equal(BaseMutationErrorCodes.BatchRolledBack, item.Error!.Code));
    }

    [Fact]
    public async Task PostCommitEventsAreOrderedRedactedAndObserveCommittedState()
    {
        var store = new FakeRecordStore("primary");
        var publisher = new OrderedPublisher();
        var observer = new CommittedStateObserver(store);
        using var provider = OperationTestServices.Build(
            store,
            new ConstrainedPolicyEvaluator(readMask: new FieldMask
            {
                Mode = FieldMaskMode.IncludeOnly,
                Include = ["title"]
            }),
            fields:
            [
                new FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String },
                new FieldDefinition { Id = "secret", ApplicationName = "secret", WireName = "secret", Type = BaseFieldTypes.String }
            ],
            configureServices: services =>
            {
                services.AddSingleton<IBaseEventPublisher>(publisher);
                services.AddSingleton<IBaseCommittedMutationObserver>(observer);
            });

        var result = await Runtime(provider).BatchAsync(
            Batch(
                BaseRecordBatchExecutionMode.Atomic,
                Create("one", "items", "a", "one", "secret-a"),
                Create("two", "items", "b", "two", "secret-b")),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Batch),
            CancellationToken.None);

        Assert.Equal(BaseRecordBatchOutcome.Committed, result.Value!.Outcome);
        Assert.Equal(["a", "b"], publisher.Events.Select(static item => item.Resource.RecordId!.Value.Value));
        Assert.Equal(["a", "b"], observer.RecordIds);
        Assert.All(observer.Statuses, status => Assert.Equal(OperationStatus.Ok, status));
        Assert.All(publisher.Events, item =>
        {
            Assert.True(item.After!.Redacted);
            Assert.Equal(["title"], item.After.Payload!.Fields!.Keys);
        });
    }

    [Fact]
    public async Task ProviderSessionCannotBeUsedAfterExecutionReturns()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await Runtime(provider).CreateAsync(
            "items",
            CreateRequest("session", "value"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.LastSession!.GetAsync(
                Collection("items"),
                RecordId.Create("session"),
                RuntimeTestData.Operation(BaseOperationKind.Get),
                CancellationToken.None));
    }

    [Fact]
    public async Task UnexpectedPostCommitFailurePreservesCommittedStandaloneValues()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services =>
                services.AddSingleton<IBaseEventFactory, ThrowingEventFactory>());
        var runtime = Runtime(provider);

        var created = await runtime.CreateAsync(
            "items",
            CreateRequest("created", "value"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, created.Status);
        Assert.Equal("created", created.Value!.Id.Value);
        Assert.Empty(created.Value.Payload.Fields!);
        Assert.True(created.Value.Policy!.Redacted);
        Assert.Contains(created.Warnings!, warning =>
            warning.Code == "base.runtime.events.postCommitFailed");

        var upserted = await runtime.UpsertAsync(
            "items",
            Upsert("upserted", "created", "updated"),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Upsert),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, upserted.Status);
        Assert.Equal(RecordUpsertOutcome.Created, upserted.Value!.Outcome);
        Assert.Equal("upserted", upserted.Value.Record.Id.Value);
        Assert.Empty(upserted.Value.Record.Payload.Fields!);
        Assert.Contains(upserted.Warnings!, warning =>
            warning.Code == "base.runtime.events.postCommitFailed");

        var deleted = await runtime.DeleteAsync(
            "items",
            RecordId.Create("created"),
            new RecordDeleteRequest { ReturnPrevious = true },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Deleted, deleted.Status);
        Assert.True(deleted.Value!.Deleted);
        Assert.Equal("created", deleted.Value.Id.Value);
        Assert.Null(deleted.Value.Previous);
        Assert.Contains(deleted.Warnings!, warning =>
            warning.Code == "base.runtime.events.postCommitFailed");
        Assert.Equal(OperationStatus.NotFound, (await Get(store, "created")).Status);
    }

    private static IBaseRecordRuntime Runtime(ServiceProvider provider) =>
        provider.GetRequiredService<IBaseRecordRuntime>();

    private static BaseRecordBatchRequest Batch(
        BaseRecordBatchExecutionMode mode,
        params BaseRecordBatchItem[] items) => new()
    {
        Mode = mode,
        Operations = items
    };

    private static BaseRecordBatchItem Create(
        string itemId,
        string collectionId,
        string recordId,
        string title,
        string? secret = null) => new()
    {
        ItemId = itemId,
        CollectionId = collectionId,
        Kind = BaseRecordMutationKind.Create,
        Create = CreateRequest(recordId, title, secret)
    };

    private static RecordCreateRequest CreateRequest(
        string recordId,
        string title,
        string? secret = null) => new()
    {
        RequestedId = RecordId.Create(recordId),
        Payload = Payload(title, secret)
    };

    private static RecordUpsertRequest Upsert(
        string id,
        string createTitle,
        string updateTitle) => new()
    {
        Id = RecordId.Create(id),
        CreatePayload = Payload(createTitle),
        UpdatePayload = Payload(updateTitle),
        UpdateMode = RecordUpsertUpdateMode.Patch,
        Condition = RecordUpsertExistenceCondition.Any
    };

    private static RecordPayload Payload(string title, string? secret = null)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["title"] = Json(title)
        };
        if (secret is not null)
            fields["secret"] = Json(secret);
        return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return document.RootElement.Clone();
    }

    private static RecordEnvelope Record(
        string collectionId,
        string id,
        string title,
        string? revision = null) => new()
    {
        CollectionId = collectionId,
        Id = RecordId.Create(id),
        Payload = Payload(title),
        Metadata = new RecordMetadata
        {
            Revision = revision is null ? null : new RevisionToken(revision)
        }
    };

    private static CollectionDefinition Collection(string id) => new()
    {
        Id = id,
        Name = id,
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static ValueTask<OperationResult<RecordEnvelope>> Get(
        FakeRecordStore store,
        string id) =>
        store.GetAsync(
            Collection("items"),
            RecordId.Create(id),
            RuntimeTestData.Operation(BaseOperationKind.Get),
            CancellationToken.None);

    private static FakeRecordStore RevisionStore() => new(
        "primary",
        revision: new RevisionCapability
        {
            Supported = true,
            Guarantee = RevisionGuarantee.Store,
            Patch = true,
            Replace = true,
            Delete = true
        });

    private static ServiceProvider BuildTwoStoreProvider(
        FakeRecordStore first,
        FakeRecordStore second)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(
            new MultiCollectionContributor("first-items", "second-items"));
        services.AddHPDBaseRuntime().UseTestPolicyAuthority(new AllowPolicyEvaluator());
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>()
            .RebuildAsync().AsTask().GetAwaiter().GetResult();
        var registry = provider.GetRequiredService<IRecordStoreRegistry>();
        registry.Add(new RecordStoreRegistration
        {
            StoreId = first.Capabilities.StoreId,
            Store = first,
            CollectionIds = ["first-items"]
        });
        registry.Add(new RecordStoreRegistration
        {
            StoreId = second.Capabilities.StoreId,
            Store = second,
            CollectionIds = ["second-items"]
        });
        return provider;
    }

    private sealed class MultiCollectionContributor(params string[] ids)
        : IBaseDescriptorContributor
    {
        public string Id => "l30-multiple-collections";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            foreach (var id in ids)
            {
                builder.AddCollection(Collection(id) with
                {
                    MutationMode = BaseCollectionMutationMode.Mutable
                });
            }
        }
    }

    private sealed class CapturingObserver : IBaseCommittedMutationObserver
    {
        public List<BaseRecordMutationEvent> Events { get; } = [];

        public ValueTask ObserveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default)
        {
            Events.Add(mutation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderedPublisher : IBaseEventPublisher
    {
        public List<BaseRecordMutationEvent> Events { get; } = [];

        public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
            BaseEvent @event,
            CancellationToken cancellationToken = default)
        {
            var mutation = Assert.IsType<BaseRecordMutationEvent>(@event);
            Events.Add(mutation);
            return ValueTask.FromResult(OperationResults.Ok(new EventPublishResult
            {
                EventId = mutation.EventId,
                Guarantee = EventDeliveryGuarantee.BestEffort
            }));
        }
    }

    private sealed class ThrowingEventFactory : IBaseEventFactory
    {
        public BaseRecordMutationEvent CreateRecordMutationEvent(
            BaseOperationKind operation,
            OperationContext context,
            PrincipalContext principal,
            CollectionDefinition collection,
            RecordEnvelope? before,
            RecordEnvelope? after,
            string[]? changedFields,
            string? committedEventId) =>
            throw new InvalidOperationException("hostile post-commit failure");
    }

    private sealed class CommittedStateObserver(FakeRecordStore store)
        : IBaseCommittedMutationObserver
    {
        public List<string> RecordIds { get; } = [];
        public List<OperationStatus> Statuses { get; } = [];

        public async ValueTask ObserveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default)
        {
            var id = mutation.Resource.RecordId!.Value;
            RecordIds.Add(id.Value);
            var result = await store.GetAsync(
                Collection("items"),
                id,
                RuntimeTestData.Operation(BaseOperationKind.Get),
                cancellationToken);
            Statuses.Add(result.Status);
        }
    }
}
