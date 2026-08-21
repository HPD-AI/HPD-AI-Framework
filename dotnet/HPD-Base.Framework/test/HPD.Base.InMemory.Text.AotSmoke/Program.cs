using System.Text.Json.Serialization;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.InMemory.Text.AotSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddPolicyAuthority<AllowAll>(new() { Id = "text.aot.policy", Version = 1, OwningModuleId = "text.aot", EvaluatorContractId = "text.aot.policy.v1", EvaluatorContractVersion = 1, CompositionOrder = 0 });
            builder.AddStaticGrantAuthority(new() { Id = BaseTextGrants.Query, Version = 1, OwningModuleId = "text.aot", SourceContractId = "text.aot.grants", SourceContractVersion = 1 }, Grant("aot"));
            builder.AddCollection(AotTextRecord.Collection);
        });
        await using ServiceProvider provider = services.BuildServiceProvider(); if (!(await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()) return 2;
        BaseCollectionSession<AotTextRecord> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "aot" }).Collection(AotTextRecord.Collection);
        (await collection.CreateAsync(new("one"), new() { Title = "Portable lexical search", State = "published" })).RequireValue();
        BaseTextResult<AotTextRecord> result = (await collection.Text(AotTextRecord.TextIndexes.Content, BaseTextQuery.ExactPhrase("lexical", "search")).Where(AotTextRecord.Fields.State, "published").Take(4).ExecuteAsync()).RequireValue();
        IBaseTextAdministration administration = provider.GetRequiredService<IBaseTextAdministration>(); BaseTextIndexStatus state = (await administration.GetAsync(AotTextRecord.Collection.Id, AotTextRecord.TextIndexes.Content.Definition.Id)).Value!;
        BaseTextRebuildResult rebuilt = (await administration.RebuildAsync(new() { CollectionId = state.CollectionId, TextIndexId = state.TextIndexId, ExpectedGeneration = state.Generation, Identity = BaseMutationRequestIdentity.Create("aot", "text.rebuild", "one", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("one"u8))) })).Value!;
        return result.Matches is [{ Record.Id.Value: "one" }] && rebuilt.PublishedGeneration == state.Generation + 1 ? 0 : 3;
    }
    private static AccessGrant Grant(string subject) => new() { Id = BaseTextGrants.Query, ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application, Subject = new() { Kind = AccessSubjectKind.User, Id = subject }, Action = BaseTextGrants.Query, Scope = new() { Kind = ResourceScopeKind.TextIndex, CollectionId = "aot_text", TextIndexId = "aot.text.content" } };
}

[BaseCollection("aot_text", typeof(AotTextJsonContext))]
[BaseTextIndex("aot.text.content", Fields = [nameof(Title)], Weights = [4], FilterFields = [nameof(State)])]
internal partial record AotTextRecord { [BaseField("aot.text.title")] public required string Title { get; init; } [BaseField("aot.text.state")] public required string State { get; init; } }
[JsonSerializable(typeof(AotTextRecord))] internal partial class AotTextJsonContext : JsonSerializerContext;
internal sealed class AllowAll : IPolicyEvaluator { public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow()); }
