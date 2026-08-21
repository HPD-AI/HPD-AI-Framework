using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using HPD.Base.Sqlite;

namespace HPD.Base.Tests.Text;

public sealed class BaseTextSemanticTests
{
    public static TheoryData<string> TextLimitNames => new()
    {
        nameof(BaseTextExecutionLimits.MaximumQueryNodes), nameof(BaseTextExecutionLimits.MaximumQueryDepth), nameof(BaseTextExecutionLimits.MaximumPhraseTerms), nameof(BaseTextExecutionLimits.MaximumQueryBytes),
        nameof(BaseTextExecutionLimits.MaximumFilterNodes), nameof(BaseTextExecutionLimits.MaximumFilterDepth), nameof(BaseTextExecutionLimits.MaximumFilterLiterals), nameof(BaseTextExecutionLimits.MaximumInValues),
        nameof(BaseTextExecutionLimits.MaximumPrefixExpansions), nameof(BaseTextExecutionLimits.MaximumPrefixExpansionBytes), nameof(BaseTextExecutionLimits.MaximumSecondaryOrderFields), nameof(BaseTextExecutionLimits.MaximumOrderingBytes),
        nameof(BaseTextExecutionLimits.MaximumCandidates), nameof(BaseTextExecutionLimits.MaximumScoreProofBytes), nameof(BaseTextExecutionLimits.MaximumTokensPerField), nameof(BaseTextExecutionLimits.MaximumNormalizedBytesPerField),
        nameof(BaseTextExecutionLimits.MaximumNormalizedBytesPerRecord), nameof(BaseTextExecutionLimits.MaximumResults), nameof(BaseTextExecutionLimits.MaximumResultBytes), nameof(BaseTextExecutionLimits.MaximumCursorBytes),
        nameof(BaseTextExecutionLimits.MaximumStatementParameters), nameof(BaseTextExecutionLimits.MaximumTransientBytes), nameof(BaseTextExecutionLimits.QueryTimeout), nameof(BaseTextExecutionLimits.ConsistencyWaitTimeout),
    };

    [Theory]
    [MemberData(nameof(TextLimitNames))]
    public void Every_text_platform_limit_accepts_the_exact_ceiling_and_rejects_plus_one(string member)
    {
        BaseTextIndexDefinition definition = BaseTextCertificationSchemaRecord.TextIndexes.Content.Definition;
        BaseTextExecutionLimits exact = BaseTextPlatform.DefaultLimits;
        Assert.NotEmpty(BaseTextIndexContract.Seal(definition with { Limits = SetLimit(exact, member, false), DefinitionChecksum = [] }).DefinitionChecksum);
        Assert.Throws<InvalidOperationException>(() => BaseTextIndexContract.Seal(definition with { Limits = SetLimit(exact, member, true), DefinitionChecksum = [] }));
    }

    [Fact]
    public async Task Noncooperative_provider_work_is_bounded_quarantined_and_released()
    {
        await using var state = new BaseTextOperationalState();
        var late = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await state.InvokeAsync<int>(
                _ => new ValueTask<int>(late.Task),
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));

        Assert.Equal(0, state.Active);
        Assert.Equal(1, state.Quarantined);

        late.SetResult(42);
        await WaitUntilAsync(() => state.Quarantined == 0, TimeSpan.FromSeconds(2));

        int recovered = await state.InvokeAsync(
            _ => ValueTask.FromResult(7),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        Assert.Equal(7, recovered);
        Assert.Equal(0, state.Active);
        Assert.Equal(0, state.Quarantined);
    }

    [Fact]
    public async Task Certification_noncooperative_gate_has_exact_release_identity()
    {
        var controller = new BaseTextCertificationFaultController([
            new BaseTextCertificationFaultSchedule
            {
                Fault = BaseTextCertificationFault.QueryNonCooperative,
                Occurrence = 1,
                Delay = TimeSpan.Zero,
                PartialSuccessCount = 0,
            },
        ]);
        controller.Activate();
        BaseTextCertificationFaultSchedule fault = Assert.IsType<BaseTextCertificationFaultSchedule>(controller.Next(BaseTextCertificationOperationKind.Query));
        Task retained = controller.BeforeAsync(BaseTextCertificationOperationKind.Query, fault, CancellationToken.None).AsTask();
        await WaitUntilAsync(() => controller.RetainedCount == 1, TimeSpan.FromSeconds(2));

        BaseTextCertificationLateWorkResult wrong = controller.Release(BaseTextCertificationOperationKind.Rebuild, 1);
        Assert.False(wrong.WasRetained);
        Assert.False(retained.IsCompleted);

        BaseTextCertificationLateWorkResult released = controller.Release(BaseTextCertificationOperationKind.Query, 1);
        Assert.True(released.WasRetained);
        Assert.True(released.Released);
        await retained.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, controller.RetainedCount);
        Assert.Equal(BaseTextCertificationFault.QueryNonCooperative, Assert.Single(controller.Consumed));
    }

    [Fact]
    public async Task Certification_fixture_retains_and_releases_noncooperative_query()
    {
        var fixture = new BaseInMemoryTextCertificationFixture();
        await using IBaseTextCertificationHost host = await fixture.CreateAsync(new()
        {
            ProtocolVersion = BaseTextProviderCertification.ProtocolVersion,
            ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
            Plan = BaseTextCertificationPlan.Local,
            Limits = BaseTextPlatform.DefaultLimits,
            TimeProvider = TimeProvider.System,
            TokenKeys = [new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)0x5a, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }],
            Faults = [new BaseTextCertificationFaultSchedule { Fault = BaseTextCertificationFault.QueryNonCooperative, Occurrence = 1, Delay = TimeSpan.Zero, PartialSuccessCount = 0 }],
        }, CancellationToken.None);
        await host.Authority.SeedAsync(new()
        {
            Records = [new BaseTextCertificationRecord { Id = "held", Tenant = "tenant", Active = true, Priority = 1, Optional = null, Title = "held query", Body = "certification" }],
        }, CancellationToken.None);
        Task<BaseTextCertificationOperationResult> query = host.ExecuteAsync(new BaseTextCertificationOperation.Query(new()
        {
            IndexId = "base.testing.text.content.v1",
            Query = new BaseTextHttpQueryNode { Kind = "term", Value = "held" },
            Order = [],
            Take = 1,
            Consistency = "current",
        }), CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<TimeoutException>(() => query.WaitAsync(TimeSpan.FromMilliseconds(100)));
        BaseTextCertificationFaultState state = await host.Provider.InspectFaultAsync(CancellationToken.None);
        Assert.Equal(BaseTextCertificationFault.QueryNonCooperative, Assert.Single(state.Consumed));
        BaseTextCertificationShutdownResult blocked = await host.ShutdownAsync(new() { MaximumWait = TimeSpan.FromMilliseconds(20) }, CancellationToken.None);
        Assert.False(blocked.Completed);
        Assert.Equal(1, blocked.RetainedOperationCount);

        BaseTextCertificationLateWorkResult released = await host.Provider.ReleaseLateWorkAsync(BaseTextCertificationOperationKind.Query, 1, CancellationToken.None);
        Assert.True(released.Released);
        Assert.True((await query.WaitAsync(TimeSpan.FromSeconds(2))).Status.IsSuccess());
        Assert.True((await host.ShutdownAsync(new() { MaximumWait = TimeSpan.FromSeconds(1) }, CancellationToken.None)).Completed);
    }

    [Fact]
    public async Task Inmemory_provider_passes_the_public_text_certification_corpus()
    {
        BaseTextCertificationReport report = await BaseTextProviderCertification.RunAsync(new BaseInMemoryTextCertificationFixture(), new()
        {
            ProtocolVersion = BaseTextProviderCertification.ProtocolVersion,
            ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
            Plan = BaseTextCertificationPlan.Local,
            Limits = BaseTextPlatform.DefaultLimits,
            TimeProvider = TimeProvider.System,
            TokenKeys = [new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)0x5a, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }],
            Faults = [],
        });
        Assert.True(report.Passed, string.Join(Environment.NewLine, report.Cases.Where(static value => !value.Passed).Select(static value => value.Id + ":" + value.ErrorCode)));
        Assert.Equal("2bf43e5121621ae4522185dbdf8b81d42e7bdcfa7822aeff3e9c7fbdc7e08cbc", Convert.ToHexStringLower(report.ReportChecksum.AsSpan()));
    }

    [Fact]
    public async Task Sqlite_provider_passes_the_same_public_text_certification_corpus()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-text-cert-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var fixture = new BaseTextCertificationFixture("sqlite.fts5", 1, BaseTextProviderClass.CoLocatedTransactional,
                builder => builder.UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; options.AdministrationEnabled = true; })), "sqlite", ["sqlite-bundled"]);
            BaseTextCertificationReport report = await BaseTextProviderCertification.RunAsync(fixture, new()
            {
                ProtocolVersion = BaseTextProviderCertification.ProtocolVersion,
                ProviderClass = BaseTextProviderClass.CoLocatedTransactional,
                Plan = BaseTextCertificationPlan.Local,
                Limits = BaseTextPlatform.DefaultLimits,
                TimeProvider = TimeProvider.System,
                TokenKeys = [new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)0x5a, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }],
                Faults = [],
            });
            Assert.True(report.Passed, string.Join(Environment.NewLine, report.Cases.Where(static value => !value.Passed).Select(static value => value.Id + ":" + value.ErrorCode)));
            Assert.Equal("d78b2587f7a6355ca0fedaa03231ac2029be1736684950330c50449177f94804", Convert.ToHexStringLower(report.ReportChecksum.AsSpan()));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The retained provider work did not leave quarantine.");
            await Task.Delay(10);
        }
    }

    private static BaseTextExecutionLimits SetLimit(BaseTextExecutionLimits value, string member, bool plusOne) => member switch
    {
        nameof(value.MaximumQueryNodes) => value with { MaximumQueryNodes = value.MaximumQueryNodes + (plusOne ? 1 : 0) },
        nameof(value.MaximumQueryDepth) => value with { MaximumQueryDepth = value.MaximumQueryDepth + (plusOne ? 1 : 0) },
        nameof(value.MaximumPhraseTerms) => value with { MaximumPhraseTerms = value.MaximumPhraseTerms + (plusOne ? 1 : 0) },
        nameof(value.MaximumQueryBytes) => value with { MaximumQueryBytes = value.MaximumQueryBytes + (plusOne ? 1 : 0) },
        nameof(value.MaximumFilterNodes) => value with { MaximumFilterNodes = value.MaximumFilterNodes + (plusOne ? 1 : 0) },
        nameof(value.MaximumFilterDepth) => value with { MaximumFilterDepth = value.MaximumFilterDepth + (plusOne ? 1 : 0) },
        nameof(value.MaximumFilterLiterals) => value with { MaximumFilterLiterals = value.MaximumFilterLiterals + (plusOne ? 1 : 0) },
        nameof(value.MaximumInValues) => value with { MaximumInValues = value.MaximumInValues + (plusOne ? 1 : 0) },
        nameof(value.MaximumPrefixExpansions) => value with { MaximumPrefixExpansions = value.MaximumPrefixExpansions + (plusOne ? 1 : 0) },
        nameof(value.MaximumPrefixExpansionBytes) => value with { MaximumPrefixExpansionBytes = value.MaximumPrefixExpansionBytes + (plusOne ? 1 : 0) },
        nameof(value.MaximumSecondaryOrderFields) => value with { MaximumSecondaryOrderFields = value.MaximumSecondaryOrderFields + (plusOne ? 1 : 0) },
        nameof(value.MaximumOrderingBytes) => value with { MaximumOrderingBytes = value.MaximumOrderingBytes + (plusOne ? 1 : 0) },
        nameof(value.MaximumCandidates) => value with { MaximumCandidates = value.MaximumCandidates + (plusOne ? 1 : 0) },
        nameof(value.MaximumScoreProofBytes) => value with { MaximumScoreProofBytes = value.MaximumScoreProofBytes + (plusOne ? 1 : 0) },
        nameof(value.MaximumTokensPerField) => value with { MaximumTokensPerField = value.MaximumTokensPerField + (plusOne ? 1 : 0) },
        nameof(value.MaximumNormalizedBytesPerField) => value with { MaximumNormalizedBytesPerField = value.MaximumNormalizedBytesPerField + (plusOne ? 1 : 0) },
        nameof(value.MaximumNormalizedBytesPerRecord) => value with { MaximumNormalizedBytesPerRecord = value.MaximumNormalizedBytesPerRecord + (plusOne ? 1 : 0) },
        nameof(value.MaximumResults) => value with { MaximumResults = value.MaximumResults + (plusOne ? 1 : 0), MaximumCandidates = value.MaximumCandidates + (plusOne ? 1 : 0) },
        nameof(value.MaximumResultBytes) => value with { MaximumResultBytes = value.MaximumResultBytes + (plusOne ? 1 : 0) },
        nameof(value.MaximumCursorBytes) => value with { MaximumCursorBytes = value.MaximumCursorBytes + (plusOne ? 1 : 0) },
        nameof(value.MaximumStatementParameters) => value with { MaximumStatementParameters = value.MaximumStatementParameters + (plusOne ? 1 : 0) },
        nameof(value.MaximumTransientBytes) => value with { MaximumTransientBytes = value.MaximumTransientBytes + (plusOne ? 1 : 0) },
        nameof(value.QueryTimeout) => value with { QueryTimeout = value.QueryTimeout + (plusOne ? TimeSpan.FromTicks(1) : TimeSpan.Zero) },
        nameof(value.ConsistencyWaitTimeout) => value with { ConsistencyWaitTimeout = value.ConsistencyWaitTimeout + (plusOne ? TimeSpan.FromTicks(1) : TimeSpan.Zero) },
        _ => throw new ArgumentOutOfRangeException(nameof(member)),
    };

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
        BaseResult<BaseRecord<TextSemanticDocument>> createdA = await collection.CreateAsync(new RecordId("a"), new TextSemanticDocument { Title = "Distributed systems", Body = "portable search", State = "published" });
        Assert.True(createdA is BaseSuccess<BaseRecord<TextSemanticDocument>>, createdA is BaseFailure<BaseRecord<TextSemanticDocument>> failed ? failed.Error.Code + ":" + failed.Error.Message : createdA.Status.ToString());
        (await collection.CreateAsync(new RecordId("b"), new TextSemanticDocument { Title = "Systems", Body = "distributed distributed", State = "draft" })).RequireValue();
        (await collection.CreateAsync(new RecordId("c"), new TextSemanticDocument { Title = "Distributed", Body = "systems", State = "published" })).RequireValue();
        (await collection.CreateAsync(new RecordId("order-a"), new TextSemanticDocument { Title = "Ordering", Body = "equal", State = "alpha" })).RequireValue();
        (await collection.CreateAsync(new RecordId("order-z"), new TextSemanticDocument { Title = "Ordering", Body = "equal", State = "zeta" })).RequireValue();

        BaseTextResult<TextSemanticDocument> first = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed"))
            .Where(TextSemanticDocument.Fields.State, "published").Take(1).ExecuteAsync()).RequireValue();
        BaseTextResult<TextSemanticDocument> second = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed"))
            .Where(TextSemanticDocument.Fields.State, "published").Take(1).After(first.Next!.Value).ExecuteAsync()).RequireValue();

        Assert.Single(first.Matches); Assert.Single(second.Matches); Assert.NotEqual(first.Matches[0].Record.Id, second.Matches[0].Record.Id);
        Assert.All(first.Matches.Concat(second.Matches), static match => Assert.Equal("published", match.Record.Value.State));
        BaseTextResult<TextSemanticDocument> atLeast = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed"))
            .Take(10).WithConsistency(new BaseTextConsistencyRequirement.AtLeast(first.Consistency)).ExecuteAsync()).RequireValue();
        Assert.Equal(3, atLeast.Matches.Length);
        BaseResult<BaseTextResult<TextSemanticDocument>> orderedResult = await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("ordering"))
            .ThenBy(TextSemanticDocument.Fields.State, QuerySortDirection.Desc).Take(10).ExecuteAsync();
        Assert.True(orderedResult is BaseSuccess<BaseTextResult<TextSemanticDocument>>, orderedResult is BaseFailure<BaseTextResult<TextSemanticDocument>> failure ? failure.Error.Code : orderedResult.Status.ToString());
        BaseTextResult<TextSemanticDocument> ordered = orderedResult.RequireValue();
        Assert.Equal(["order-z", "order-a"], ordered.Matches.Select(static match => match.Record.Id.Value));
        using (var liveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await using IAsyncEnumerator<BaseLiveQueryTransition<BaseTextResult<TextSemanticDocument>>> live = collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("distributed")).Take(10).LiveAsync(liveTimeout.Token).GetAsyncEnumerator(liveTimeout.Token);
            Assert.True(await live.MoveNextAsync()); Assert.Equal(BaseLiveQueryTransitionKind.Snapshot, live.Current.Kind); Assert.Equal(3, live.Current.Value!.Matches.Length);
            await collection.CreateAsync(new RecordId("d"), new TextSemanticDocument { Title = "Distributed runtime", Body = "systems", State = "published" }, cancellationToken: liveTimeout.Token);
            Assert.True(await live.MoveNextAsync()); Assert.Equal(BaseLiveQueryTransitionKind.Snapshot, live.Current.Kind); Assert.Equal(4, live.Current.Value!.Matches.Length);
        }
        IBaseTextAdministration administration = provider.GetRequiredService<IBaseTextAdministration>(); BaseTextIndexStatus before = (await administration.GetAsync(TextSemanticDocument.Collection.Id, TextSemanticDocument.TextIndexes.Content.Definition.Id)).Value!;
        BaseTextRebuildRequest rebuild = Rebuild(before.Generation, "inmemory"); BaseTextRebuildResult rebuilt = (await administration.RebuildAsync(rebuild)).Value!; Assert.Equal(before.Generation + 1, rebuilt.PublishedGeneration); Assert.Equal(6, rebuilt.RecordCount);
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
            Collection = TextSemanticDocument.Collection.Definition, Index = forged, Query = BaseTextQuery.Token("anything"), Constraint = new BaseTextCandidateConstraint.True(), Order = [], Take = 1, Consistency = new BaseTextConsistencyRequirement.Current(), Principal = new() { AuthenticationState = PrincipalAuthenticationState.Admin }, Operation = new() { ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application, Operation = BaseOperationKind.TextQuery, CollectionId = TextSemanticDocument.Collection.Id },
        }, default);
        Assert.Equal(OperationStatus.ValidationFailed, result.Status); Assert.Equal(BaseTextErrorCodes.ContractInvalid, result.Error?.Code); Assert.Equal(0, tracking.OpenCount);
    }

    [Fact]
    public async Task Runtime_rejects_a_malformed_candidate_constraint_before_provider_influence()
    {
        var services = new ServiceCollection().AddLogging(); var tracking = new TrackingTextAuthority(malformed: false);
        services.AddHPDBase(builder =>
        {
            builder.AddPolicyAuthority<AllowTextPolicy>(new() { Id = "text.invalid-filter", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.invalid-filter.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 });
            (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("invalid-filter"); builder.AddStaticGrantAuthority(definition, grant);
            builder.AddCollection(TextSemanticDocument.Collection);
        });
        services.RemoveAll<IBaseTextProvider>(); services.AddSingleton<IBaseTextProvider>(tracking);
        await using ServiceProvider provider = services.BuildServiceProvider(); Assert.True((await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess());
        BaseTextIndexDefinition index = TextSemanticDocument.TextIndexes.Content.Definition;
        var malformed = new BaseTextCandidateConstraint.Equal(
            new BaseTextFilterField(TextSemanticDocument.Fields.State.Id, BaseTextFilterValueKind.String),
            new BaseTextFilterValue { Kind = BaseTextFilterValueKind.String, StringValue = "published", BooleanValue = true });
        OperationResult<BaseTextRuntimeResult> result = await provider.GetRequiredService<IBaseTextRuntime>().ExecuteAsync(new()
        {
            Collection = TextSemanticDocument.Collection.Definition, Index = index, Query = BaseTextQuery.Token("anything"), Constraint = malformed, Order = [],
            Take = 1, Consistency = new BaseTextConsistencyRequirement.Current(), Principal = new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "invalid-filter" },
            Operation = new() { ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application, Operation = BaseOperationKind.TextQuery, CollectionId = TextSemanticDocument.Collection.Id },
        }, default);
        Assert.Equal(OperationStatus.ValidationFailed, result.Status); Assert.Equal(BaseTextErrorCodes.QueryInvalid, result.Error?.Code); Assert.Equal(0, tracking.OpenCount);
    }

    [Fact]
    public void Candidate_in_values_have_one_canonical_order_and_no_duplicates()
    {
        BaseTextFilterField field = new(TextSemanticDocument.Fields.State.Id, BaseTextFilterValueKind.String);
        BaseTextCandidateConstraint first = BaseTextConstraintContract.In(field, [BaseTextFilterValue.FromString("published"), BaseTextFilterValue.FromString("draft"), BaseTextFilterValue.FromString("published")]);
        BaseTextCandidateConstraint second = BaseTextConstraintContract.In(field, [BaseTextFilterValue.FromString("draft"), BaseTextFilterValue.FromString("published")]);
        Assert.True(BaseTextSemanticEvaluator.ConstraintEncoding(first).AsSpan().SequenceEqual(BaseTextSemanticEvaluator.ConstraintEncoding(second).AsSpan()));
        Assert.Equal(2, Assert.IsType<BaseTextCandidateConstraint.In>(first).Values.Length);
    }

    [Fact]
    public void Candidate_constraint_encoding_uses_the_locked_tags()
    {
        Assert.Equal([.. "HPDB-TEXT-CONSTRAINT-1\0"u8.ToArray(), (byte)1], BaseTextSemanticEvaluator.ConstraintEncoding(new BaseTextCandidateConstraint.True()).ToArray());
        BaseTextFilterField field = new(TextSemanticDocument.Fields.State.Id, BaseTextFilterValueKind.String);
        BaseTextCandidateConstraint equal = new BaseTextCandidateConstraint.Equal(field, BaseTextFilterValue.FromString("x"));
        byte[] encoded = BaseTextSemanticEvaluator.ConstraintNodeEncoding(equal).ToArray();
        Assert.Equal(7, encoded[0]);
        Assert.Equal(1, encoded[1 + 4 + System.Text.Encoding.UTF8.GetByteCount(field.StableFieldId)]);
        Assert.Equal(1, encoded[2 + 4 + System.Text.Encoding.UTF8.GetByteCount(field.StableFieldId)]);
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
    public async Task Sqlite_rejects_dynamic_field_influence_it_cannot_lower_before_match()
    {
        string path = Path.Combine(Path.GetTempPath(), "hpd-base-text-influence-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder =>
            {
                builder.ConfigureSchema(options => options.PlanProtectionKey = Enumerable.Repeat((byte)0x51, 32).ToArray()).ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 51, Key = Enumerable.Repeat((byte)0x51, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch });
                builder.UseStore(SqliteStore.Configure(options => { options.DataSource = path; options.StoreId = "sqlite"; options.AdministrationEnabled = true; }));
                builder.AddPolicyAuthority<DynamicTextPolicy>(new() { Id = "text.dynamic.sqlite", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "text.dynamic.sqlite.eval", EvaluatorContractVersion = 1, CompositionOrder = 0 });
                (BaseGrantAuthorityDefinition definition, AccessGrant grant) = TextGrant("dynamic-sqlite", DynamicTextDocument.Collection.Id, DynamicTextDocument.TextIndexes.Content.Definition.Id); builder.AddStaticGrantAuthority(definition, grant); builder.AddCollection(DynamicTextDocument.Collection);
            });
            await using ServiceProvider provider = services.BuildServiceProvider(); IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>(); BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "sqlite" })).Value!; Assert.True((await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess()); var initialized = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync(); Assert.True(initialized.IsSuccess(), initialized.Error?.Code + ":" + initialized.Error?.Message);
            BaseCollectionSession<DynamicTextDocument> collection = provider.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectKind = AccessSubjectKind.User, SubjectId = "dynamic-sqlite" }).Collection(DynamicTextDocument.Collection);
            await collection.CreateAsync(new("record"), new DynamicTextDocument { InternalTitle = "prohibited", State = "draft" });
            BaseResult<BaseTextResult<DynamicTextDocument>> result = await collection.Text(DynamicTextDocument.TextIndexes.Content, BaseTextQuery.Token("prohibited")).Take(10).ExecuteAsync();
            Assert.Equal(OperationStatus.Unsupported, result.Status); Assert.Equal(BaseTextErrorCodes.PolicyConstraintUnsupported, Assert.IsType<BaseFailure<BaseTextResult<DynamicTextDocument>>>(result).Error.Code);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Continuation_order_and_completeness_bind_the_requested_boundary()
    {
        var score = new BaseTextScore { Units = 10 }; ImmutableArray<BaseTextOrderingValue> values = [];
        BaseTextCandidate at = Candidate("b", score, values); BaseTextCandidate before = Candidate("a", score, values); BaseTextCandidate after = Candidate("c", score, values);
        Assert.False(BaseTextOrderingContract.IsStrictlyAfter(at, at.CanonicalOrderingBoundary, [])); Assert.False(BaseTextOrderingContract.IsStrictlyAfter(before, at.CanonicalOrderingBoundary, [])); Assert.True(BaseTextOrderingContract.IsStrictlyAfter(after, at.CanonicalOrderingBoundary, []));
        BaseTextProviderCapability capability = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional); ImmutableArray<byte> report = ImmutableArray.Create(new byte[32]); BaseTextProviderDescriptor descriptor = new() { Id = "tests.boundary", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional, Capability = capability, NativeDependencyReceipts = [], CertificationContractChecksum = BaseTextCertificationReceiptContract.ContractChecksum, CertificationReportChecksum = report, CertificationReceipt = BaseTextCertificationReceiptContract.Create("tests.boundary", 1, BaseTextProviderClass.CoLocatedTransactional, capability, [], report) };
        BaseTextAuthoritySnapshot snapshot = new() { StoreIdentityDigest = "tests", RestoreEpoch = 1, SchemaGeneration = 1, CollectionId = "records", PurgeGeneration = 0, TextIndexId = "index", TextIndexVersion = 1, TextIndexGeneration = 1, AuthoritativeHead = new(1), AppliedThrough = new(1), SearchVisibleThrough = new(1), AnalyzerReceipt = BaseTextContractReceipts.AnalyzerReceipt, ScoringReceipt = BaseTextContractReceipts.ScoringReceipt };
        BaseTextLoweringReceipt lowering = new() { ProviderId = descriptor.Id, ProviderVersion = 1, ProviderClass = descriptor.ProviderClass, AuthoritySnapshotDigest = ImmutableArray.Create(new byte[32]), IndexChecksum = ImmutableArray.Create(new byte[32]), QueryDigest = ImmutableArray.Create(new byte[32]), ConstraintDigest = ImmutableArray.Create(new byte[32]), InfluenceConstraintsDigest = ImmutableArray.Create(new byte[32]), StatementShapeDigest = ImmutableArray.Create(new byte[32]), OrderingDigest = ImmutableArray.Create(new byte[32]), LimitsDigest = ImmutableArray.Create(new byte[32]), CertificationReceiptDigest = ImmutableArray.Create(new byte[32]) };
        BaseTextCompletenessEvidence expected = BaseTextProviderEvidence.CreateCompleteness(descriptor, snapshot, lowering, [after], 2, at.CanonicalOrderingBoundary); BaseTextCompletenessEvidence substituted = BaseTextProviderEvidence.CreateCompleteness(descriptor, snapshot, lowering, [after], 2, before.CanonicalOrderingBoundary);
        Assert.False(BaseTextProviderEvidence.CompletenessEquals(expected, substituted));
        static BaseTextCandidate Candidate(string id, BaseTextScore score, ImmutableArray<BaseTextOrderingValue> values) => new() { RecordId = new(id), Revision = new("test:1"), IndexedPosition = new(1), Score = score, SecondaryOrdering = values, CanonicalOrderingBoundary = BaseTextOrderingContract.Boundary(score, values, new(id)), ScoreProof = new() { Fields = [], Features = [], ProofDigest = ImmutableArray.Create(new byte[32]) } };
    }

    [Fact]
    public void Certification_receipt_rejects_capability_and_native_dependency_substitution()
    {
        BaseTextProviderCapability capability = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional); ImmutableArray<byte> report = ImmutableArray.Create(Enumerable.Repeat((byte)7, 32).ToArray()); ImmutableArray<string> dependencies = ["native-v1"];
        BaseTextProviderDescriptor descriptor = new() { Id = "tests.certified", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional, Capability = capability, NativeDependencyReceipts = dependencies, CertificationContractChecksum = BaseTextCertificationReceiptContract.ContractChecksum, CertificationReportChecksum = report, CertificationReceipt = BaseTextCertificationReceiptContract.Create("tests.certified", 1, BaseTextProviderClass.CoLocatedTransactional, capability, dependencies, report) };
        Assert.True(BaseTextCertificationReceiptContract.Validate(descriptor)); Assert.False(BaseTextCertificationReceiptContract.Validate(descriptor with { Capability = capability with { MaximumCandidates = capability.MaximumCandidates - 1 } })); Assert.False(BaseTextCertificationReceiptContract.Validate(descriptor with { NativeDependencyReceipts = ["native-v2"] }));
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
            await collection.CreateAsync(new RecordId("sql-order-a"), new TextSemanticDocument { Title = "Ordering", Body = "equal", State = "alpha" });
            await collection.CreateAsync(new RecordId("sql-order-z"), new TextSemanticDocument { Title = "Ordering", Body = "equal", State = "zeta" });
            BaseTextResult<TextSemanticDocument> result = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.ExactPhrase("lexical", "search")).Take(10).ExecuteAsync()).RequireValue();
            Assert.Equal(2, result.Matches.Length);
            BaseTextResult<TextSemanticDocument> filtered = (await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.All(BaseTextQuery.Token("portable"), BaseTextQuery.StartsWith("lex"))).Where(TextSemanticDocument.Fields.State, "published").Take(10).ExecuteAsync()).RequireValue();
            Assert.Single(filtered.Matches); Assert.Equal("sqlite", filtered.Matches[0].Record.Id.Value);
            BaseResult<BaseTextResult<TextSemanticDocument>> orderedResult = await collection.Text(TextSemanticDocument.TextIndexes.Content, BaseTextQuery.Token("ordering")).ThenBy(TextSemanticDocument.Fields.State, QuerySortDirection.Desc).Take(10).ExecuteAsync();
            Assert.True(orderedResult is BaseSuccess<BaseTextResult<TextSemanticDocument>>, orderedResult is BaseFailure<BaseTextResult<TextSemanticDocument>> failure ? failure.Error.Code : orderedResult.Status.ToString());
            BaseTextResult<TextSemanticDocument> ordered = orderedResult.RequireValue();
            Assert.Equal(["sql-order-z", "sql-order-a"], ordered.Matches.Select(static match => match.Record.Id.Value));
            IBaseTextAdministration administration = provider.GetRequiredService<IBaseTextAdministration>(); BaseTextIndexStatus before = (await administration.GetAsync(TextSemanticDocument.Collection.Id, TextSemanticDocument.TextIndexes.Content.Definition.Id)).Value!; BaseTextRebuildRequest rebuild = Rebuild(before.Generation, "sqlite"); BaseTextRebuildResult rebuilt = (await administration.RebuildAsync(rebuild)).Value!; Assert.Equal(2, rebuilt.PublishedGeneration); Assert.Equal(4, rebuilt.RecordCount);
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
    public BaseTextProviderDescriptor Descriptor { get; } = CreateDescriptor();
    private static BaseTextProviderDescriptor CreateDescriptor() { BaseTextProviderCapability capability = BaseTextPlatform.ProviderCapability(BaseTextProviderClass.CoLocatedTransactional); ImmutableArray<byte> report = ImmutableArray.Create(new byte[32]); return new() { Id = "tests.text", Version = 1, ProviderClass = BaseTextProviderClass.CoLocatedTransactional, Capability = capability, NativeDependencyReceipts = [], CertificationContractChecksum = BaseTextCertificationReceiptContract.ContractChecksum, CertificationReportChecksum = report, CertificationReceipt = BaseTextCertificationReceiptContract.Create("tests.text", 1, BaseTextProviderClass.CoLocatedTransactional, capability, [], report) }; }
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
            BaseTextLoweringReceipt receipt = BaseTextProviderEvidence.CreateLoweringReceipt(owner.Descriptor, Snapshot, value.Index, value.QueryDigest, value.ConstraintDigest, value.InfluenceConstraints, value.Order, value.Limits);
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
                Completeness = BaseTextProviderEvidence.CreateCompleteness(owner.Descriptor, Snapshot, plan.Lowering, [.. candidates], value.TakePlusOne, value.AfterBoundary),
                Accounting = new() { InputBytes = queryBytes + constraintBytes, QueryBytes = queryBytes, ConstraintBytes = constraintBytes, StatementParameters = plan.StatementParameters, AuthorizedRecordsExamined = candidates.Length, PostingsExamined = candidates.Length, PrefixExpansionCount = 0, PrefixExpansionBytes = 0, ScoreProofBytes = candidates.Length * 32, CandidateCount = candidates.Length, OrderingBytes = orderingBytes, RetainedTransientBytes = queryBytes + constraintBytes + candidates.Length * 32 + orderingBytes, Elapsed = TimeSpan.Zero }
            }));
        }
        public ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default) { owner.HydrationCount++; return ValueTask.FromResult(OperationResults.Ok(Array.Empty<RecordEnvelope>())); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static BaseTextCandidate Candidate(string id, ulong score) { var value = new BaseTextScore { Units = score }; ImmutableArray<BaseTextOrderingValue> order = []; return new() { RecordId = new(id), Revision = new("test:1"), IndexedPosition = new(1), Score = value, SecondaryOrdering = order, CanonicalOrderingBoundary = BaseTextOrderingContract.Boundary(value, order, new(id)), ScoreProof = new() { Fields = [], Features = [], ProofDigest = ImmutableArray.Create(new byte[32]) } }; }
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
