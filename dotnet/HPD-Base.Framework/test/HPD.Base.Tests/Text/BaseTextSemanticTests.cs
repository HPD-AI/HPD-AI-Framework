using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    public void Cursor_binds_complete_snapshot_policy_and_expiry_authority()
    {
        var clock = new MutableTextClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        using var protector = new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions { ActiveKey = new BaseOpaqueTokenKey { Id = 49, Key = Enumerable.Repeat((byte)0x49, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch } }), clock);
        var codec = new BaseTextCursorCodec(protector, clock);
        BaseTextAuthoritySnapshot snapshot = TextSnapshot();
        byte[] query = System.Security.Cryptography.SHA256.HashData("query"u8), constraint = System.Security.Cryptography.SHA256.HashData("constraint"u8), authority = System.Security.Cryptography.SHA256.HashData("authority"u8), boundary = [1, 2, 3];
        BaseTextCursor cursor = codec.Issue(snapshot, query, constraint, authority, boundary);

        Assert.Equal(BaseTextCursorReadStatus.Valid, codec.Read(cursor, snapshot, query, constraint, authority, out ImmutableArray<byte> decoded));
        Assert.Equal(boundary, decoded.ToArray());
        Assert.Equal(BaseTextCursorReadStatus.ScopeMismatch, codec.Read(cursor, snapshot with { PurgeGeneration = 8 }, query, constraint, authority, out _));
        Assert.Equal(BaseTextCursorReadStatus.ScopeMismatch, codec.Read(cursor, snapshot, query, constraint, System.Security.Cryptography.SHA256.HashData("changed-policy"u8), out _));
        clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(BaseTextCursorReadStatus.Expired, codec.Read(cursor, snapshot, query, constraint, authority, out _));
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
    public void Manual_text_index_uses_frozen_serializer_handles_and_canonical_sealing()
    {
        var metadata = new TextSemanticJsonContext(BaseSerializerGeneratedContract.CreateOptions(null)).TextSemanticDocument;
        BaseJsonProperty<TextSemanticDocument, string> title = BaseJsonProperty<TextSemanticDocument, string>.Bind(metadata, "Title");
        BaseJsonProperty<TextSemanticDocument, string> body = BaseJsonProperty<TextSemanticDocument, string>.Bind(metadata, "Body");
        BaseJsonProperty<TextSemanticDocument, string> state = BaseJsonProperty<TextSemanticDocument, string>.Bind(metadata, "State");
        BaseCollection<TextSemanticDocument> manual = BaseCollection.Define("manual-text-documents", metadata, collection =>
        {
            collection.String("text.semantic.title", nameof(TextSemanticDocument.Title), title).Required();
            collection.String("text.semantic.body", nameof(TextSemanticDocument.Body), body).Required();
            collection.String("text.semantic.state", nameof(TextSemanticDocument.State), state).Required();
            collection.TextIndex("manual.text.content.v1", 1, index => index.Analyzer(BaseTextAnalyzers.UnicodeCaseFoldedV1).Field(title, 4).Field(body, 1).FilterField(state).Audience(HPDBaseEndpointAudience.Application).Limits(BaseTextPlatform.DefaultLimits));
        });

        BaseTextIndexDefinition definition = Assert.Single(manual.Definition.TextIndexes!);
        Assert.Equal(["text.semantic.title", "text.semantic.body"], definition.Fields.Select(static value => value.StableFieldId));
        Assert.Equal(32, definition.DefinitionChecksum.Length);
    }

    [Fact]
    public async Task Typed_inmemory_search_ranks_filters_hydrates_and_pages()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddDependencies(options => options.ProtectionKey = Enumerable.Repeat((byte)0x49, 32).ToArray()).AddLiveQueries();
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
        using (var liveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await using IAsyncEnumerator<BaseLiveQueryTransition<BaseTextResult<TextSemanticDocument>>> live = collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed")).Take(10).LiveAsync(liveTimeout.Token).GetAsyncEnumerator(liveTimeout.Token);
            Assert.True(await live.MoveNextAsync()); Assert.Equal(BaseLiveQueryTransitionKind.Snapshot, live.Current.Kind); Assert.Equal(3, live.Current.Value!.Matches.Length);
            await collection.CreateAsync(new RecordId("d"), new TextSemanticDocument { Title = "Distributed runtime", Body = "systems", State = "published" }, cancellationToken: liveTimeout.Token);
            Assert.True(await live.MoveNextAsync()); Assert.Equal(BaseLiveQueryTransitionKind.Snapshot, live.Current.Kind); Assert.Equal(4, live.Current.Value!.Matches.Length);
        }
        IBaseTextAdministration administration = provider.GetRequiredService<IBaseTextAdministration>(); BaseTextIndexStatus before = (await administration.GetAsync(TextSemanticDocument.Collection.Id, TextSemanticDocument.TextIndexes.Content.Definition.Id)).Value!;
        BaseTextRebuildRequest rebuild = Rebuild(before.Generation, "inmemory"); BaseTextRebuildResult rebuilt = (await administration.RebuildAsync(rebuild)).Value!; Assert.Equal(before.Generation + 1, rebuilt.PublishedGeneration); Assert.Equal(4, rebuilt.RecordCount);
        BaseTextRebuildResult duplicate = (await administration.RebuildAsync(rebuild)).Value!; Assert.Equal(rebuilt.PublishedGeneration, duplicate.PublishedGeneration); Assert.True(rebuilt.PublicationChecksum.AsSpan().SequenceEqual(duplicate.PublicationChecksum.AsSpan()));
        Assert.Equal(OperationStatus.Conflict, (await administration.RebuildAsync(Rebuild(before.Generation, "inmemory", "changed"))).Status);
    }


    [Fact]
    public async Task Query_requires_the_exact_index_grant_before_provider_use()
    {
        var services = new ServiceCollection().AddLogging(); var tracking = new TrackingTextAuthority(malformed: false);
        services.AddHPDBase(builder => builder.AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.no-grant", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.no-grant.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 }).AddCollection(TextSemanticDocument.Collection));
        services.RemoveAll<IBaseTextProvider>(); services.AddSingleton<IBaseTextProvider>(tracking);
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "no-grant" }).Collection(TextSemanticDocument.Collection);
        BaseResult<BaseTextResult<TextSemanticDocument>> result = await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("anything")).Take(1).ExecuteAsync();
        Assert.Equal(OperationStatus.PolicyDenied, result.Status); Assert.Equal(0, tracking.OpenCount);
    }

    [Fact]
    public async Task Runtime_rejects_a_forged_index_contract_before_policy_or_provider_influence()
    {
        var services = new ServiceCollection().AddLogging(); var tracking = new TrackingTextAuthority(malformed: false);
        services.AddHPDBase(builder => builder.AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.forged", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.forged.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 }).AddCollection(TextSemanticDocument.Collection));
        services.RemoveAll<IBaseTextProvider>(); services.AddSingleton<IBaseTextProvider>(tracking);
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseTextIndexDefinition installed = TextSemanticDocument.TextIndexes.Content.Definition;
        BaseTextIndexDefinition forged = installed with { Limits = installed.Limits with { MaximumResults = installed.Limits.MaximumResults - 1 } };
        OperationResult<BaseTextRuntimeResult> result = await provider.GetRequiredService<IBaseTextRuntime>().ExecuteAsync(new BaseTextRuntimeRequest
        {
            Collection = TextSemanticDocument.Collection.Definition, Index = forged, Query = BaseTextQuery.Token("anything"), Constraint = new BaseTextCandidateConstraint.True(), Take = 1, Consistency = new BaseTextConsistencyRequirement.Current(), Principal = new() { AuthenticationState = PrincipalAuthenticationState.Admin }, Operation = new() { ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application, Operation = BaseOperationKind.TextQuery, CollectionId = TextSemanticDocument.Collection.Id },
        }, default);
        Assert.Equal(OperationStatus.ValidationFailed, result.Status); Assert.Equal(BaseTextErrorCodes.ContractInvalid, result.Error?.Code); Assert.Equal(0, tracking.OpenCount);
    }

    [Fact]
    public async Task Dynamic_field_influence_is_required_and_rechecked_before_matching()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.AddPolicyAuthority<DynamicTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.dynamic", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.dynamic.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 });
            (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("dynamic", DynamicTextDocument.Collection.Id, DynamicTextDocument.TextIndexes.Content.Definition.Id); builder.AddStaticGrantAuthority(definition, grant);
            builder.AddCollection(DynamicTextDocument.Collection);
        });
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseCollectionSession<DynamicTextDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "dynamic" }).Collection(DynamicTextDocument.Collection);
        await collection.CreateAsync(new("allowed"), new DynamicTextDocument { InternalTitle = "hidden keyword", State = "published" });
        await collection.CreateAsync(new("denied"), new DynamicTextDocument { InternalTitle = "hidden keyword", State = "draft" });

        BaseTextResult<DynamicTextDocument> result = (await collection.Text(DynamicTextDocument.TextIndexes.Content, BaseTextQuery.Token("hidden")).Take(10).ExecuteAsync()).RequireValue();
        Assert.Equal(["allowed"], result.Matches.Select(static value => value.Record.Id.Value));
    }

    [Fact]
    public async Task Runtime_rejects_hostile_candidate_ordering_before_hydration()
    {
        var services = new ServiceCollection().AddLogging(); var hostile = new TrackingTextAuthority(malformed: true);
        services.AddHPDBase(builder => { builder.AddPolicyAuthority<AllowTextPolicy>(new() { Id = "text.hostile", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.hostile.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 }); (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("hostile"); builder.AddStaticGrantAuthority(definition, grant); builder.AddCollection(TextSemanticDocument.Collection); });
        services.RemoveAll<IBaseTextProvider>(); services.AddSingleton<IBaseTextProvider>(hostile);
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess()); BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "hostile" }).Collection(TextSemanticDocument.Collection);
        BaseResult<BaseTextResult<TextSemanticDocument>> result = await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("anything")).Take(1).ExecuteAsync(); Assert.Equal(OperationStatus.StoreError, result.Status); Assert.Equal(BaseTextErrorCodes.ProviderContractInvalid, Assert.IsType<BaseFailure<BaseTextResult<TextSemanticDocument>>>(result).Error.Code); Assert.Equal(0, hostile.HydrationCount);
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
        string temporary = Path.GetTempPath(); if (temporary.StartsWith("/var/", StringComparison.Ordinal)) temporary = "/private" + temporary; string path = Path.Combine(temporary, "hpd-base-text-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder =>
            {
                builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x49, 32).ToArray())
                    .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 49, Key = Enumerable.Repeat((byte)0x49, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch })
                    .UseStore(SqliteStore.Configure(options => { options.StoreId = "sqlite"; options.DataSource = path; options.AdministrationEnabled = true; }))
                    .AddPolicyAuthority<AllowTextPolicy>(new BasePolicyAuthorityDefinition { Id = "text.sqlite.allow", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.sqlite.allow.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 });
                (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("sqlite-text"); builder.AddStaticGrantAuthority(definition, grant);
                builder.AddCollection(TextSemanticDocument.Collection);
            });
            await using ServiceProvider provider = services.BuildServiceProvider(); IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>(); BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!; Assert.True((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess()); OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync(); Assert.True(readiness.IsSuccess(), readiness.Error?.Code + ":" + readiness.Error?.Message);
            BaseCollectionSession<TextSemanticDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "sqlite-text" }).Collection(TextSemanticDocument.Collection);
            await collection.CreateAsync(new RecordId("sqlite"), new TextSemanticDocument { Title = "Portable lexical search", Body = "SQLite snapshot", State = "published" });
            await collection.CreateAsync(new RecordId("sqlite-draft"), new TextSemanticDocument { Title = "Portable lexical search", Body = "SQLite hidden draft", State = "draft" });
            BaseTextResult<TextSemanticDocument> result = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.ExactPhrase("lexical", "search")).Take(10).ExecuteAsync()).RequireValue();
            Assert.Equal(2, result.Matches.Length);
            BaseTextResult<TextSemanticDocument> filtered = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.All(BaseTextQuery.Token("portable"), BaseTextQuery.StartsWith("lex"))).Where(TextSemanticDocument.Fields.State, "published").Take(10).ExecuteAsync()).RequireValue();
            Assert.Single(filtered.Matches); Assert.Equal("sqlite", filtered.Matches[0].Record.Id.Value);
            IBaseTextAdministration administration = provider.GetRequiredService<IBaseTextAdministration>(); BaseTextIndexStatus before = (await administration.GetAsync(TextSemanticDocument.Collection.Id, TextSemanticDocument.TextIndexes.Content.Definition.Id)).Value!; BaseTextRebuildRequest rebuild = Rebuild(before.Generation, "sqlite"); BaseTextRebuildResult rebuilt = (await administration.RebuildAsync(rebuild)).Value!; Assert.Equal(2, rebuilt.PublishedGeneration); Assert.Equal(2, rebuilt.RecordCount);
            BaseTextRebuildResult duplicate = (await administration.RebuildAsync(rebuild)).Value!; Assert.Equal(rebuilt.PublishedGeneration, duplicate.PublishedGeneration); Assert.True(rebuilt.PublicationChecksum.AsSpan().SequenceEqual(duplicate.PublicationChecksum.AsSpan()));
            Assert.Equal(OperationStatus.Conflict, (await administration.RebuildAsync(Rebuild(before.Generation, "sqlite", "changed"))).Status);
            IHPDBaseApplication application = provider.GetRequiredService<IHPDBaseApplication>(); Assert.True(provider.GetRequiredService<SqliteRecordStore>().AdministrationCapability.Backup); var artifact = new MemoryStream(); var administrator = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.System }; BaseBackupManifest manifest = (await application.Administration.CreateBackupAsync(artifact, new() { StoreId = "sqlite", Principal = administrator })).RequireValue();
            byte[] corrupted = artifact.ToArray(); corrupted[corrupted.Length / 2] ^= 0x20; Assert.Equal(BaseAdministrationErrorCodes.ArtifactInvalid, Assert.IsType<BaseFailure<BaseBackupManifest>>(await application.Administration.ValidateBackupAsync(new MemoryStream(corrupted), new() { StoreId = "sqlite", Principal = administrator })).Error.Code);
            (await collection.ReplaceAsync(new("sqlite"), new TextSemanticDocument { Title = "Changed record", Body = "different", State = "published" })).RequireValue(); artifact.Position = 0;
            (await application.Administration.RestoreAsync(artifact, new() { StoreId = "sqlite", Principal = administrator, ExpectedCurrentStoreIdentityDigest = manifest.StoreIdentityDigest, ExpectedArtifactStoreIdentityDigest = manifest.StoreIdentityDigest, IdentityMode = BaseRestoreIdentityMode.RequireCurrentStoreIdentity, RecoveryImageRetention = BaseRecoveryImageRetention.DeleteAfterSuccessfulRestore, ConfirmDestructiveReplacement = true })).RequireValue();
            BaseTextResult<TextSemanticDocument> restored = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.ExactPhrase("lexical", "search")).Where(TextSemanticDocument.Fields.State, "published").Take(10).ExecuteAsync()).RequireValue(); Assert.Single(restored.Matches);
            Assert.Equal(rebuilt.PublishedGeneration, (await administration.RebuildAsync(rebuild)).Value!.PublishedGeneration);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static (BaseGrantAuthorityDefinition Definition, AccessGrant Grant) TextGrant(string subject, string collectionId = "text-semantic-documents", string indexId = "text.semantic.content.v1") =>
        (new BaseGrantAuthorityDefinition { Id = BaseTextGrants.Query, Version = 1, OwningModuleId = "tests", SourceContractId = "tests.text.grants", SourceContractVersion = 1 },
        new AccessGrant
        {
            Id = BaseTextGrants.Query, ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application,
            Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = subject }, Action = BaseTextGrants.Query,
            Scope = new ResourceScope { Kind = ResourceScopeKind.TextIndex, CollectionId = collectionId, TextIndexId = indexId },
        });
    private static BaseTextRebuildRequest Rebuild(long generation, string key, string? fingerprintSalt = null) => new() { CollectionId = TextSemanticDocument.Collection.Id, TextIndexId = TextSemanticDocument.TextIndexes.Content.Definition.Id, ExpectedGeneration = generation, Identity = BaseMutationRequestIdentity.Create("tests", "text.rebuild", key, BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprintSalt ?? key)))) };
    private static BaseTextAuthoritySnapshot TextSnapshot() => new() { StoreIdentityDigest = "store", RestoreEpoch = 1, SchemaGeneration = 2, CollectionId = "text-semantic-documents", PurgeGeneration = 3, TextIndexId = "text.semantic.content.v1", TextIndexVersion = 1, TextIndexGeneration = 4, AuthoritativeHead = new(5), AppliedThrough = new(5), SearchVisibleThrough = new(5), AnalyzerReceipt = BaseTextContractReceipts.AnalyzerReceipt, ScoringReceipt = BaseTextContractReceipts.ScoringReceipt };
}

file sealed class MutableTextClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    internal void Advance(TimeSpan value) => _now = _now.Add(value);
}

internal sealed class TrackingTextAuthority(bool malformed) : IBaseTextProvider, IBaseTextAuthority
{
    internal int OpenCount; internal int HydrationCount;
    public IBaseTextAuthority Authority => this;
    public BaseTextProviderDescriptor Descriptor { get; } = new() { Id = "tests.text", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional, Capability = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional), NativeDependencyReceipts = [], CertificationReceipt = ImmutableArray.Create(new byte[32]) };
    public ValueTask<OperationResult<IBaseTextHydrationSession>> OpenAsync(BaseTextAuthorityOpenRequest request, CancellationToken cancellationToken = default) { OpenCount++; return ValueTask.FromResult(OperationResults.Ok<IBaseTextHydrationSession>(new Session(this, request, malformed))); }
    public ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult(OperationResults.Ok(Array.Empty<BaseTextIndexStatus>()));
    public ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResults.NotFound<BaseTextIndexStatus>(new BaseError { Code = BaseTextErrorCodes.IndexUnavailable, Message = "Unavailable.", Category = ErrorCategory.NotFound }));
    public ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResults.NotFound<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.IndexUnavailable, Message = "Unavailable.", Category = ErrorCategory.NotFound }));
    private sealed class Session(TrackingTextAuthority owner, BaseTextAuthorityOpenRequest request, bool malformed) : IBaseTextHydrationSession
    {
        private sealed class Plan(BaseTextLoweringReceipt lowering, long queryBytes, long constraintBytes, long statementParameters) : BaseTextProviderPlan
        {
            internal BaseTextLoweringReceipt Lowering { get; } = lowering;
            internal long QueryBytes { get; } = queryBytes;
            internal long ConstraintBytes { get; } = constraintBytes;
            internal long StatementParameters { get; } = statementParameters;
        }
        public BaseTextAuthoritySnapshot Snapshot { get; } = new() { StoreIdentityDigest = "tests", RestoreEpoch = 1, SchemaGeneration = 1, CollectionId = request.CollectionId, PurgeGeneration = 0, TextIndexId = request.TextIndexId, TextIndexVersion = request.TextIndexVersion, TextIndexGeneration = 1, AuthoritativeHead = new(1), AppliedThrough = new(1), SearchVisibleThrough = new(1), AnalyzerReceipt = BaseTextContractReceipts.AnalyzerReceipt, ScoringReceipt = BaseTextContractReceipts.ScoringReceipt };
        public ValueTask<OperationResult<BaseTextConstraintPreparation>> PrepareAsync(BaseTextProviderPreparationRequest value, CancellationToken cancellationToken = default)
        {
            BaseTextLoweringReceipt receipt = BaseTextProviderEvidence.CreateLoweringReceipt(owner.Descriptor, Snapshot, value.Index, value.QueryDigest, value.ConstraintDigest, value.InfluenceConstraints, value.Limits);
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextConstraintPreparation { QueryDigest = value.QueryDigest, ConstraintDigest = value.ConstraintDigest, Enforcement = BaseTextConstraintEnforcement.CompleteBeforeMatchingAndRanking, Receipt = receipt, Plan = new Plan(receipt, BaseTextQueryContract.Encode(value.NormalizedQuery).Length, BaseTextSemanticEvaluator.ConstraintEncoding(value.Constraint).Length, BaseTextProviderEvidence.StatementParameterCount(value.NormalizedQuery, value.Constraint)) }));
        }
        public ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest value, CancellationToken cancellationToken = default)
        {
            BaseTextCandidate[] candidates = malformed ? [Candidate("z", 1), Candidate("a", 2)] : [];
            Plan plan = Assert.IsType<Plan>(value.Plan);
            long orderingBytes = candidates.Sum(static item => item.CanonicalOrderingBoundary.Length);
            long queryBytes = plan.QueryBytes;
            long constraintBytes = plan.ConstraintBytes;
            return ValueTask.FromResult(OperationResults.Ok(new BaseTextProviderResult
            {
                Snapshot = Snapshot,
                Candidates = [.. candidates],
                Completeness = BaseTextProviderEvidence.CreateCompleteness(owner.Descriptor, Snapshot, plan.Lowering, [.. candidates], value.TakePlusOne),
                Accounting = new() { InputBytes = queryBytes + constraintBytes, QueryBytes = queryBytes, ConstraintBytes = constraintBytes, StatementParameters = plan.StatementParameters, AuthorizedRecordsExamined = candidates.Length, PostingsExamined = candidates.Length, PrefixExpansionCount = 0, PrefixExpansionBytes = 0, ScoreProofBytes = candidates.Length * 32, CandidateCount = candidates.Length, OrderingBytes = orderingBytes, ExactHydrationBytes = 0, ResultBytes = 0, CursorBytes = 0, RetainedTransientBytes = queryBytes + constraintBytes + candidates.Length * 32 + orderingBytes, Elapsed = TimeSpan.Zero }
            }));
        }
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default) { owner.HydrationCount++; return ValueTask.FromResult(OperationResults.Ok(Array.Empty<RecordEnvelope>())); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static BaseTextCandidate Candidate(string id, ulong score) { var value = new BaseTextScore { Units = score }; return new() { RecordId = new(id), Revision = new("test:1"), IndexedPosition = new(1), Score = value, CanonicalOrderingBoundary = BaseTextSemanticEvaluator.OrderingBoundary(value, new(id)), ScoreProof = new() { Fields = [], Features = [], ProofDigest = ImmutableArray.Create(new byte[32]) } }; }
    }
}

internal sealed class AllowTextPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
}

internal sealed class DynamicTextPolicy : IPolicyEvaluator
{
    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(
        PolicyDecision.Allow().WithTextSearchInfluence("text.dynamic.title", new FilterExpression { Kind = FilterNodeKind.Compare, Field = "text.dynamic.state", Operator = FilterOperator.Equal, Value = new QueryValue { Kind = QueryValueKind.String, String = "published" } }));
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

[BaseCollection("text-dynamic-documents", typeof(DynamicTextJsonContext))]
[BaseTextIndex("text.dynamic.content.v1", Fields = [nameof(DynamicTextDocument.InternalTitle)], Weights = [1], FilterFields = [nameof(DynamicTextDocument.State)])]
internal sealed partial record DynamicTextDocument
{
    [BaseField("text.dynamic.title")]
    [BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)]
    public required string InternalTitle { get; init; }
    [BaseField("text.dynamic.state", Operators = BaseFieldOperator.Equal)]
    public required string State { get; init; }
}

[JsonSerializable(typeof(DynamicTextDocument))]
internal sealed partial class DynamicTextJsonContext : JsonSerializerContext;
