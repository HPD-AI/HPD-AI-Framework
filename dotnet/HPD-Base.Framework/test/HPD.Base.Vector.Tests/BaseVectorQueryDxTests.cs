using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base.Vector.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Vector.Tests;

public sealed class BaseVectorQueryDxTests
{
    [Fact]
    public async Task Typed_or_and_in_filters_and_boundary_ties_are_deterministic()
    {
        await using ServiceProvider provider = Build();
        BaseTestVectorStore store = provider.GetRequiredService<BaseTestVectorStore>();
        store.Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id,
        [
            Entry("c", "three", [0, 1]),
            Entry("b", "two", [1, 0]),
            Entry("a", "one", [1, 0]),
        ]);
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(Admin());

        BaseVectorResult<VectorDxDocument> orResult = (await session.Collection(VectorDxDocument.Collection)
            .Vector(VectorDxDocument.VectorIndexes.Cosine)
            .Nearest(BaseVector.Create([1, 0]))
            .Where(VectorDxDocument.Fields.Tenant, "none")
            .OrWhere(VectorDxDocument.Fields.Tenant, "one")
            .OrWhere(VectorDxDocument.Fields.Tenant, "two")
            .Take(1)
            .ExecuteAsync()).RequireValue();
        BaseVectorResult<VectorDxDocument> inResult = (await session.Collection(VectorDxDocument.Collection)
            .Vector(VectorDxDocument.VectorIndexes.Cosine)
            .Nearest(BaseVector.Create([1, 0]))
            .WhereAny(VectorDxDocument.Fields.Tenant, "one", "two")
            .Take(1)
            .ExecuteAsync()).RequireValue();

        orResult.Matches.Should().ContainSingle();
        orResult.Matches[0].Record.Id.Value.Should().Be("a");
        inResult.Matches.Should().ContainSingle();
        inResult.Matches[0].Record.Id.Value.Should().Be("a");
    }

    [Theory]
    [InlineData(BaseVectorFunction.CosineSimilarity)]
    [InlineData(BaseVectorFunction.EuclideanDistance)]
    [InlineData(BaseVectorFunction.DotProductSimilarity)]
    public async Task Testing_provider_labels_every_function_truthfully(BaseVectorFunction function)
    {
        await using ServiceProvider provider = Build();
        BaseVectorIndex<VectorDxDocument> index = function switch
        {
            BaseVectorFunction.CosineSimilarity => VectorDxDocument.VectorIndexes.Cosine,
            BaseVectorFunction.EuclideanDistance => VectorDxDocument.VectorIndexes.Euclidean,
            _ => VectorDxDocument.VectorIndexes.Dot,
        };
        provider.GetRequiredService<BaseTestVectorStore>().Seed(VectorDxDocument.Collection.Id, index.Definition.Id, [Entry("a", "one", [1, 0])]);
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(Admin());

        BaseVectorResult<VectorDxDocument> result = (await session.Collection(VectorDxDocument.Collection).Vector(index).Nearest(BaseVector.Create([1, 0])).Take(1).ExecuteAsync()).RequireValue();

        result.Matches[0].Measure.Function.Should().Be(function);
        result.Matches[0].Measure.Direction.Should().Be(function == BaseVectorFunction.EuclideanDistance ? BaseVectorMeasureDirection.LowerIsNearer : BaseVectorMeasureDirection.HigherIsNearer);
    }

    [Fact]
    public async Task Non_cooperative_search_is_bounded_quarantined_and_capacity_limited()
    {
        await using ServiceProvider provider = Build(
            vector => { vector.ProviderTimeout = TimeSpan.FromMilliseconds(100); vector.MaxActiveAndQuarantinedOperations = 1; vector.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100); },
            testing => { testing.SearchDelay = TimeSpan.FromMilliseconds(400); testing.IgnoreSearchCancellation = true; });
        provider.GetRequiredService<BaseTestVectorStore>().Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, [Entry("a", "one", [1, 0])]);
        BaseVectorQuery<VectorDxDocument> query = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(VectorDxDocument.Collection).Vector(VectorDxDocument.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(1);

        BaseResult<BaseVectorResult<VectorDxDocument>> first = await query.ExecuteAsync();
        BaseResult<BaseVectorResult<VectorDxDocument>> second = await query.ExecuteAsync();
        IBaseHealthContributor health = provider.GetServices<IBaseHealthContributor>().Single(contributor => contributor.Id == "hpd.base.vector");
        HealthDescriptor during = (await health.GetHealthAsync()).Single();

        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)first).Error.Code.Should().Be(BaseVectorErrorCodes.Timeout);
        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)second).Error.Code.Should().Be(BaseVectorErrorCodes.Timeout);
        during.Metrics!.Single(metric => metric.Name == "quarantinedOperations").NumberValue.Should().Be(1);
        await Task.Delay(450);
        HealthDescriptor after = (await health.GetHealthAsync()).Single();
        after.Metrics!.Single(metric => metric.Name == "quarantinedOperations").NumberValue.Should().Be(0);
    }

    [Fact]
    public async Task Derived_fixture_enforces_lag_gap_retention_and_explicit_consistency()
    {
        await using ServiceProvider provider = Build(
            vector => vector.DerivedProviderDefaultConsistency = new BaseVectorConsistencyRequirement.BoundedStaleness(TimeSpan.FromMinutes(1)),
            testing => testing.Consistency = BaseVectorProviderConsistency.DerivedJournal);
        BaseTestVectorStore store = provider.GetRequiredService<BaseTestVectorStore>();
        store.Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, [Entry("a", "one", [1, 0])]);
        store.SetDerivedState(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, 3, DateTimeOffset.UtcNow.AddMinutes(-2));
        BaseVectorQuery<VectorDxDocument> query = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(VectorDxDocument.Collection).Vector(VectorDxDocument.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(1);

        BaseResult<BaseVectorResult<VectorDxDocument>> stale = await query.ExecuteAsync();
        BaseVectorResult<VectorDxDocument> explicitlyAvailable = (await query.WithConsistency(new BaseVectorConsistencyRequirement.Available()).ExecuteAsync()).RequireValue();
        store.ApplyDerivedPosition(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, DateTimeOffset.UtcNow);
        BaseResult<BaseVectorResult<VectorDxDocument>> gap = await query.WithConsistency(new BaseVectorConsistencyRequirement.Available()).ExecuteAsync();
        store.SetDerivedState(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, 5, DateTimeOffset.UtcNow);
        store.OvertakeDerivedRetention(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id);
        BaseResult<BaseVectorResult<VectorDxDocument>> overtaken = await query.WithConsistency(new BaseVectorConsistencyRequirement.Available()).ExecuteAsync();

        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)stale).Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyUnavailable);
        explicitlyAvailable.Matches.Should().ContainSingle();
        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)gap).Error.Code.Should().Be("base.vector.rebuildRequired");
        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)overtaken).Error.Code.Should().Be("base.vector.rebuildRequired");
    }

    [Fact]
    public async Task Derived_at_least_waits_for_ordered_catch_up_without_weakening_consistency()
    {
        await using ServiceProvider provider = Build(
            vector =>
            {
                vector.DerivedProviderDefaultConsistency = new BaseVectorConsistencyRequirement.Available();
                vector.ConsistencyWaitTimeout = TimeSpan.FromSeconds(1);
            },
            testing => testing.Consistency = BaseVectorProviderConsistency.DerivedJournal);
        BaseTestVectorStore store = provider.GetRequiredService<BaseTestVectorStore>();
        store.Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, [Entry("a", "one", [1, 0])]);
        store.SetDerivedState(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, 5, DateTimeOffset.UtcNow);
        BaseVectorQuery<VectorDxDocument> vectors = provider.GetRequiredService<IBaseSessionFactory>().For(Admin())
            .Collection(VectorDxDocument.Collection).Vector(VectorDxDocument.VectorIndexes.Cosine);
        BaseVectorConsistencyToken token = (await vectors.CaptureConsistencyAsync()).RequireValue();
        store.SetDerivedState(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, 3, DateTimeOffset.UtcNow);

        Task<BaseResult<BaseVectorResult<VectorDxDocument>>> pending = vectors.Nearest(BaseVector.Create([1, 0]))
            .WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(token)).Take(1).ExecuteAsync().AsTask();
        await Task.Delay(50);
        store.ApplyDerivedPosition(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 4, DateTimeOffset.UtcNow);
        store.ApplyDerivedPosition(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, DateTimeOffset.UtcNow);

        BaseVectorResult<VectorDxDocument> result = (await pending).RequireValue();
        result.Matches.Should().ContainSingle();

        store.SetDerivedState(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, 5, 3, DateTimeOffset.UtcNow);
        BaseResult<BaseVectorResult<VectorDxDocument>> timedOut = await vectors.Nearest(BaseVector.Create([1, 0]))
            .WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(token)).Take(1).ExecuteAsync();
        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)timedOut).Error.Code.Should().Be(BaseVectorErrorCodes.Timeout);
    }

    [Fact]
    public async Task Shutdown_drain_is_bounded_when_quarantined_work_ignores_cancellation()
    {
        ServiceProvider provider = Build(
            vector => { vector.ProviderTimeout = TimeSpan.FromMilliseconds(100); vector.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(100); },
            testing => { testing.SearchDelay = TimeSpan.FromMilliseconds(500); testing.IgnoreSearchCancellation = true; });
        provider.GetRequiredService<BaseTestVectorStore>().Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, [Entry("a", "one", [1, 0])]);
        BaseVectorQuery<VectorDxDocument> query = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(VectorDxDocument.Collection).Vector(VectorDxDocument.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(1);
        _ = await query.ExecuteAsync();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await provider.DisposeAsync();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(350));
        await Task.Delay(450);
    }

    [Fact]
    public async Task Consistency_tokens_reject_tamper_cross_index_use_and_exact_expiry()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using ServiceProvider provider = Build(vector => vector.ConsistencyTokenLifetime = TimeSpan.FromMinutes(1), timeProvider: time);
        BaseTestVectorStore store = provider.GetRequiredService<BaseTestVectorStore>();
        store.Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, [Entry("a", "one", [1, 0])]);
        store.Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Euclidean.Definition.Id, [Entry("a", "one", [1, 0])]);
        BaseCollectionSession<VectorDxDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(VectorDxDocument.Collection);
        BaseVectorConsistencyToken token = (await collection.Vector(VectorDxDocument.VectorIndexes.Cosine).CaptureConsistencyAsync()).RequireValue();
        string wire = token.Encode();
        char replacement = wire[^1] == 'A' ? 'B' : 'A';
        BaseVectorConsistencyToken tampered = BaseVectorConsistencyToken.Parse(wire[..^1] + replacement);

        BaseResult<BaseVectorResult<VectorDxDocument>> invalid = await collection.Vector(VectorDxDocument.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(tampered)).Take(1).ExecuteAsync();
        BaseResult<BaseVectorResult<VectorDxDocument>> wrongIndex = await collection.Vector(VectorDxDocument.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0])).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(token)).Take(1).ExecuteAsync();
        time.Advance(TimeSpan.FromMinutes(1));
        BaseResult<BaseVectorResult<VectorDxDocument>> expired = await collection.Vector(VectorDxDocument.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(token)).Take(1).ExecuteAsync();

        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)invalid).Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyInvalid);
        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)wrongIndex).Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyScopeMismatch);
        ((BaseFailure<BaseVectorResult<VectorDxDocument>>)expired).Error.Code.Should().Be(BaseVectorErrorCodes.ConsistencyExpired);
    }

    [Fact]
    public async Task Vector_influence_denial_occurs_before_provider_preparation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = new byte[32], IssueNotBefore = DateTimeOffset.UnixEpoch })
            .ReplacePolicyEvaluator<DenyVectorPolicy>()
            .AddCollection(VectorDxDocument.Collection)
            .AddVector()
            .UseTestVectorProvider());
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseTestVectorStore store = provider.GetRequiredService<BaseTestVectorStore>();
        store.Seed(VectorDxDocument.Collection.Id, VectorDxDocument.VectorIndexes.Cosine.Definition.Id, [Entry("a", "one", [1, 0])]);

        BaseResult<BaseVectorResult<VectorDxDocument>> result = await provider.GetRequiredService<IBaseSessionFactory>().For(Admin()).Collection(VectorDxDocument.Collection).Vector(VectorDxDocument.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(1).ExecuteAsync();

        result.Status.Should().Be(OperationStatus.PolicyDenied);
        store.PrepareCalls.Should().Be(0);
        store.SearchCalls.Should().Be(0);
    }

    private static ServiceProvider Build(Action<HPDBaseVectorOptions>? vector = null, Action<BaseTestVectorProviderOptions>? testing = null, TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (timeProvider is not null) services.AddSingleton<TimeProvider>(timeProvider);
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = new byte[32], IssueNotBefore = DateTimeOffset.UnixEpoch })
            .ReplacePolicyEvaluator<AllowAllVectorPolicy>()
            .AddCollection(VectorDxDocument.Collection)
            .AddVector(vector)
            .UseTestVectorProvider(testing));
        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync().AsTask().GetAwaiter().GetResult().IsSuccess().Should().BeTrue();
        return provider;
    }

    private static PrincipalContext Admin() => new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "vector-test" };
    private static BaseTestVectorEntry Entry(string id, string tenant, float[] vector) => new()
    {
        Record = new RecordEnvelope
        {
            CollectionId = VectorDxDocument.Collection.Id,
            Id = new RecordId(id),
            Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [nameof(VectorDxDocument.Title)] = JsonSerializer.SerializeToElement(id),
                [nameof(VectorDxDocument.Tenant)] = JsonSerializer.SerializeToElement(tenant),
                [nameof(VectorDxDocument.Embedding)] = JsonSerializer.SerializeToElement(vector),
            } },
            Metadata = new RecordMetadata { Revision = new RevisionToken("test:1") },
        },
        Vector = BaseVector.Create(vector),
        Filters = new Dictionary<string, BaseVectorFilterValue> { [VectorDxDocument.Fields.Tenant.Id] = BaseVectorFilterValue.FromString(tenant) },
    };

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}

[BaseCollection("vector_dx_documents", typeof(VectorDxJsonContext))]
[BaseVectorIndex("vector.dx.cosine", nameof(VectorDxDocument.Embedding), VectorSpace = "vector.dx.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity, FilterFields = [nameof(VectorDxDocument.Tenant)])]
[BaseVectorIndex("vector.dx.euclidean", nameof(VectorDxDocument.Embedding), VectorSpace = "vector.dx.v1", Dimensions = 2, Function = BaseVectorFunction.EuclideanDistance, FilterFields = [nameof(VectorDxDocument.Tenant)])]
[BaseVectorIndex("vector.dx.dot", nameof(VectorDxDocument.Embedding), VectorSpace = "vector.dx.v1", Dimensions = 2, Function = BaseVectorFunction.DotProductSimilarity, FilterFields = [nameof(VectorDxDocument.Tenant)])]
public partial record VectorDxDocument
{
    [BaseField("vector.dx.title")] public required string Title { get; init; }
    [BaseField("vector.dx.tenant", Operators = BaseFieldOperator.Equal)] public required string Tenant { get; init; }
    [BaseField("vector.dx.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(VectorDxDocument))]
public partial class VectorDxJsonContext : JsonSerializerContext;

public sealed class AllowAllVectorPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}

public sealed class DenyVectorPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Operation.Operation == BaseOperationKind.VectorQuery ? PolicyDecision.Deny("vector.denied", "Vector access is denied.") : PolicyDecision.Allow());
}
