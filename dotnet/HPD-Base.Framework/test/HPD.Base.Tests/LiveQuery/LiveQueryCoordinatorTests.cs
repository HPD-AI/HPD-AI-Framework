using FluentAssertions;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Tests.LiveQuery;

public sealed class LiveQueryCoordinatorTests
{
    [Fact]
    public async Task SubscribeExecutesImmediatelyAndPublishesInitialSnapshot()
    {
        await using var subscription = await Coordinator().SubscribeAsync(Request("orders", 7, Reference("a")));

        var transition = await NextAsync(subscription);

        transition.Kind.Should().Be(BaseLiveQueryTransitionKind.Snapshot);
        transition.Version.Should().Be(1);
        transition.Value.Should().Be(7);
        subscription.SubscriptionId.Should().MatchRegex("^[a-f0-9]{32}$");
    }

    [Fact]
    public async Task OnlyExactMatchingInvalidationReruns()
    {
        var executions = 0;
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "orders",
            ExecuteAsync = _ => ValueTask.FromResult(Evaluation(++executions, Reference("a")))
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("b")));
        await Task.Delay(50);
        executions.Should().Be(1);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        (await NextAsync(subscription)).Value.Should().Be(2);
    }

    [Fact]
    public async Task SuccessfulRerunAtomicallyReplacesDependencies()
    {
        var executions = 0;
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "orders",
            ExecuteAsync = _ =>
            {
                executions++;
                return ValueTask.FromResult(Evaluation(
                    executions,
                    executions == 1 ? Reference("a") : Reference("b")));
            }
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        _ = await NextAsync(subscription);
        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        await Task.Delay(50);
        executions.Should().Be(2);

        await coordinator.InvalidateAsync(Invalidation(Reference("b")));
        (await NextAsync(subscription)).Value.Should().Be(3);
    }

    [Fact]
    public async Task RerunsAreSerialAndInvalidationsDuringExecutionCoalesce()
    {
        var executions = 0;
        var active = 0;
        var maximumActive = 0;
        var rerunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRerun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "orders",
            ExecuteAsync = async cancellationToken =>
            {
                var currentActive = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, currentActive);
                var execution = Interlocked.Increment(ref executions);
                if (execution == 2)
                {
                    rerunStarted.TrySetResult();
                    await releaseRerun.Task.WaitAsync(cancellationToken);
                }
                Interlocked.Decrement(ref active);
                return Evaluation(execution, Reference("a"));
            }
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        await rerunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 10; index++)
            await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        releaseRerun.TrySetResult();

        (await NextAsync(subscription)).Version.Should().Be(2);
        (await NextAsync(subscription)).Version.Should().Be(3);
        executions.Should().Be(3);
        maximumActive.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_event_identity_is_delivered_once()
    {
        var executions = 0;
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "idempotent-invalidation",
            ExecuteAsync = _ => ValueTask.FromResult(Evaluation(++executions, Reference("a"))),
        });
        _ = await NextAsync(subscription);
        BaseDependencyInvalidation invalidation = Invalidation(Reference("a"));
        await coordinator.InvalidateAsync(invalidation);
        _ = await NextAsync(subscription);
        await coordinator.InvalidateAsync(invalidation);
        await Task.Delay(50);
        executions.Should().Be(2);
    }

    [Fact]
    public async Task DependencyIntroducedByRunningEvaluationCannotMissItsInvalidation()
    {
        var executions = 0;
        var rerunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRerun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "changing.orders",
            ExecuteAsync = async cancellationToken =>
            {
                var execution = Interlocked.Increment(ref executions);
                if (execution == 2)
                {
                    rerunStarted.TrySetResult();
                    await releaseRerun.Task.WaitAsync(cancellationToken);
                }
                return Evaluation(execution, execution == 1 ? Reference("a") : Reference("b"));
            }
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        await rerunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.InvalidateAsync(Invalidation(Reference("b")));
        releaseRerun.TrySetResult();

        (await NextAsync(subscription)).Version.Should().Be(2);
        (await NextAsync(subscription)).Version.Should().Be(3);
        executions.Should().Be(3);
    }

    [Fact]
    public async Task InvalidationDuringInitialExecutionSchedulesConservativeRerun()
    {
        var executions = 0;
        var initialStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = Coordinator();
        var subscriptionTask = coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "initial.race",
            ExecuteAsync = async cancellationToken =>
            {
                var execution = Interlocked.Increment(ref executions);
                if (execution == 1)
                {
                    initialStarted.TrySetResult();
                    await releaseInitial.Task.WaitAsync(cancellationToken);
                }
                return Evaluation(execution, Reference("a"));
            }
        }).AsTask();

        await initialStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.InvalidateAsync(Invalidation(Reference("unseen-during-open")));
        releaseInitial.TrySetResult();
        await using var subscription = await subscriptionTask;

        (await NextAsync(subscription)).Version.Should().Be(1);
        (await NextAsync(subscription)).Version.Should().Be(2);
        executions.Should().Be(2);
    }

    [Fact]
    public async Task InvalidationImmediatelyBeforeDependencyInstallationCannotBeLost()
    {
        var executions = 0;
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "install.race",
            ExecuteAsync = async _ =>
            {
                var execution = Interlocked.Increment(ref executions);
                if (execution == 2)
                    await coordinator.InvalidateAsync(Invalidation(Reference("b")));
                return Evaluation(execution, execution == 1 ? Reference("a") : Reference("b"));
            }
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));

        (await NextAsync(subscription)).Version.Should().Be(2);
        (await NextAsync(subscription)).Version.Should().Be(3);
        executions.Should().Be(3);
    }

    [Fact]
    public async Task EveryRerunUsesCurrentHostPolicy()
    {
        var allowed = true;
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "secure.orders",
            ExecuteAsync = _ => allowed
                ? ValueTask.FromResult(Evaluation(1, Reference("a")))
                : throw new BaseLiveQueryException("base.liveQuery.unauthorized", "The query is no longer authorized.")
        });
        _ = await NextAsync(subscription);
        allowed = false;

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        var failure = await NextAsync(subscription);

        failure.Kind.Should().Be(BaseLiveQueryTransitionKind.Failed);
        failure.Failure!.Code.Should().Be("base.liveQuery.unauthorized");
    }

    [Fact]
    public async Task UnexpectedExecutorFailureIsSanitized()
    {
        var executions = 0;
        var coordinator = Coordinator();
        await using var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "orders",
            ExecuteAsync = _ =>
            {
                if (++executions > 1)
                    throw new InvalidOperationException("secret-record-42");
                return ValueTask.FromResult(Evaluation(1, Reference("a")));
            }
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        var failure = await NextAsync(subscription);

        failure.Failure!.Code.Should().Be(BaseLiveQueryErrorCodes.ExecutionFailed);
        failure.Failure.Message.Should().NotContain("secret");
    }

    [Fact]
    public async Task InitialEvaluationTimeoutIsEnforcedWhenExecutorIgnoresCancellation()
    {
        var coordinator = Coordinator(options =>
            options.MaxEvaluationDuration = TimeSpan.FromMilliseconds(20));
        var never = new TaskCompletionSource<BaseLiveQueryEvaluation<int>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var subscribe = async () => await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "initial.timeout",
            ExecuteAsync = _ => new ValueTask<BaseLiveQueryEvaluation<int>>(never.Task)
        }).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        var failure = await subscribe.Should().ThrowAsync<BaseLiveQueryException>();
        failure.Which.Code.Should().Be(BaseLiveQueryErrorCodes.ExecutionFailed);
        failure.Which.SafeMessage.ToLowerInvariant().Should().NotContain("cancel");
    }

    [Fact]
    public async Task RerunTimeoutTerminatesAndDisposalDoesNotWaitForExecutor()
    {
        var executions = 0;
        var never = new TaskCompletionSource<BaseLiveQueryEvaluation<int>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = Coordinator(options =>
            options.MaxEvaluationDuration = TimeSpan.FromMilliseconds(20));
        var subscription = await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "rerun.timeout",
            ExecuteAsync = _ => Interlocked.Increment(ref executions) == 1
                ? ValueTask.FromResult(Evaluation(1, Reference("a")))
                : new ValueTask<BaseLiveQueryEvaluation<int>>(never.Task)
        });
        _ = await NextAsync(subscription);

        await coordinator.InvalidateAsync(Invalidation(Reference("a")));
        var failure = await NextAsync(subscription);

        failure.Kind.Should().Be(BaseLiveQueryTransitionKind.Failed);
        failure.Failure!.Code.Should().Be(BaseLiveQueryErrorCodes.ExecutionFailed);
        await subscription.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TransitionQueueRejectsACompetingReader()
    {
        await using var subscription = await Coordinator()
            .SubscribeAsync(Request("single.reader", 1, Reference("a")));
        await using var first = subscription.Transitions.GetAsyncEnumerator();
        (await first.MoveNextAsync()).Should().BeTrue();
        await using var competing = subscription.Transitions.GetAsyncEnumerator();

        var read = async () => await competing.MoveNextAsync();

        var failure = await read.Should().ThrowAsync<BaseLiveQueryException>();
        failure.Which.Code.Should().Be(BaseLiveQueryErrorCodes.RequestInvalid);
    }

    [Fact]
    public async Task CapacityAndDependencyLimitsFailExplicitly()
    {
        var coordinator = Coordinator(options =>
        {
            options.MaxActiveSubscriptions = 1;
            options.MaxDependenciesPerEvaluation = 1;
        });
        await using var first = await coordinator.SubscribeAsync(Request("first", 1, Reference("a")));

        var capacity = async () => await coordinator.SubscribeAsync(Request("second", 2, Reference("b")));
        (await capacity.Should().ThrowAsync<BaseLiveQueryException>())
            .Which.Code.Should().Be(BaseLiveQueryErrorCodes.CapacityExceeded);

        await first.DisposeAsync();
        var invalid = async () => await coordinator.SubscribeAsync(new BaseLiveQueryRequest<int>
        {
            QueryId = "invalid",
            ExecuteAsync = _ => ValueTask.FromResult(new BaseLiveQueryEvaluation<int>
            {
                Value = 1,
                Dependencies = new BaseDependencySet { References = [] }
            })
        });
        (await invalid.Should().ThrowAsync<BaseLiveQueryException>())
            .Which.Code.Should().Be(BaseLiveQueryErrorCodes.DependenciesInvalid);
    }

    [Fact]
    public async Task DisposalEndsTheTransitionStreamNormally()
    {
        var subscription = await Coordinator().SubscribeAsync(Request("orders", 1, Reference("a")));
        await using var enumerator = subscription.Transitions.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        await subscription.DisposeAsync();

        (await enumerator.MoveNextAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CommittedMutationObserverMapsAndPublishesInvalidation()
    {
        var reference = Reference("a");
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDependencyInvalidationMapper>(
            new StubInvalidationMapper(Invalidation(reference)));
        services.AddHPDBaseLiveQuery();
        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IBaseLiveQueryCoordinator>();
        await using var subscription = await coordinator.SubscribeAsync(Request("orders", 1, reference));
        _ = await NextAsync(subscription);

        var observer = provider.GetServices<IBaseCommittedMutationObserver>().Single();
        await observer.ObserveAsync(Mutation());

        (await NextAsync(subscription)).Version.Should().Be(2);
    }

    [Fact]
    public async Task InvalidationMappingFailureTerminatesSubscriptionsSafely()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseDependencyInvalidationMapper>(new ThrowingInvalidationMapper());
        services.AddHPDBaseLiveQuery();
        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IBaseLiveQueryCoordinator>();
        await using var subscription = await coordinator.SubscribeAsync(Request("orders", 1, Reference("a")));
        _ = await NextAsync(subscription);

        var observer = provider.GetServices<IBaseCommittedMutationObserver>().Single();
        var observe = async () => await observer.ObserveAsync(Mutation());
        await observe.Should().ThrowAsync<Exception>()
            .WithMessage("Live-query invalidation failed.");
        var failure = await NextAsync(subscription);

        failure.Kind.Should().Be(BaseLiveQueryTransitionKind.Failed);
        failure.Failure!.Code.Should().Be(BaseLiveQueryErrorCodes.InvalidationFailed);
        failure.Failure.Message.Should().NotContain("secret");
    }

    [Fact]
    public async Task DescriptorAdvertisesOnlyServerRerunCapabilitiesAndBounds()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseLiveQuery(options =>
        {
            options.MaxActiveSubscriptions = 12;
            options.MaxDependenciesPerEvaluation = 7;
            options.MaxEvaluationDuration = TimeSpan.FromSeconds(4);
        });
        using var provider = services.BuildServiceProvider();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        var module = snapshot.Manifest.Modules!.Single(item => item.Id == BaseLiveQueryModuleIds.Module);
        var family = snapshot.Capabilities.Families.Single(item => item.FamilyId == "base.liveQuery");

        module.ContributedCapabilities.Should().Equal(
            BaseLiveQueryFeatureIds.ServerRerun,
            BaseLiveQueryFeatureIds.CommittedInvalidation);
        module.ContributedRouteIds.Should().BeNullOrEmpty();
        family.Limits.Should().Contain(item =>
            item.Name == "activeSubscriptions" && item.Value == "12");
        family.Limits.Should().Contain(item =>
            item.Name == "dependenciesPerEvaluation" && item.Value == "7");
        family.Limits.Should().Contain(item =>
            item.Name == "evaluationDuration"
            && item.Value == "4000"
            && item.Unit == "milliseconds");
    }

    [Fact]
    public void EvaluationDurationMustBeStrictlyBounded()
    {
        var tooShort = () => Coordinator(options =>
            options.MaxEvaluationDuration = TimeSpan.FromMilliseconds(9));
        var tooLong = () => Coordinator(options =>
            options.MaxEvaluationDuration = TimeSpan.FromMinutes(10).Add(TimeSpan.FromMilliseconds(1)));

        tooShort.Should().Throw<ArgumentOutOfRangeException>();
        tooLong.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static IBaseLiveQueryCoordinator Coordinator(Action<BaseLiveQueryOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddHPDBaseLiveQuery(configure);
        return services.BuildServiceProvider().GetRequiredService<IBaseLiveQueryCoordinator>();
    }

    private static BaseLiveQueryRequest<int> Request(
        string id,
        int value,
        BaseDependencyReference reference) => new()
    {
        QueryId = id,
        ExecuteAsync = _ => ValueTask.FromResult(Evaluation(value, reference))
    };

    private static BaseLiveQueryEvaluation<int> Evaluation(
        int value,
        BaseDependencyReference reference) => new()
    {
        Value = value,
        Dependencies = new BaseDependencySet { References = [reference] }
    };

    private static BaseDependencyReference Reference(string value) => new()
    {
        TemplateId = "test.record",
        Value = value
    };

    private static BaseDependencyInvalidation Invalidation(BaseDependencyReference reference) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        OccurredAt = DateTimeOffset.UtcNow,
        Reason = BaseDependencyInvalidationReasons.RecordMutation,
        References = [reference]
    };

    private static BaseRecordMutationEvent Mutation() => new()
    {
        EventId = "event-one",
        Type = BaseEventTypes.RecordCreated,
        SchemaVersion = BaseEventSchemaVersions.V1,
        Visibility = VisibilityLevel.Internal,
        Resource = new EventResource
        {
            Kind = EventResourceKind.Record,
            CollectionId = "items",
            RecordId = new RecordId("record-one")
        },
        Operation = BaseOperationKind.Create
    };

    private static async Task<BaseLiveQueryTransition<T>> NextAsync<T>(
        IBaseLiveQuerySubscription<T> subscription)
    {
        await using var enumerator = subscription.Transitions.GetAsyncEnumerator();
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        return enumerator.Current;
    }

    private sealed class StubInvalidationMapper(BaseDependencyInvalidation invalidation)
        : IBaseDependencyInvalidationMapper
    {
        public ValueTask<BaseDependencyInvalidation> MapAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(invalidation);
    }

    private sealed class ThrowingInvalidationMapper : IBaseDependencyInvalidationMapper
    {
        public ValueTask<BaseDependencyInvalidation> MapAsync(
            BaseRecordMutationEvent mutation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret-record-42");
    }
}
