using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests;

public sealed class CompoundCountReadTests
{
    [Fact]
    public async Task InMemoryReturnsOrderedZeroAndNonzeroIndependentCounts()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("compound.read")
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
            .AddRead(CompoundSummaryRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        OperationResult<BaseApplicationReadiness> initialized = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        initialized.IsSuccess().Should().BeTrue(initialized.Error?.Message);
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System,
            SubjectId = "compound-count-test",
        });
        (await session.Collection(CompoundAlphaRecord.Collection).CreateAsync(
            RecordId.Create("alpha-1"), new CompoundAlphaRecord { Enabled = true })).Should().BeOfType<BaseSuccess<BaseRecord<CompoundAlphaRecord>>>();

        CompoundSummaryRead.Row[] rows = (await session.Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true })).RequireValue();
        rows.Should().Equal(
            new CompoundSummaryRead.Row { Kind = "alpha", Count = 1 },
            new CompoundSummaryRead.Row { Kind = "beta", Count = 0 });
        (await session.Reads.FirstAsync(CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true }))
            .Should().BeOfType<BaseFailure<CompoundSummaryRead.Row?>>()
            .Which.Error.Code.Should().Be("base.relational.read.terminalUnsupported");
        (await session.Reads.AnyAsync(CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true }))
            .Should().BeOfType<BaseFailure<bool>>()
            .Which.Error.Code.Should().Be("base.relational.read.terminalUnsupported");
    }

    [Fact]
    public async Task SqliteReturnsAllBranchesFromOneReadTransaction()
    {
        string dataSource = Path.Combine(Path.GetTempPath(), $"hpd-base-compound-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
                .AddTestStaticGrant("compound.read")
                .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
                .AddRead(CompoundSummaryRead.Definition)
                .ConfigureSchema(options => { options.ApplicationId = "compound-count-tests"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x42, 32).ToArray(); })
                .UseStore(SqliteStore.Configure(options => { options.DataSource = dataSource; options.StoreId = "compound-count-tests"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "compound-count-tests" });
            planned.IsSuccess().Should().BeTrue(planned.Error?.Message);
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "compound-sqlite-test" });
            (await session.Collection(CompoundBetaRecord.Collection).CreateAsync(
                RecordId.Create("beta-1"), new CompoundBetaRecord { Enabled = true })).Should().BeOfType<BaseSuccess<BaseRecord<CompoundBetaRecord>>>();
            CompoundSummaryRead.Row[] rows = (await session.Reads.ToArrayAsync(
                CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true })).RequireValue();
            rows.Should().Equal(new CompoundSummaryRead.Row { Kind = "alpha", Count = 0 }, new CompoundSummaryRead.Row { Kind = "beta", Count = 1 });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(dataSource)!).Where(file => Path.GetFileName(file).StartsWith(Path.GetFileName(dataSource), StringComparison.Ordinal))) File.Delete(file);
        }
    }

    [Fact]
    public async Task RuntimeRejectsReorderedCompoundProviderRows()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("compound.read")
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
            .AddRead(CompoundSummaryRead.Definition)
            .UseStore(TestStoreProvider.Create(new ReorderedCompoundStore(), relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "compound-hostile-test" });
        BaseResult<CompoundSummaryRead.Row[]> result = await session.Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });
        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Theory]
    [InlineData(CompoundHostility.MissingEvidence)]
    [InlineData(CompoundHostility.WrongChecksum)]
    [InlineData(CompoundHostility.WrongGeneration)]
    [InlineData(CompoundHostility.NegativeCount)]
    [InlineData(CompoundHostility.AdditionalRow)]
    public async Task RuntimeRejectsHostileCompoundEvidenceAndRows(CompoundHostility hostility)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("compound.read")
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
            .AddRead(CompoundSummaryRead.Definition)
            .UseStore(TestStoreProvider.Create(new ReorderedCompoundStore(hostility), relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "compound-hostile-evidence" });

        BaseResult<CompoundSummaryRead.Row[]> result = await session.Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });

        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.resultInvalid");
    }

    [Fact]
    public async Task InMemoryBranchesObserveOneCapturedSnapshotDuringConcurrentWrites()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("compound.read").AddCollection(CompoundAlphaRecord.Collection)
            .AddRead(CompoundSameSourceRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "compound-concurrency-test" });
        Task writer = Task.Run(async () =>
        {
            for (int index = 0; index < 64; index++)
                (await session.Collection(CompoundAlphaRecord.Collection).CreateAsync(
                    RecordId.Create($"concurrent-{index:D3}"), new CompoundAlphaRecord { Enabled = true })).Should().BeOfType<BaseSuccess<BaseRecord<CompoundAlphaRecord>>>();
        });
        do
        {
            CompoundSameSourceRead.Row[] rows = (await session.Reads.ToArrayAsync(
                CompoundSameSourceRead.Handle, new CompoundSameSourceRead { Enabled = true })).RequireValue();
            rows[0].Count.Should().Be(rows[1].Count);
        } while (!writer.IsCompleted);
        await writer;
    }

    [Fact]
    public async Task SqliteBranchesObserveOneReadTransactionDuringConcurrentWrites()
    {
        string dataSource = Path.Combine(Path.GetTempPath(), $"hpd-base-compound-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
                .AddTestStaticGrant("compound.read").AddCollection(CompoundAlphaRecord.Collection)
                .AddRead(CompoundSameSourceRead.Definition)
                .ConfigureSchema(options => { options.ApplicationId = "compound-concurrency-tests"; options.PlanProtectionKey = Enumerable.Repeat((byte)0x43, 32).ToArray(); })
                .UseStore(SqliteStore.Configure(options => { options.DataSource = dataSource; options.StoreId = "compound-concurrency-tests"; })));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "compound-concurrency-tests" });
            planned.IsSuccess().Should().BeTrue(planned.Error?.Message);
            BaseSchemaPlan plan = planned.Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "compound-sqlite-concurrency" });
            Task writer = Task.Run(async () =>
            {
                for (int index = 0; index < 32; index++)
                    (await session.Collection(CompoundAlphaRecord.Collection).CreateAsync(
                        RecordId.Create($"sqlite-concurrent-{index:D3}"), new CompoundAlphaRecord { Enabled = true })).Should().BeOfType<BaseSuccess<BaseRecord<CompoundAlphaRecord>>>();
            });
            do
            {
                CompoundSameSourceRead.Row[] rows = (await session.Reads.ToArrayAsync(
                    CompoundSameSourceRead.Handle, new CompoundSameSourceRead { Enabled = true })).RequireValue();
                rows[0].Count.Should().Be(rows[1].Count);
            } while (!writer.IsCompleted);
            await writer;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in Directory.GetFiles(Path.GetDirectoryName(dataSource)!).Where(file => Path.GetFileName(file).StartsWith(Path.GetFileName(dataSource), StringComparison.Ordinal))) File.Delete(file);
        }
    }

    [Fact]
    public async Task PolicyDenialOnOneBranchPreventsAllProviderInfluence()
    {
        var store = new CountingCompoundStore();
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<DenyBetaPolicyEvaluator>()
            .AddTestStaticGrant("compound.read").AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
            .AddRead(CompoundSummaryRead.Definition).UseStore(TestStoreProvider.Create(store, relational: true)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = "compound-policy-test" });
        BaseResult<CompoundSummaryRead.Row[]> result = await session.Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });
        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>();
        store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CallerCancellationBeforeInfluenceNeverCallsTheProvider()
    {
        var store = new CountingCompoundStore();
        var services = CompoundServices<AllowPolicyEvaluator>(store);
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseSession session = SystemSession(provider, "compound-cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> execute = async () => await session.Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true }, cancellation.Token);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        store.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PolicyHiddenPredicateFieldPreventsAllProviderInfluence()
    {
        var store = new CountingCompoundStore();
        var services = CompoundServices<HideEnabledPolicyEvaluator>(store);
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        BaseResult<CompoundSummaryRead.Row[]> result = await SystemSession(provider, "compound-hidden-field").Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });

        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>();
        store.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(CompoundCapabilityHostility.IndependentBranchesDisabled)]
    [InlineData(CompoundCapabilityHostility.SingleSnapshotDisabled)]
    [InlineData(CompoundCapabilityHostility.BranchLimitTooSmall)]
    [InlineData(CompoundCapabilityHostility.OperationLimitTooSmall)]
    public async Task EveryCompoundCapabilityAuthorityMemberFailsClosedBeforeInfluence(CompoundCapabilityHostility hostility)
    {
        var store = new LimitedCompoundStore(hostility);
        var services = CompoundServices<AllowPolicyEvaluator>(store);
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        BaseResult<CompoundSummaryRead.Row[]> result = await SystemSession(provider, "compound-capability-limit").Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });

        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.unsupported");
        store.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(CompoundCapabilityHostility.IndependentBranchesDisabled)]
    [InlineData(CompoundCapabilityHostility.SingleSnapshotDisabled)]
    [InlineData(CompoundCapabilityHostility.BranchLimitTooSmall)]
    [InlineData(CompoundCapabilityHostility.OperationLimitTooSmall)]
    public void EveryCompoundCapabilityMemberChangesTheCanonicalChecksum(CompoundCapabilityHostility hostility)
    {
        RelationalReadCapability original = ReorderedCompoundStore.Capability();
        RelationalReadCapability changed = original with
        {
            IndependentAggregateBranches = hostility != CompoundCapabilityHostility.IndependentBranchesDisabled,
            SingleSnapshotCompoundReads = hostility != CompoundCapabilityHostility.SingleSnapshotDisabled,
            MaxCompoundBranches = hostility == CompoundCapabilityHostility.BranchLimitTooSmall ? 31 : 32,
            MaxCompoundOperations = hostility == CompoundCapabilityHostility.OperationLimitTooSmall ? 255 : 256,
        };

        BaseRelationalReadCapabilityContract.Checksum(changed)
            .Should().NotEqual(BaseRelationalReadCapabilityContract.Checksum(original));
    }

    [Fact]
    public async Task HostileCapabilityMutationAfterInstallationFailsClosedBeforeExecution()
    {
        var store = new MutableCapabilityCompoundStore();
        HPDBaseStoreProvider installed = TestStoreProvider.Create(store, relational: true);
        store.CurrentCapability = store.CurrentCapability with { JoinKinds = null! };
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("compound.read")
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
            .AddRead(CompoundSummaryRead.Definition).UseStore(installed));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        BaseResult<CompoundSummaryRead.Row[]> result = await SystemSession(provider, "compound-hostile-capability").Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });

        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.unsupported");
        store.Calls.Should().Be(0);
    }

    [Fact]
    public void InstalledProviderReturnsDefensiveRelationalCapabilityCopies()
    {
        var store = new MutableCapabilityCompoundStore();
        HPDBaseStoreProvider installed = TestStoreProvider.Create(store, relational: true);

        RelationalReadCapability first = installed.RelationalReads;
        first.AggregateKinds[0] = (BaseAggregateKind)999;

        installed.RelationalReads.AggregateKinds.Should().Equal(BaseAggregateKind.Count);
    }

    [Fact]
    public async Task SchemaGenerationChangeAfterProviderExecutionFailsClosed()
    {
        var store = new ReorderedCompoundStore(CompoundHostility.ResultGenerationRace);
        var services = CompoundServices<AllowPolicyEvaluator>(store);
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        BaseResult<CompoundSummaryRead.Row[]> result = await SystemSession(provider, "compound-generation-race").Reads.ToArrayAsync(
            CompoundSummaryRead.Handle, new CompoundSummaryRead { Enabled = true });

        result.Should().BeOfType<BaseFailure<CompoundSummaryRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.schemaNotReady");
    }

    [Fact]
    public void PublicDiscriminatorCannotCollideWithLogicalCollectionIdentity()
    {
        Action resolve = () => new ServiceCollection().AddHPDBase(builder => builder
            .AddCollection(CompoundAlphaRecord.Collection)
            .AddRead(CompoundCollisionRead.Definition));

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicDiscriminatorCannotCollideWithBranchOrSourceIdentity(bool sourceIdentity)
    {
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> generated = CompoundSummaryRead.Definition;
        BaseRelationalCompoundCountBranch target = generated.Plan.CompoundCountBranches[0];
        BaseRelationalCompoundCountBranch[] branches = generated.Plan.CompoundCountBranches
            .Select((branch, index) => index == 0 ? branch with
            { Discriminator = sourceIdentity ? target.Source.Id : target.Id } : branch with { })
            .OrderBy(static branch => branch.Discriminator, StringComparer.Ordinal).ToArray();
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> invalid = Clone(generated, generated.Plan with
        { CompoundCountBranches = branches, Sources = branches.Select(static branch => branch.Source).ToArray() });
        Action install = () => new ServiceCollection().AddHPDBase(builder => builder
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection).AddRead(invalid));

        install.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GeneratedAndManualCompoundDefinitionsProduceIdenticalGraphAuthority()
    {
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> generated = CompoundSummaryRead.Definition;
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> manual = Clone(generated, generated.Plan with
        {
            Sources = generated.Plan.Sources.Select(static source => source with { }).ToArray(),
            CompoundCountBranches = generated.Plan.CompoundCountBranches.Select(static branch => branch with
            { Source = branch.Source with { } }).ToArray(),
        });

        BaseLogicalSchema generatedSchema = BuildSchema(generated);
        BaseLogicalSchema manualSchema = BuildSchema(manual);

        generatedSchema.CanonicalChecksum.Should().Be(manualSchema.CanonicalChecksum);
        generated.Plan.CompoundChecksum.Should().Be(manual.Plan.CompoundChecksum);
        generated.Plan.CompoundCountBranches.Select(static branch => branch.BranchChecksum)
            .Should().Equal(manual.Plan.CompoundCountBranches.Select(static branch => branch.BranchChecksum));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void BranchChecksumBindsEveryNormativeInput(int member)
    {
        BaseRelationalCompoundCountBranch original = CompoundSummaryRead.Definition.Plan.CompoundCountBranches[0];
        byte[] collectionChecksum = Enumerable.Repeat((byte)0x31, 32).ToArray();
        BaseRelationalCompoundCountBranch changed = member switch
        {
            0 => original with { Id = original.Id + "-changed" },
            1 => original with { Source = original.Source with { Id = original.Source.Id + "-changed" } },
            2 => original with { Source = original.Source with { CollectionId = CompoundBetaRecord.Collection.Id } },
            3 => original with { Predicate = null },
            4 => original with { Discriminator = original.Discriminator + "-changed" },
            5 => original with { DiscriminatorOutputFieldId = original.DiscriminatorOutputFieldId + "-changed" },
            6 => original with { CountOutputFieldId = original.CountOutputFieldId + "-changed" },
            _ => original,
        };
        byte[] changedCollectionChecksum = member == 7 ? Enumerable.Repeat((byte)0x32, 32).ToArray() : collectionChecksum;

        BaseCompoundReadAuthority.BranchChecksum(changed, changedCollectionChecksum).ToArray()
            .Should().NotEqual(BaseCompoundReadAuthority.BranchChecksum(original, collectionChecksum).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void CompoundChecksumBindsBranchesSerializersAndEveryBudget(int member)
    {
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> source = CompoundSummaryRead.Definition;
        BaseRelationalReadPlan originalPlan = source.Plan;
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> original = Clone(source, originalPlan);
        original.ParameterSerializerContractChecksum = "parameter-a";
        original.RowSerializerContractChecksum = "row-a";
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> changed = Clone(source, originalPlan);
        changed.ParameterSerializerContractChecksum = member == 1 ? "parameter-b" : "parameter-a";
        changed.RowSerializerContractChecksum = member == 2 ? "row-b" : "row-a";
        BaseRelationalCompoundCountBranch[] branches = originalPlan.CompoundCountBranches.Select(static branch => branch with { }).ToArray();
        if (member == 0) branches[0] = branches[0] with
        { BranchChecksum = BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0x77, 32).ToArray()) };
        BaseRelationalReadBudgets budgets = originalPlan.Budgets;
        BaseRelationalReadBudgets changedBudgets = member switch
        {
            3 => budgets with { MaxResultRows = budgets.MaxResultRows + 1 },
            4 => budgets with { MaxResultBytes = budgets.MaxResultBytes + 1 },
            5 => budgets with { MaxOperations = budgets.MaxOperations + 1 },
            6 => budgets with { MaxExecutionMilliseconds = budgets.MaxExecutionMilliseconds + 1 },
            7 => budgets with { MaxCompoundBranches = budgets.MaxCompoundBranches + 1 },
            8 => budgets with { MaxCompoundOperations = budgets.MaxCompoundOperations + 1 },
            _ => budgets,
        };

        BaseRelationalReadPlan changedPlan = originalPlan with
        {
            Budgets = changedBudgets,
            DependencyMode = member == 9 ? (BaseReadDependencyMode)1 : originalPlan.DependencyMode,
        };
        BaseCompoundReadAuthority.CompoundChecksum(branches, changed, changedPlan).ToArray()
            .Should().NotEqual(BaseCompoundReadAuthority.CompoundChecksum(originalPlan.CompoundCountBranches, original, originalPlan).ToArray());
    }

    [Fact]
    public async Task AggregateEvidenceAndRowsExhaustOneCheckedBudgetWithoutPartialOutput()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<AllowPolicyEvaluator>()
            .AddTestStaticGrant("compound.read").AddCollection(CompoundAlphaRecord.Collection)
            .AddRead(CompoundTightBudgetRead.Definition));
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

        BaseResult<CompoundTightBudgetRead.Row[]> result = await SystemSession(provider, "compound-accounting").Reads.ToArrayAsync(
            CompoundTightBudgetRead.Handle, new CompoundTightBudgetRead { Enabled = true });

        result.Should().BeOfType<BaseFailure<CompoundTightBudgetRead.Row[]>>()
            .Which.Error.Code.Should().Be("base.relational.read.limitExceeded");
        InMemoryRecordStore.TryAccumulateRelationalResultBytes(long.MaxValue, 1, out long inMemoryTotal).Should().BeFalse();
        inMemoryTotal.Should().Be(0);
        SqliteRecordStore.TryAccumulateRelationalResultBytes(long.MaxValue, 1, out long sqliteTotal).Should().BeFalse();
        sqliteTotal.Should().Be(0);
    }

    private static BaseLogicalSchema BuildSchema(BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> read)
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder.ConfigureSchema(options => options.ApplicationId = "compound-parity")
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection).AddRead(read));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<BaseLogicalSchema>();
    }

    private static BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> Clone(
        BaseReadDefinition<CompoundSummaryRead, CompoundSummaryRead.Row> source, BaseRelationalReadPlan plan) => new(
            plan, null, null, source.ParameterCodec, source.RowCodec, source.ClientContract)
        {
            Exposure = source.Exposure, Authorization = source.Authorization, Disclosure = source.Disclosure,
            SourceAuthority = source.SourceAuthority, Audience = source.Audience, RequiredGrantId = source.RequiredGrantId,
            ConfidentialOutputFieldIds = source.ConfidentialOutputFieldIds, SecretOutputFieldIds = source.SecretOutputFieldIds,
            SystemSourceIds = source.SystemSourceIds, SerializerRegistration = source.SerializerRegistration,
            ParameterDeclarations = source.ParameterDeclarations, RowDeclarations = source.RowDeclarations,
        };

    private static IServiceCollection CompoundServices<TPolicy>(ReorderedCompoundStore store) where TPolicy : class, IPolicyEvaluator, new()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder.AddTestPolicyAuthority<TPolicy>()
            .AddTestStaticGrant("compound.read")
            .AddCollection(CompoundAlphaRecord.Collection).AddCollection(CompoundBetaRecord.Collection)
            .AddRead(CompoundSummaryRead.Definition)
            .UseStore(TestStoreProvider.Create(store, relational: true)));
        return services;
    }

    private static BaseSession SystemSession(ServiceProvider provider, string subjectId) =>
        provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        { AuthenticationState = PrincipalAuthenticationState.System, SubjectKind = AccessSubjectKind.System, SubjectId = subjectId });

    private sealed class DenyBetaPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision
            {
                Effect = string.Equals(request.Collection?.Id, CompoundBetaRecord.Collection.Id, StringComparison.Ordinal) ? PolicyEffect.Deny : PolicyEffect.Allow,
                Outcome = string.Equals(request.Collection?.Id, CompoundBetaRecord.Collection.Id, StringComparison.Ordinal) ? PolicyOutcome.Denied : PolicyOutcome.Allowed,
            });
    }

    private sealed class HideEnabledPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed }
                .WithReadMask(new FieldMask { Mode = FieldMaskMode.Exclude, Exclude = ["compound.alpha.enabled", "compound.beta.enabled"] }));
    }

    private sealed class CountingCompoundStore() : ReorderedCompoundStore
    {
        public int Calls { get; private set; }
        public override ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
            BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default)
        { Calls++; return base.ExecuteReadAsync(request, cancellationToken); }
    }

    public enum CompoundCapabilityHostility { IndependentBranchesDisabled, SingleSnapshotDisabled, BranchLimitTooSmall, OperationLimitTooSmall }

    private sealed class LimitedCompoundStore(CompoundCapabilityHostility hostility) : ReorderedCompoundStore, IRelationalReadStore
    {
        public new RelationalReadCapability RelationalReads { get; } = Capability() with
        {
            IndependentAggregateBranches = hostility != CompoundCapabilityHostility.IndependentBranchesDisabled,
            SingleSnapshotCompoundReads = hostility != CompoundCapabilityHostility.SingleSnapshotDisabled,
            MaxCompoundBranches = hostility == CompoundCapabilityHostility.BranchLimitTooSmall ? 1 : 32,
            MaxCompoundOperations = hostility == CompoundCapabilityHostility.OperationLimitTooSmall ? 1 : 256,
        };
        RelationalReadCapability IRelationalReadStore.RelationalReads => RelationalReads;
        public int Calls { get; private set; }
        public override ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
            BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default)
        { Calls++; return base.ExecuteReadAsync(request, cancellationToken); }
    }

    private sealed class MutableCapabilityCompoundStore : ReorderedCompoundStore, IRelationalReadStore
    {
        public RelationalReadCapability CurrentCapability { get; set; } = ReorderedCompoundStore.Capability();
        RelationalReadCapability IRelationalReadStore.RelationalReads => CurrentCapability;
        public int Calls { get; private set; }
        public override ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
            BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default)
        { Calls++; return base.ExecuteReadAsync(request, cancellationToken); }
    }

    public enum CompoundHostility { Reordered, MissingEvidence, WrongChecksum, WrongGeneration, ResultGenerationRace, NegativeCount, AdditionalRow }

    private class ReorderedCompoundStore(CompoundHostility hostility = CompoundHostility.Reordered) : FakeRecordStore("compound-hostile"), IRelationalReadStore
    {
        public RelationalReadCapability RelationalReads { get; } = Capability();

        internal static RelationalReadCapability Capability(int maxCompoundBranches = 32) => new()
        {
            Supported = true, JoinKinds = [], AggregateKinds = [BaseAggregateKind.Count],
            ComparisonOperators = [FilterOperator.Equal], ValueKinds = [QueryValueKind.Boolean, QueryValueKind.String, QueryValueKind.Integer],
            IndependentAggregateBranches = true, SingleSnapshotCompoundReads = true, MaxCompoundBranches = maxCompoundBranches, MaxCompoundOperations = 256,
            MaxSources = 32, MaxPredicateNodes = 256, MaxAggregates = 32, MaxProjectionFields = 64,
            MaxResultRows = 1_000, MaxResultBytes = 1_048_576, SnapshotConsistency = true, CompleteDependencyEvidence = true,
        };

        public virtual ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(
            BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default)
        {
            BaseRelationalCompoundCountBranch[] ordered = request.Plan.CompoundCountBranches;
            BaseRelationalCompoundCountBranch[] resultBranches = hostility == CompoundHostility.Reordered ? ordered.Reverse().ToArray() : ordered;
            BaseRelationalRow[] rows = resultBranches.Select((branch, index) => new BaseRelationalRow
            {
                Fields =
                [
                    new() { FieldId = branch.DiscriminatorOutputFieldId, Value = new QueryValue { Kind = QueryValueKind.String, String = branch.Discriminator } },
                    new() { FieldId = branch.CountOutputFieldId, Value = new QueryValue { Kind = QueryValueKind.Integer, Integer = hostility == CompoundHostility.NegativeCount && index == 0 ? -1 : 0 } },
                ],
            }).ToArray();
            if (hostility == CompoundHostility.AdditionalRow) rows = [.. rows, rows[^1] with { }];
            BaseRelationalCompoundBranchEvidence[] evidence = ordered.Select((branch, ordinal) => new BaseRelationalCompoundBranchEvidence
            {
                BranchId = branch.Id,
                BranchChecksum = hostility == CompoundHostility.WrongChecksum && ordinal == 0 ? BaseSchemaAuthorityChecksum.Create(Enumerable.Repeat((byte)0xA5, 32).ToArray()) : branch.BranchChecksum,
                RowOrdinal = ordinal,
                SchemaGeneration = hostility == CompoundHostility.WrongGeneration && ordinal == 0 ? request.Plan.SchemaGeneration + 1 : request.Plan.SchemaGeneration,
            }).ToArray();
            if (hostility == CompoundHostility.MissingEvidence) evidence = evidence[..^1];
            return ValueTask.FromResult(OperationResults.Ok(new BaseRelationalReadExecutionResult
            {
                Result = new BaseRelationalReadResult
                {
                    Rows = rows,
                    Page = new PageInfo { Limit = rows.Length, HasMore = false }, Count = rows.Length,
                },
                DependencyEvidence = request.Plan.Sources.Select(static source => source.CollectionId).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).Select(static id => new BaseReadDependencyEvidence { CollectionId = id }).ToArray(),
                CompoundBranches = evidence,
                SnapshotAuthority = TestRelationalReadAuthority.Create(request,
                    hostility == CompoundHostility.ResultGenerationRace
                        ? request.Plan.SchemaGeneration + 1 : request.Plan.SchemaGeneration),
            }));
        }
    }
}

[BaseCollection("compound-alpha-records", typeof(CompoundCountJsonContext))]
internal sealed partial record CompoundAlphaRecord
{
    [BaseField("compound.alpha.enabled")] public required bool Enabled { get; init; }
}

[BaseCollection("compound-beta-records", typeof(CompoundCountJsonContext))]
internal sealed partial record CompoundBetaRecord
{
    [BaseField("compound.beta.enabled")] public required bool Enabled { get; init; }
}

[BaseRead("compound-summary", typeof(CompoundCountJsonContext), RequiredGrantId = "compound.read")]
internal sealed partial record CompoundSummaryRead
{
    [BaseReadParameter("compound.summary.enabled")] public required bool Enabled { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("compound.summary.kind")] public required string Kind { get; init; }
        [BaseReadField("compound.summary.count")] public required long Count { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<CompoundSummaryRead, Row> read) => read
        .CountBranch("alpha-branch", Row.Fields.Kind, "alpha", CompoundAlphaRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(CompoundAlphaRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CountBranch("beta-branch", Row.Fields.Kind, "beta", CompoundBetaRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(CompoundBetaRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CompoundLimits(4_096, 32, 2_000, 2, 16);
}

[BaseRead("compound-same-source", typeof(CompoundCountJsonContext), RequiredGrantId = "compound.read")]
internal sealed partial record CompoundSameSourceRead
{
    [BaseReadParameter("compound.same.enabled")] public required bool Enabled { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("compound.same.kind")] public required string Kind { get; init; }
        [BaseReadField("compound.same.count")] public required long Count { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<CompoundSameSourceRead, Row> read) => read
        .CountBranch("first-branch", Row.Fields.Kind, "first", CompoundAlphaRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(CompoundAlphaRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CountBranch("second-branch", Row.Fields.Kind, "second", CompoundAlphaRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(CompoundAlphaRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CompoundLimits(4_096, 32, 2_000, 2, 16);
}

[BaseRead("compound-collision", typeof(CompoundCountJsonContext), RequiredGrantId = "compound.read")]
internal sealed partial record CompoundCollisionRead
{
    [BaseReadParameter("compound.collision.enabled")] public required bool Enabled { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("compound.collision.kind")] public required string Kind { get; init; }
        [BaseReadField("compound.collision.count")] public required long Count { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<CompoundCollisionRead, Row> read) => read
        .CountBranch("collision-branch", Row.Fields.Kind, "compound-alpha-records", CompoundAlphaRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(CompoundAlphaRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CompoundLimits(2_048, 8, 1_000, 1, 4);
}

[BaseRead("compound-tight-budget", typeof(CompoundCountJsonContext), RequiredGrantId = "compound.read")]
internal sealed partial record CompoundTightBudgetRead
{
    [BaseReadParameter("compound.tight.enabled")] public required bool Enabled { get; init; }
    public sealed partial record Row
    {
        [BaseReadField("compound.tight.kind")] public required string Kind { get; init; }
        [BaseReadField("compound.tight.count")] public required long Count { get; init; }
    }
    public static void Configure(BaseReadDefinitionBuilder<CompoundTightBudgetRead, Row> read) => read
        .CountBranch("tight-branch", Row.Fields.Kind, "tight", CompoundAlphaRecord.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(CompoundAlphaRecord.Fields.Enabled).Equal(branch.Parameter(Parameters.Enabled))))
        .CompoundLimits(1, 4, 1_000, 1, 4);
}

[JsonSerializable(typeof(CompoundAlphaRecord))]
[JsonSerializable(typeof(CompoundBetaRecord))]
[JsonSerializable(typeof(CompoundSummaryRead))]
[JsonSerializable(typeof(CompoundSummaryRead.Row), TypeInfoPropertyName = "CompoundSummaryReadRow")]
[JsonSerializable(typeof(CompoundSameSourceRead))]
[JsonSerializable(typeof(CompoundSameSourceRead.Row), TypeInfoPropertyName = "CompoundSameSourceReadRow")]
[JsonSerializable(typeof(CompoundCollisionRead))]
[JsonSerializable(typeof(CompoundCollisionRead.Row), TypeInfoPropertyName = "CompoundCollisionReadRow")]
[JsonSerializable(typeof(CompoundTightBudgetRead))]
[JsonSerializable(typeof(CompoundTightBudgetRead.Row), TypeInfoPropertyName = "CompoundTightBudgetReadRow")]
internal sealed partial class CompoundCountJsonContext : JsonSerializerContext;
