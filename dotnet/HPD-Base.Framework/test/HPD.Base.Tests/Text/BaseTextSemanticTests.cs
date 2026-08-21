using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using HPD.Base.Sqlite;

namespace HPD.Base.Tests.Text;

public sealed class BaseTextSemanticTests
{
    [Fact]
    public void Analyzer_applies_compatibility_normalization_and_full_case_folding()
    {
        ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze("Straße ＡＢＣ");

        Assert.Equal(["strasse", "abc"], tokens.ToArray());
    }

    [Fact]
    public void Analyzer_preserves_connectors_only_between_letters_or_digits()
    {
        ImmutableArray<string> tokens = BaseTextAnalyzer.Analyze("_alpha alpha_beta beta_ a__b");

        Assert.Equal(["alpha", "alpha_beta", "beta", "a", "b"], tokens.ToArray());
    }

    [Fact]
    public void Analyzer_rejects_unpaired_utf16_surrogates()
    {
        Assert.Throws<ArgumentException>(() => BaseTextAnalyzer.Analyze("bad\ud800value"));
    }

    [Theory]
    [InlineData("\u212B", "å")]
    [InlineData("\uFB03", "ffi")]
    [InlineData("\u2460", "1")]
    [InlineData("가", "가")]
    [InlineData("A\u030A", "å")]
    public void Analyzer_uses_pinned_nfkc_and_full_fold(string source, string expected) => Assert.Equal([expected], BaseTextAnalyzer.Analyze(source).ToArray());

    [Fact]
    public void Query_canonicalization_is_commutative_and_deduplicates_children()
    {
        BaseTextQuery first = BaseTextQuery.All(
            BaseTextQuery.Token("alpha"),
            BaseTextQuery.Token("beta"),
            BaseTextQuery.Token("alpha"));
        BaseTextQuery second = BaseTextQuery.All(
            BaseTextQuery.Token("beta"),
            BaseTextQuery.Token("alpha"));

        Assert.True(BaseTextQueryContract.Encode(first).AsSpan().SequenceEqual(BaseTextQueryContract.Encode(second).AsSpan()));
        Assert.Equal(2, Assert.IsType<BaseTextQuery.And>(first).Children.Length);
    }

    [Fact]
    public void Query_rejects_unanchored_negative_nodes()
    {
        BaseTextQuery negative = BaseTextQuery.Exclude(BaseTextQuery.Token("secret"));

        Assert.Throws<ArgumentException>(() => BaseTextQueryContract.Validate(negative));
        BaseTextQuery anchored = BaseTextQuery.All(BaseTextQuery.Token("public"), negative);
        Assert.Same(anchored, BaseTextQueryContract.Validate(anchored));
    }

    [Theory]
    [InlineData(1, 1, 0, 1_692_308UL)]
    [InlineData(8, 3, 10, 4_292_680UL)]
    public void Scoring_uses_exact_candidate_local_integer_formula(
        int weight,
        int termFrequency,
        int fieldLength,
        ulong expected)
    {
        Assert.Equal(expected, BaseTextScoring.Feature(weight, termFrequency, fieldLength));
    }

    [Fact]
    public void Scoring_rounds_exact_halves_to_even()
    {
        Assert.Equal((UInt128)2, BaseTextScoring.RoundHalfEven(5, 2));
        Assert.Equal((UInt128)4, BaseTextScoring.RoundHalfEven(7, 2));
    }

    [Fact]
    public void Generated_text_index_uses_serializer_bound_fields_and_canonical_identity()
    {
        BaseTextIndexDefinition index = TextSemanticDocument.TextIndexes.Content.Definition;

        Assert.Equal("text.semantic.content.v1", index.Id);
        Assert.Equal(["text.semantic.title", "text.semantic.body"], index.Fields.Select(static field => field.StableFieldId));
        Assert.Equal(32, index.DefinitionChecksum.Length);
    }

    [Fact]
    public async Task Typed_inmemory_search_ranks_filters_hydrates_and_pages()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.allow", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.allow.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 });
            (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("text-test"); builder.AddStaticGrantAuthority(definition, grant);
            builder.AddCollection(TextSemanticDocument.Collection);
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "text-test" }).Collection(TextSemanticDocument.Collection);
        await collection.CreateAsync(new RecordId("a"), new TextSemanticDocument { Title = "Distributed systems", Body = "portable search", State = "published" });
        await collection.CreateAsync(new RecordId("b"), new TextSemanticDocument { Title = "Systems", Body = "distributed distributed", State = "draft" });
        await collection.CreateAsync(new RecordId("c"), new TextSemanticDocument { Title = "Distributed", Body = "systems", State = "published" });

        BaseTextResult<TextSemanticDocument> first = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed"))
            .Where(TextSemanticDocument.Fields.State, "published").Take(1).ExecuteAsync()).RequireValue();
        BaseTextResult<TextSemanticDocument> second = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed"))
            .Where(TextSemanticDocument.Fields.State, "published").Take(1).After(first.Next!.Value).ExecuteAsync()).RequireValue();

        Assert.Single(first.Matches); Assert.Single(second.Matches); Assert.NotEqual(first.Matches[0].Record.Id, second.Matches[0].Record.Id);
        Assert.All(first.Matches.Concat(second.Matches), static match => Assert.Equal("published", match.Record.Value.State));
        BaseTextResult<TextSemanticDocument> atLeast = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed"))
            .Take(10).WithConsistency(new BaseTextConsistencyRequirement.AtLeast(first.Consistency)).ExecuteAsync()).RequireValue();
        Assert.Equal(3, atLeast.Matches.Length);
    }


    [Fact]
    public async Task Query_requires_the_exact_index_grant_before_provider_use()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.no-grant", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.no-grant.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 }).AddCollection(TextSemanticDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "no-grant" }).Collection(TextSemanticDocument.Collection);
        BaseResult<BaseTextResult<TextSemanticDocument>> result = await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("anything")).Take(1).ExecuteAsync();
        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
    }

    [Fact]
    public async Task Inmemory_projection_rejects_oversized_indexed_text_atomically()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.limit", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.limit.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 }).AddCollection(TextSemanticDocument.Collection));
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin }).Collection(TextSemanticDocument.Collection);
        BaseResult<BaseRecord<TextSemanticDocument>> created = await collection.CreateAsync(new RecordId("too-large"), new TextSemanticDocument { Title = new string('a', 65), Body = "body", State = "published" });
        Assert.Equal(OperationStatus.ValidationFailed, created.Status); Assert.Equal(OperationStatus.NotFound, (await collection.GetAsync(new RecordId("too-large"))).Status);
    }

    [Fact]
    public async Task Sqlite_search_uses_one_authoritative_snapshot_for_ranking_and_hydration()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-text-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder =>
            {
                builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x49, 32).ToArray())
                    .UseStore(SqliteStore.Configure(options => options.ConnectionString = $"Data Source={path}"))
                    .AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.sqlite.allow", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.sqlite.allow.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 });
                (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("sqlite-text"); builder.AddStaticGrantAuthority(definition, grant);
                builder.AddCollection(TextSemanticDocument.Collection);
            });
            await using ServiceProvider provider = services.BuildServiceProvider(); IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>(); BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!; Assert.True((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess()); OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync(); Assert.True(readiness.IsSuccess(), readiness.Error?.Code + ":" + readiness.Error?.Message);
            BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "sqlite-text" }).Collection(TextSemanticDocument.Collection);
            await collection.CreateAsync(new RecordId("sqlite"), new TextSemanticDocument { Title = "Portable lexical search", Body = "SQLite snapshot", State = "published" });
            BaseTextResult<TextSemanticDocument> result = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.ExactPhrase("lexical", "search")).Take(10).ExecuteAsync()).RequireValue();
            Assert.Single(result.Matches); Assert.Equal("sqlite", result.Matches[0].Record.Id.Value);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static (BaseGrantAuthorityDefinition Definition, AccessGrant Grant) TextGrant(string subject) =>
        (new BaseGrantAuthorityDefinition { Id = BaseTextGrants.Query, Version = 1, OwningModuleId = "tests", SourceContractId = "tests.text.grants", SourceContractVersion = 1 },
        new AccessGrant
        {
            Id = BaseTextGrants.Query, ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application,
            Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = subject }, Action = BaseTextGrants.Query,
            Scope = new ResourceScope { Kind = ResourceScopeKind.TextIndex, CollectionId = "text-semantic-documents", TextIndexId = "text.semantic.content.v1" },
        });
}

internal sealed class AllowTextPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}

[BaseCollection("text-semantic-documents", typeof(TextSemanticJsonContext))]
[BaseTextIndex("text.semantic.content.v1", Fields = [nameof(TextSemanticDocument.Title), nameof(TextSemanticDocument.Body)], Weights = [4, 1], FilterFields = [nameof(TextSemanticDocument.State)])]
internal sealed partial record TextSemanticDocument
{
    [BaseField("text.semantic.title")] public required string Title { get; init; }
    [BaseField("text.semantic.body")] public required string Body { get; init; }
    [BaseField("text.semantic.state", Operators = BaseFieldOperator.Equal)] public required string State { get; init; }
}

[JsonSerializable(typeof(TextSemanticDocument))]
internal sealed partial class TextSemanticJsonContext : JsonSerializerContext;
