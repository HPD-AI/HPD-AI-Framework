using System.Text.Json;
using HPD.Base.Events;
using HPD.Base.Dependencies;
using HPD.Base.LiveQuery;
using HPD.Base.LiveQuery.DependencyInjection;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Events;
using HPD.Base.Runtime.Operations;
using HPD.Base.Runtime.Stores;
using HPD.Base.Schema;
using HPD.Base.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Events;

public sealed class EventDispatcherTests
{
    [Fact]
    public async Task BestEffortPublishFailurePreservesMutationAndAddsWarning()
    {
        var store = new FakeRecordStore("primary");
        using var provider = Provider(
            store,
            services => services.AddSingleton<IBaseEventPublisher, FailingEventPublisher>());

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Single(result.Events!);
        Assert.Contains(result.Warnings!, warning => warning.Code == "base.runtime.events.publishFailed");
    }

    [Fact]
    public async Task DisabledEventsPreserveMutationAndAddWarning()
    {
        var store = new FakeRecordStore("primary");
        using var provider = Provider(
            store,
            configureRuntime: options => options.Events.Enabled = false);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Single(result.Events!);
        Assert.Contains(result.Warnings!, warning => warning.Code == "base.runtime.events.disabled");
    }

    [Fact]
    public async Task CommittedMutationObserversRunWhenExternalEventsAreDisabled()
    {
        var store = new FakeRecordStore("primary");
        var observer = new CapturingMutationObserver();
        using var provider = Provider(
            store,
            services => services.AddSingleton<IBaseCommittedMutationObserver>(observer),
            options => options.Events.Enabled = false);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Single(observer.Mutations);
    }

    [Fact]
    public async Task ObserverFailurePreservesCommittedMutationAndAddsBoundedWarning()
    {
        var store = new FakeRecordStore("primary");
        var succeedingObserver = new CapturingMutationObserver();
        using var provider = Provider(
            store,
            services =>
            {
                services.AddSingleton<IBaseCommittedMutationObserver>(new ThrowingMutationObserver());
                services.AddSingleton<IBaseCommittedMutationObserver>(succeedingObserver);
            });

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Single(succeedingObserver.Mutations);
        var warning = Assert.Single(result.Warnings!, warning =>
            warning.Code == "base.runtime.events.observerFailed");
        Assert.DoesNotContain("secret", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestCancellationAfterCommitCannotSuppressObservers()
    {
        using var requestCancellation = new CancellationTokenSource();
        var store = new FakeRecordStore("primary")
        {
            AfterCreateCommitted = requestCancellation.Cancel
        };
        var observer = new CapturingMutationObserver();
        using var provider = Provider(
            store,
            services => services.AddSingleton<IBaseCommittedMutationObserver>(observer));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            requestCancellation.Token);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Single(observer.Mutations);
    }

    [Fact]
    public async Task LiveQueryMappingFailureCommitsWarnsFailsSubscriptionAndContinuesObservers()
    {
        var store = new FakeRecordStore("primary");
        var laterObserver = new CapturingMutationObserver();
        using var provider = Provider(
            store,
            services =>
            {
                services.AddSingleton<IBaseDependencyInvalidationMapper, ThrowingInvalidationMapper>();
                services.AddHPDBaseLiveQuery();
                services.AddSingleton<IBaseCommittedMutationObserver>(laterObserver);
            });
        var coordinator = provider.GetRequiredService<IBaseLiveQueryCoordinator>();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "integration.failure",
            ExecuteAsync = _ => ValueTask.FromResult(new BaseLiveQueryEvaluation<int>
            {
                Value = 1,
                Dependencies = new BaseDependencySet
                {
                    References =
                    [
                        new BaseDependencyReference
                        {
                            TemplateId = "test.record",
                            Value = "opaque"
                        }
                    ]
                }
            })
        });
        _ = await NextAsync(subscription);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);
        var failure = await NextAsync(subscription);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Equal(BaseLiveQueryTransitionKind.Failed, failure.Kind);
        Assert.Equal(BaseLiveQueryErrorCodes.InvalidationFailed, failure.Failure!.Code);
        Assert.DoesNotContain("secret", failure.Failure.Message, StringComparison.OrdinalIgnoreCase);
        var warning = Assert.Single(result.Warnings!, item =>
            item.Code == "base.runtime.events.observerFailed");
        Assert.DoesNotContain("secret", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(laterObserver.Mutations);
    }

    [Fact]
    public async Task TimedOutObserverCannotSuppressLiveQueryOrLaterObservers()
    {
        var store = new FakeRecordStore("primary");
        var laterObserver = new CapturingMutationObserver();
        using var provider = Provider(
            store,
            services =>
            {
                services.AddSingleton<IBaseCommittedMutationObserver>(new NeverCompletingMutationObserver());
                services.AddSingleton<IBaseDependencyInvalidationMapper>(new MatchingInvalidationMapper());
                services.AddHPDBaseLiveQuery();
                services.AddSingleton<IBaseCommittedMutationObserver>(laterObserver);
            },
            options => options.Events.PostCommitWorkTimeout = TimeSpan.FromMilliseconds(30));
        var coordinator = provider.GetRequiredService<IBaseLiveQueryCoordinator>();
        await using var subscription = await SubscribeAsync(coordinator);
        _ = await NextAsync(subscription);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);
        var refreshed = await NextAsync(subscription);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(2, refreshed.Version);
        Assert.Equal(BaseLiveQueryTransitionKind.Snapshot, refreshed.Kind);
        Assert.Single(result.Warnings!, item =>
            item.Code == "base.runtime.events.observerFailed");
        Assert.Single(laterObserver.Mutations);
    }

    [Fact]
    public async Task TimedOutLiveQueryMapperFailsSubscriptionsAndCannotSuppressLaterObserver()
    {
        var store = new FakeRecordStore("primary");
        var laterObserver = new CapturingMutationObserver();
        using var provider = Provider(
            store,
            services =>
            {
                services.AddSingleton<IBaseCommittedMutationObserver>(new NeverCompletingMutationObserver());
                services.AddSingleton<IBaseDependencyInvalidationMapper>(new NeverCompletingInvalidationMapper());
                services.AddHPDBaseLiveQuery();
                services.AddSingleton<IBaseCommittedMutationObserver>(laterObserver);
            },
            options => options.Events.PostCommitWorkTimeout = TimeSpan.FromMilliseconds(30));
        var coordinator = provider.GetRequiredService<IBaseLiveQueryCoordinator>();
        await using var subscription = await SubscribeAsync(coordinator);
        _ = await NextAsync(subscription);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);
        var failure = await NextAsync(subscription);

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(BaseLiveQueryTransitionKind.Failed, failure.Kind);
        Assert.Equal(BaseLiveQueryErrorCodes.InvalidationFailed, failure.Failure!.Code);
        Assert.Single(result.Warnings!, item =>
            item.Code == "base.runtime.events.observerFailed");
        Assert.Single(laterObserver.Mutations);
    }

    [Fact]
    public async Task TimedOutPublisherCannotHoldCommittedOperationIndefinitely()
    {
        var store = new FakeRecordStore("primary");
        using var provider = Provider(
            store,
            services => services.AddSingleton<IBaseEventPublisher>(new NeverCompletingEventPublisher()),
            options => options.Events.PostCommitWorkTimeout = TimeSpan.FromMilliseconds(30));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Contains(result.Warnings!, item =>
            item.Code == "base.runtime.events.publishFailed");
    }

    [Fact]
    public async Task RequireEnqueueFailsRuntimeValidationWithoutDurablePublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        services.AddHPDBaseRuntime(options => options.Events.PublishFailureMode = BaseEventPublishFailureMode.RequireEnqueue);
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        Assert.False(snapshot.Validation.Succeeded);
        Assert.Contains(snapshot.Validation.Issues!, issue => issue.Code == "base.runtime.events.transactionalJournalRequired");
    }

    [Fact]
    public async Task RequireEnqueueRejectsNonJournalStoreBeforeMutation()
    {
        var store = new FakeRecordStore("primary");
        using var provider = Provider(
            store,
            configureRuntime: options =>
                options.Events.PublishFailureMode = BaseEventPublishFailureMode.RequireEnqueue);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items",
            CreateRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Create),
            CancellationToken.None);

        Assert.Equal(OperationStatus.CapabilityUnavailable, result.Status);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal("base.runtime.events.transactionalJournalRequired", result.Error!.Code);
    }

    private static ServiceProvider Provider(
        FakeRecordStore store,
        Action<IServiceCollection>? configureServices = null,
        Action<HPDBaseRuntimeOptions>? configureRuntime = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionContributor());
        services.AddSingleton<IPolicyEvaluator>(new AllowPolicyEvaluator());
        configureServices?.Invoke(services);
        services.AddHPDBaseRuntime(configureRuntime);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = ["items"]
        });
        return provider;
    }

    private static RecordCreateRequest CreateRequest()
    {
        using var document = JsonDocument.Parse("""{"title":"hello"}""");
        return new RecordCreateRequest
        {
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.Json,
                Json = document.RootElement.Clone()
            }
        };
    }

    private sealed class CollectionContributor : IBaseDescriptorContributor
    {
        public string Id => "collections";

        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            builder.AddCollection(new CollectionDefinition
            {
                Id = "items",
                Name = "items",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                Operations = new CollectionOperationMatrix
                {
                    Create = true
                }
            });
        }
    }

    private sealed class CapturingMutationObserver : IBaseCommittedMutationObserver
    {
        public List<BaseRecordMutationEvent> Mutations { get; } = [];

        public ValueTask ObserveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default)
        {
            Mutations.Add(mutation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingMutationObserver : IBaseCommittedMutationObserver
    {
        public ValueTask ObserveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret-record-42");
    }

    private sealed class ThrowingInvalidationMapper : IBaseDependencyInvalidationMapper
    {
        public ValueTask<BaseDependencyInvalidation> MapAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret-record-42");
    }

    private sealed class MatchingInvalidationMapper : IBaseDependencyInvalidationMapper
    {
        public ValueTask<BaseDependencyInvalidation> MapAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BaseDependencyInvalidation
            {
                EventId = mutation.EventId,
                OccurredAt = mutation.Timestamp,
                Reason = BaseDependencyInvalidationReasons.RecordMutation,
                References =
                [
                    new BaseDependencyReference
                    {
                        TemplateId = "test.record",
                        Value = "opaque"
                    }
                ]
            });
    }

    private sealed class NeverCompletingInvalidationMapper : IBaseDependencyInvalidationMapper
    {
        private readonly TaskCompletionSource<BaseDependencyInvalidation> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<BaseDependencyInvalidation> MapAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            new(_completion.Task);
    }

    private sealed class NeverCompletingMutationObserver : IBaseCommittedMutationObserver
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask ObserveAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            new(_completion.Task);
    }

    private sealed class NeverCompletingEventPublisher : IBaseEventPublisher
    {
        private readonly TaskCompletionSource<OperationResult<EventPublishResult>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
            BaseEvent @event,
            CancellationToken cancellationToken = default) =>
            new(_completion.Task);
    }

    private static ValueTask<IBaseLiveQuerySubscription<int>> SubscribeAsync(
        IBaseLiveQueryCoordinator coordinator) =>
        coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "timeout.integration",
            ExecuteAsync = _ => ValueTask.FromResult(new BaseLiveQueryEvaluation<int>
            {
                Value = 1,
                Dependencies = new BaseDependencySet
                {
                    References =
                    [
                        new BaseDependencyReference
                        {
                            TemplateId = "test.record",
                            Value = "opaque"
                        }
                    ]
                }
            })
        });

    private static async Task<BaseLiveQueryTransition<T>> NextAsync<T>(
        IBaseLiveQuerySubscription<T> subscription)
    {
        await using var enumerator = subscription.Transitions.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        return enumerator.Current;
    }
}
