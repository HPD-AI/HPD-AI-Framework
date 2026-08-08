using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base;
using HPD.Base.Vector.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Vector.DerivedAotSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 1, Key = new byte[32], IssueNotBefore = DateTimeOffset.UnixEpoch })
            .ReplacePolicyEvaluator<AllowAll>()
            .AddCollection(DerivedVectorRecord.Collection)
            .AddVector(options => options.DerivedProviderDefaultConsistency = new BaseVectorConsistencyRequirement.Available())
            .UseTestVectorProvider(options => options.Consistency = BaseVectorProviderConsistency.DerivedJournal));
        await using ServiceProvider provider = services.BuildServiceProvider();
        if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()) return 2;
        BaseTestVectorStore store = provider.GetRequiredService<BaseTestVectorStore>();
        store.Seed(DerivedVectorRecord.Collection.Id, DerivedVectorRecord.VectorIndexes.Search.Definition.Id,
        [
            new BaseTestVectorEntry
            {
                Record = new RecordEnvelope
                {
                    CollectionId = DerivedVectorRecord.Collection.Id,
                    Id = new RecordId("one"),
                    Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = new Dictionary<string, JsonElement> { [nameof(DerivedVectorRecord.Label)] = Parse("\"one\""), [nameof(DerivedVectorRecord.Embedding)] = Parse("[1,0]") } },
                    Metadata = new RecordMetadata { Revision = new RevisionToken("derived:1") },
                },
                Vector = BaseVector.Create([1, 0]),
            },
        ]);
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "aot" });
        BaseVectorResult<DerivedVectorRecord> result = (await session.Collection(DerivedVectorRecord.Collection).Vector(DerivedVectorRecord.VectorIndexes.Search).Nearest(BaseVector.Create([1, 0])).Take(1).ExecuteAsync()).RequireValue();
        return result.Matches is [{ Record.Id.Value: "one" }] ? 0 : 3;
    }

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

[BaseCollection("derived_vector_records", typeof(DerivedVectorJsonContext))]
[BaseVectorIndex("derived.vector.search", nameof(Embedding), VectorSpace = "derived.space.v1", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity)]
internal partial record DerivedVectorRecord
{
    [BaseField("derived.vector.label")] public required string Label { get; init; }
    [BaseField("derived.vector.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(DerivedVectorRecord))]
internal partial class DerivedVectorJsonContext : JsonSerializerContext;

internal sealed class AllowAll : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}
