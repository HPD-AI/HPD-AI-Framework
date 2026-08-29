using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Application.Activations;

public sealed class ActivationMigrationTests
{
    [Theory]
    [InlineData(CompactionHostility.None)]
    [InlineData(CompactionHostility.Authority)]
    [InlineData(CompactionHostility.CommittedResult)]
    [InlineData(CompactionHostility.UnknownFailure)]
    public async Task Administration_receipt_compaction_owns_and_validates_provider_authority_capture(
        CompactionHostility hostility)
    {
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            SemanticActivationApplicationId = "migration-administration-test",
        });
        var providerStore = new HostileMigrationProvider(store)
        {
            CompactionHostility = hostility,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options =>
            {
                options.ApplicationId = "migration-administration-test";
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x52, 32).ToArray();
            });
            builder.UseStore(TestStoreProvider.CreateActivationProvider(providerStore, providerStore));
            builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
            {
                Id = "example.compaction.policy", Version = 1, OwningModuleId = "example",
                EvaluatorContractId = "example.compaction.policy.evaluator", EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            }, new AllowPolicy());
            AddGrant(builder, SourceRegistration.Definition.Grants.Remove, SourceRegistration.Definition.Id);
            builder.AddActivation(SourceRegistration);
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync())
            .IsSuccess().Should().BeTrue();

        BaseResult<BaseActivationReceiptCompactionResult> result = await provider
            .GetRequiredService<IHPDBaseAdministration>()
            .CompactActivationReceiptsAsync(new BaseActivationAdministrationReceiptCompactionRequest
            {
                StoreId = providerStore.Capabilities.StoreId,
                Principal = Principal(),
                Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
                DefinitionId = SourceRegistration.Definition.Id,
                DefinitionVersion = SourceRegistration.Definition.Version,
                Take = 1,
                Identity = Identity("compact-empty"),
            });

        if (hostility != CompactionHostility.None)
        {
            result.Should().BeOfType<BaseFailure<BaseActivationReceiptCompactionResult>>();
            ((BaseFailure<BaseActivationReceiptCompactionResult>)result).Error.Code
                .Should().Be("base.activation.providerContractInvalid");
            provider.GetRequiredService<BaseActivationProviderExecutionGate>().IsQuarantined.Should().BeTrue();
            return;
        }
        result.Should().BeOfType<BaseSuccess<BaseActivationReceiptCompactionResult>>();
        BaseActivationReceiptCompactionResult page = ((BaseSuccess<BaseActivationReceiptCompactionResult>)result).Value;
        page.Completed.Should().BeTrue();
        page.ExaminedCount.Should().Be(0);
        page.DeletedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(MigrationHostility.CandidateCanonicalBytes)]
    [InlineData(MigrationHostility.CandidateChecksum)]
    [InlineData(MigrationHostility.CandidateControlChecksum)]
    [InlineData(MigrationHostility.CandidateIdentity)]
    [InlineData(MigrationHostility.CommitIdentity)]
    [InlineData(MigrationHostility.CommitSourceControlChecksum)]
    [InlineData(MigrationHostility.CommitReplacementControlChecksum)]
    [InlineData(MigrationHostility.CommitAccounting)]
    public async Task Administration_path_rejects_hostile_migration_authority_and_quarantines_provider(
        MigrationHostility hostility)
    {
        var inner = new InMemoryRecordStore();
        var hostile = new HostileMigrationProvider(inner);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options =>
            {
                options.ApplicationId = "migration-administration-test";
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x51, 32).ToArray();
            });
            builder.UseStore(TestStoreProvider.CreateActivationProvider(hostile, hostile));
            builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
            {
                Id = "example.migration.policy", Version = 1, OwningModuleId = "example",
                EvaluatorContractId = "example.migration.policy.evaluator", EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            }, new AllowPolicy());
            AddGrant(builder, SourceRegistration.Definition.Grants.Enqueue, SourceRegistration.Definition.Id);
            AddGrant(builder, SourceRegistration.Definition.Grants.Migrate, SourceRegistration.Definition.Id);
            builder.AddActivation(SourceRegistration);
            builder.AddActivation(TargetRegistration);
            builder.AddActivationMigration(CompleteBuilder().Create(Draft()));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        readiness.IsSuccess().Should().BeTrue(readiness.Error?.Code);
        PrincipalContext principal = Principal();
        BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal,
            options => options.Audience = HPDBaseEndpointAudience.ControlPlane);
        OperationResult<BaseActivationEnqueueResult> enqueued = await session.Activations.Get(SourceRegistration.Identity).EnqueueAsync(
            new SourceInput { Name = "Ada", Note = null }, Identity("enqueue-source"));
        enqueued.IsSuccess().Should().BeTrue(enqueued.Error?.Code);
        BaseActivationEnqueueResult source = enqueued.Value!;
        hostile.Hostility = hostility;
        var request = new BaseActivationAdministrationMigrationRequest
        {
            StoreId = inner.Capabilities.StoreId,
            Principal = principal,
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global },
            MigrationId = Draft().Id,
            MigrationVersion = Draft().Version,
            ActivationId = source.ActivationId,
            ExpectedGeneration = 1,
            Identity = Identity("migrate-source"),
        };
        IHPDBaseAdministration administration = provider.GetRequiredService<IHPDBaseAdministration>();

        BaseResult<BaseActivationMigrationResult> rejected = await administration.MigrateActivationAsync(request);

        rejected.Should().BeOfType<BaseFailure<BaseActivationMigrationResult>>();
        ((BaseFailure<BaseActivationMigrationResult>)rejected).Error.Code.Should().Be("base.activation.providerContractInvalid");
        provider.GetRequiredService<BaseActivationProviderExecutionGate>().IsQuarantined.Should().BeTrue();
        int candidateCalls = hostile.CandidateCalls;
        int migrationCalls = hostile.MigrationCalls;

        BaseResult<BaseActivationMigrationResult> quarantined = await administration.MigrateActivationAsync(request);

        quarantined.Should().BeOfType<BaseFailure<BaseActivationMigrationResult>>();
        hostile.CandidateCalls.Should().Be(candidateCalls);
        hostile.MigrationCalls.Should().Be(migrationCalls);
    }

    [Fact]
    public void Generated_projection_renames_properties_adds_constants_and_is_deterministic()
    {
        BaseActivationMigrationRegistration<SourceInput, TargetInput> registration = CompleteBuilder().Create(Draft());
        var installed = new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration);
        ImmutableArray<byte> projected = installed.Project(
            "{\"name\":\"Ada\",\"note\":null}"u8);
        JsonSerializer.Deserialize(projected.AsSpan(), MigrationJsonContext.Default.TargetInput)
            .Should().Be(new TargetInput { DisplayName = "Ada", Note = null, Enabled = true });
        installed.Definition.Checksum.Should().HaveCount(32);
        new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration).Definition.Checksum
            .Should().Equal(installed.Definition.Checksum);
    }

    [Fact]
    public void L72_authority_checksums_match_the_locked_literal_vectors()
    {
        BaseActivationMigrationRegistration<SourceInput, TargetInput> registration = CompleteBuilder().Create(Draft());
        var installed = new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration);
        Convert.ToHexString(SourceActivationDtos.HPDBaseActivationDtoAuthority.InputDtoAuthorityChecksum.Span)
            .Should().Be("CC1B0076E60D93104DB53C089F6A63A3F6EEBC4A660CDDA49ABB87FD7C0B8234");
        Convert.ToHexString(SourceActivationDtos.HPDBaseActivationDtoAuthority.ResultDtoAuthorityChecksum.Span)
            .Should().Be("5909DE27AFB19F166CFEC327D5D8BAE3791F20C7737E9D1990FD1BF5CDA71665");
        Convert.ToHexString(SourceActivationDtos.HPDBaseActivationDtoAuthority.DtoAuthorityChecksum.Span)
            .Should().Be("D6CA09F98906038058BC33D872F8722832F7AF9638A54F09FAC558BDB6637966");
        Convert.ToHexString(SourceRegistration.Definition.Checksum.AsSpan())
            .Should().Be("65C77C92B0C5559057BE58B14F890D02C47CFF2BCA393C03541B806631DCC7DE");
        Convert.ToHexString(SourceRegistration.Definition.Handler!.Checksum.AsSpan())
            .Should().Be("6EE9D08974EEC221C1D17EFA81511AAA78742CE4E490E0207AB4416509A87255");
        Convert.ToHexString(installed.Definition.Checksum.AsSpan())
            .Should().Be("466778EA129D45C1CCFD4E72F2D7BBC70374C913C11653AF06C36A8C0EA84A28");
    }

    [Theory]
    [InlineData("{\"name\":\"Ada\"}", false, null)]
    [InlineData("{\"name\":\"Ada\",\"note\":null}", true, null)]
    [InlineData("{\"name\":\"Ada\",\"note\":\"value\"}", true, "value")]
    public void Optional_source_missing_null_and_value_remain_distinct(string source, bool expectedPresent, string? expectedValue)
    {
        var installed = new BaseInstalledActivationMigration<SourceInput, TargetInput>(CompleteBuilder().Create(Draft()));
        ImmutableArray<byte> projected = installed.Project(System.Text.Encoding.UTF8.GetBytes(source));
        using JsonDocument document = JsonDocument.Parse(projected.ToArray());
        bool present = document.RootElement.TryGetProperty("note", out JsonElement note);
        present.Should().Be(expectedPresent);
        if (expectedPresent)
            (note.ValueKind == JsonValueKind.Null ? null : note.GetString()).Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("{ \"name\":\"Ada\"}")]
    [InlineData("{\"note\":null,\"name\":\"Ada\"}")]
    public void Provider_source_must_obey_the_exact_dto_canonical_byte_law(string source)
    {
        var installed = new BaseInstalledActivationMigration<SourceInput, TargetInput>(CompleteBuilder().Create(Draft()));

        Action action = () => installed.Project(System.Text.Encoding.UTF8.GetBytes(source));

        action.Should().Throw<BaseActivationDtoContractException>()
            .WithMessage("base.activation.providerContractInvalid");
    }

    [Fact]
    public void Real_projection_pipeline_admits_the_exact_target_limit_and_rejects_one_more_before_retention()
    {
        const int maximum = 1024;
        int emptyLength = JsonSerializer.SerializeToUtf8Bytes(
            new BoundedTargetInput { Name = "A", Padding = string.Empty }, MigrationJsonContext.Default.BoundedTargetInput).Length;
        int exactPaddingLength = maximum - emptyLength;

        BaseInstalledActivationMigration<BoundedSourceInput, BoundedTargetInput> exact = BoundedMigration(
            new string('x', exactPaddingLength), maximum);
        exact.Project("{\"name\":\"A\"}"u8).Should().HaveCount(maximum);

        BaseInstalledActivationMigration<BoundedSourceInput, BoundedTargetInput> oversized = BoundedMigration(
            new string('x', exactPaddingLength + 1), maximum);
        Action action = () => oversized.Project("{\"name\":\"A\"}"u8);
        action.Should().Throw<JsonException>().WithMessage("base.activation.migrationInvalid");
    }

    [Fact]
    public void Generated_converter_bearing_scalars_project_with_exact_authority_and_reject_hostile_enum_wire_values()
    {
        var installed = ConverterMigration();
        const string canonical = "{\"tenantId\":\"01234567-89ab-cdef-0123-456789abcdef\",\"nonce\":\"AQIDBA==\",\"mode\":\"active\"}";

        installed.Project(System.Text.Encoding.UTF8.GetBytes(canonical)).AsSpan()
            .SequenceEqual(System.Text.Encoding.UTF8.GetBytes(canonical)).Should().BeTrue();

        const string hostile = "{\"tenantId\":\"01234567-89ab-cdef-0123-456789abcdef\",\"nonce\":\"AQIDBA==\",\"mode\":\"unknown\"}";
        Action action = () => installed.Project(System.Text.Encoding.UTF8.GetBytes(hostile));
        action.Should().Throw<BaseActivationDtoContractException>()
            .WithMessage("base.activation.providerContractInvalid");
    }

    [Fact]
    public void Missing_target_leaf_fails_graph_construction()
    {
        BaseActivationMigrationRegistration<SourceInput, TargetInput> registration = Builder()
            .Map(TargetActivationDtos.InputProperties.DisplayName, SourceActivationDtos.InputProperties.Name).Create(Draft());
        Action action = () => _ = new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration);
        action.Should().Throw<InvalidOperationException>().WithMessage("base.activation.migrationInvalid");
    }

    [Fact]
    public void Foreign_owner_property_handle_is_rejected_before_registration()
    {
        var foreign = new BaseActivationInputProperty<TargetInput, string>(
            TargetActivationDtos.InputProperties.DisplayName.Authority,
            SourceActivationDtos.HPDBaseActivationDtoAuthority.InputDtoAuthorityChecksum);

        Action action = () => Builder().Map(foreign, SourceActivationDtos.InputProperties.Name);

        action.Should().Throw<InvalidOperationException>().WithMessage("base.activation.migrationInvalid");
    }

    [Fact]
    public void Registration_resolves_metadata_from_the_finalized_owner_at_installation()
    {
        BaseActivationMigrationRegistration<SourceInput, TargetInput> registration = CompleteBuilder().Create(Draft());
        object provisionalSource = registration.SourceAuthority.CurrentInputTypeInfo;
        object provisionalTarget = registration.TargetAuthority.CurrentInputTypeInfo;

        _ = BaseSerializerMetadataOwner.Create([
            (IBaseSerializerMetadataSource)SourceRegistration.Identity,
            (IBaseSerializerMetadataSource)TargetRegistration.Identity,
        ]);
        var installed = new BaseInstalledActivationMigration<SourceInput, TargetInput>(registration);

        registration.SourceAuthority.CurrentInputTypeInfo.Should().NotBeSameAs(provisionalSource);
        registration.TargetAuthority.CurrentInputTypeInfo.Should().NotBeSameAs(provisionalTarget);
        installed.Project("{\"name\":\"Ada\"}"u8).Should().NotBeEmpty();
    }

    [Fact]
    public void Transient_accounting_accepts_the_exact_ceiling_and_rejects_one_more_byte()
    {
        long total = 0;
        BaseInstalledActivationMigration<SourceInput, TargetInput>.ChargeTransient(
            ref total, 16L * 1024 * 1024, providerInfluenced: false);

        total.Should().Be(16L * 1024 * 1024);
        Action action = () => BaseInstalledActivationMigration<SourceInput, TargetInput>.ChargeTransient(
            ref total, 1, providerInfluenced: false);
        action.Should().Throw<JsonException>().WithMessage("base.activation.migrationInvalid");
    }

    private static BaseActivationMigrationProjectionBuilder<SourceInput, TargetInput> Builder() =>
        BaseActivationMigrationBuilder.From(SourceRegistration, SourceActivationDtos.HPDBaseActivationDtoAuthority)
            .To(TargetRegistration, TargetActivationDtos.HPDBaseActivationDtoAuthority);
    private static BaseActivationMigrationProjectionBuilder<SourceInput, TargetInput> CompleteBuilder() => Builder()
        .Map(TargetActivationDtos.InputProperties.DisplayName, SourceActivationDtos.InputProperties.Name)
        .Map(TargetActivationDtos.InputProperties.Note, SourceActivationDtos.InputProperties.Note)
        .Constant(TargetActivationDtos.InputProperties.Enabled, true);
    private static BaseActivationMigrationDraft Draft() => new()
    { Id = "example.migration", Version = 1, OwningModuleId = "example", GrantId = "example.source.migrate" };

    private static BaseActivationHandlerRegistration<SourceInput, SourceResult> SourceRegistration { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(Definition("example.source"),
            SourceActivationDtos.HPDBaseActivationDtoAuthority, static _ => new SourceHandler());
    private static BaseActivationHandlerRegistration<TargetInput, TargetResult> TargetRegistration { get; } =
        BaseActivationDefinitionBuilder.CreateGenerated(Definition("example.target"),
            TargetActivationDtos.HPDBaseActivationDtoAuthority, static _ => new TargetHandler());

    private static BaseInstalledActivationMigration<BoundedSourceInput, BoundedTargetInput> BoundedMigration(
        string padding, int maximumTargetBytes)
    {
        BaseActivationHandlerRegistration<BoundedSourceInput, SourceResult> source =
            BaseActivationDefinitionBuilder.CreateGenerated(Definition("example.bounded.source", maximumTargetBytes),
                BoundedSourceActivationDtos.HPDBaseActivationDtoAuthority, static _ => new BoundedSourceHandler());
        BaseActivationHandlerRegistration<BoundedTargetInput, TargetResult> target =
            BaseActivationDefinitionBuilder.CreateGenerated(Definition("example.bounded.target", maximumTargetBytes),
                BoundedTargetActivationDtos.HPDBaseActivationDtoAuthority, static _ => new BoundedTargetHandler());
        BaseActivationMigrationRegistration<BoundedSourceInput, BoundedTargetInput> registration =
            BaseActivationMigrationBuilder.From(source, BoundedSourceActivationDtos.HPDBaseActivationDtoAuthority)
                .To(target, BoundedTargetActivationDtos.HPDBaseActivationDtoAuthority)
                .Map(BoundedTargetActivationDtos.InputProperties.Name, BoundedSourceActivationDtos.InputProperties.Name)
                .Constant(BoundedTargetActivationDtos.InputProperties.Padding, padding)
                .Create(Draft());
        return new BaseInstalledActivationMigration<BoundedSourceInput, BoundedTargetInput>(registration);
    }

    private static BaseInstalledActivationMigration<ConverterSourceInput, ConverterTargetInput> ConverterMigration()
    {
        BaseActivationHandlerRegistration<ConverterSourceInput, SourceResult> source =
            BaseActivationDefinitionBuilder.CreateGenerated(Definition("example.converter.source"),
                ConverterSourceActivationDtos.HPDBaseActivationDtoAuthority, static _ => new ConverterSourceHandler());
        BaseActivationHandlerRegistration<ConverterTargetInput, TargetResult> target =
            BaseActivationDefinitionBuilder.CreateGenerated(Definition("example.converter.target"),
                ConverterTargetActivationDtos.HPDBaseActivationDtoAuthority, static _ => new ConverterTargetHandler());
        BaseActivationMigrationRegistration<ConverterSourceInput, ConverterTargetInput> registration =
            BaseActivationMigrationBuilder.From(source, ConverterSourceActivationDtos.HPDBaseActivationDtoAuthority)
                .To(target, ConverterTargetActivationDtos.HPDBaseActivationDtoAuthority)
                .Map(ConverterTargetActivationDtos.InputProperties.TenantId, ConverterSourceActivationDtos.InputProperties.TenantId)
                .Map(ConverterTargetActivationDtos.InputProperties.Nonce, ConverterSourceActivationDtos.InputProperties.Nonce)
                .Map(ConverterTargetActivationDtos.InputProperties.Mode, ConverterSourceActivationDtos.InputProperties.Mode)
                .Create(Draft());
        return new BaseInstalledActivationMigration<ConverterSourceInput, ConverterTargetInput>(registration);
    }

    private static BaseActivationDefinitionDraft Definition(string id, int maximumInputBytes = 1024) => new()
    {
        Id = id, Version = 1, OwningModuleId = "example", ExecutionClass = BaseActivationExecutionClass.AtLeastOnceWorker,
        Grants = new BaseActivationGrantSet { Enqueue = id + ".enqueue", Observe = id + ".observe", Claim = id + ".claim",
            Execute = id + ".execute", Renew = id + ".renew", Complete = id + ".complete", Fail = id + ".fail", Yield = id + ".yield", Cancel = id + ".cancel", Inspect = id + ".inspect", Replay = id + ".replay", Migrate = id + ".migrate",
            Reconcile = id + ".reconcile", Retry = id + ".retry", Dispose = id + ".dispose", Remove = id + ".remove", Repair = id + ".repair" },
        SourceGrantIds = [], Retry = new BaseActivationRetryProfile { MaximumAttempts = 1, InitialDelayMilliseconds = 1,
            MaximumDelayMilliseconds = 1, MultiplierNumerator = 1, MultiplierDenominator = 1, JitterBasisPoints = 0,
            RetryableFailureCodes = [] },
        ReceiptRetention = new BaseActivationReceiptRetentionPolicy
        {
            FormatVersion = 1, DuplicateResolutionLifetime = TimeSpan.FromHours(24),
            ProtectedBackupCoverage = BaseActivationProtectedBackupCoverage.NotRequired,
        },
        Limits = new BaseActivationLimits { MaximumInputBytes = maximumInputBytes, MaximumResultBytes = 1024, MaximumAttempts = 1, MaximumYields = 0,
            MaximumRenewalsPerSlice = 1, MaximumChildrenPerSlice = 1, MaximumLineageDepth = 1,
            LeaseDuration = TimeSpan.FromSeconds(5), HandlerTimeout = TimeSpan.FromSeconds(5),
            Provider = ProviderLimits(), AtomicCreation = AtomicLimits() },
        Handler = new BaseActivationHandlerDraft { Id = id + ".handler", Version = 1, FactoryId = id + ".factory",
            WorkerSubjectKind = AccessSubjectKind.ServicePrincipal,
            SemanticAuthority = BaseActivationHandlerSemanticAuthority.Create(id + ".semantics", 1) },
    };
    private static BaseActivationExecutionLimits ProviderLimits() => new() { MaximumCandidates = 1, MaximumInputBytes = 1024,
        MaximumResultBytes = 1024, MaximumEvidenceBytes = 4096, MaximumTransientBytes = 8192, MaximumReadIntervals = 1,
        MaximumIndexOperations = 1, AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5) };
    private static BaseAtomicMutationExecutionLimits AtomicLimits() => new() { MaximumItems = 1, MaximumQueryNodes = 1,
        MaximumQueryDepth = 1, MaximumLiteralValues = 1, MaximumSelectedRecords = 1, MaximumProducedMutations = 1,
        MaximumQueryExecutions = 1, MaximumPreviousStateRequirements = 1, MaximumRecordCaptures = 1,
        MaximumRelationTargetCaptures = 1, MaximumSelectedBytes = 1024, MaximumEvidenceBytes = 4096,
        MaximumTransientBytes = 8192, MaximumReadIntervals = 1, MaximumSubjectValidations = 1, MaximumAuthorityReads = 1,
        MaximumRelationChecks = 1, MaximumUniqueConstraintChecks = 1, MaximumRequestBytes = 1024, MaximumGenerationBytes = 1024,
        MaximumWrittenBytes = 1024, MaximumFactBytes = 1024, MaximumJournalBytes = 1024, MaximumReceiptBytes = 4096,
        MaximumResultBytes = 1024, MaximumGenerationReads = 1, MaximumGenerationComparisons = 1,
        MaximumGenerationIncrements = 1, MaximumGuardNodes = 1, MaximumExpressionNodes = 1, MaximumStatements = 1,
        MaximumBranches = 1, MaximumGuardDepth = 1, MaximumRetirementProjections = 1, MaximumRetirementBarrierReads = 1,
        MaximumRetirementAcknowledgementReads = 1, MaximumRetirementPublications = 1, MaximumRetirementEvidenceBytes = 1024,
        MaximumRetirementPublicationBytes = 1024, Deadlines = new BaseAtomicMutationDeadlines {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitObservationTimeout = TimeSpan.FromSeconds(5), ReceiptResolutionTimeout = TimeSpan.FromSeconds(5) } };

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "migration-administration",
    };

    private static BaseMutationRequestIdentity Identity(string operation) => BaseMutationRequestIdentity.Create(
        "migration-administration-test", operation, operation,
        BaseMutationRequestFingerprint.Create(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operation))));

    private static void AddGrant(HPDBaseBuilder builder, string id, string action) =>
        builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
        {
            Id = id, Version = 1, OwningModuleId = "example",
            SourceContractId = "example.migration.grants", SourceContractVersion = 1,
        }, new AccessGrant
        {
            Id = id, ApplicationId = "migration-administration-test", ModuleId = "example",
            Audience = HPDBaseEndpointAudience.ControlPlane,
            Subject = new AccessSubject { Kind = AccessSubjectKind.System, Id = "migration-administration" },
            Action = action, Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
        });

    public enum MigrationHostility
    {
        CandidateCanonicalBytes,
        CandidateChecksum,
        CandidateControlChecksum,
        CandidateIdentity,
        CommitIdentity,
        CommitSourceControlChecksum,
        CommitReplacementControlChecksum,
        CommitAccounting,
    }

    public enum CompactionHostility
    {
        None,
        Authority,
        CommittedResult,
        UnknownFailure,
    }

    private sealed class AllowPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PolicyDecision { Effect = PolicyEffect.Allow, Outcome = PolicyOutcome.Allowed });
    }

    private sealed class HostileMigrationProvider(InMemoryRecordStore inner) : IAtomicRecordStore, IBaseActivationProvider
    {
        public MigrationHostility Hostility { get; set; }
        public int CandidateCalls { get; private set; }
        public int MigrationCalls { get; private set; }
        public CompactionHostility CompactionHostility { get; init; }
        public StoreCapabilityDescriptor Capabilities => inner.Capabilities;
        public BaseActivationProviderDescriptor Descriptor => ((IBaseActivationProvider)inner).Descriptor;
        public ValueTask<OperationResult<BaseActivationYieldReservationState>> ReadYieldReservationStateAsync(
            CancellationToken cancellationToken = default) => inner.ReadYieldReservationStateAsync(cancellationToken);
        public async ValueTask<OperationResult<BaseActivationReceiptCompactionAuthority>> CaptureReceiptCompactionAuthorityAsync(
            BaseActivationReceiptCompactionAuthorityRequest request, CancellationToken cancellationToken = default)
        {
            OperationResult<BaseActivationReceiptCompactionAuthority> result =
                await inner.CaptureReceiptCompactionAuthorityAsync(request, cancellationToken);
            if (CompactionHostility != CompactionHostility.Authority
                || !result.IsSuccess() || result.Value is null) return result;
            return result with
            {
                Value = result.Value with
                {
                    Reservation = result.Value.Reservation with { Checksum = [] },
                },
            };
        }
        public async ValueTask<OperationResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
            BaseActivationReceiptCompactionRequest request, CancellationToken cancellationToken = default)
        {
            if (CompactionHostility == CompactionHostility.UnknownFailure)
                return new OperationResult<BaseActivationReceiptCompactionResult>
                {
                    Status = OperationStatus.ValidationFailed,
                    Error = new BaseError
                    {
                        Code = "hostile.compaction.failure",
                        Message = "Hostile provider detail.",
                        Category = ErrorCategory.Validation,
                    },
                };
            OperationResult<BaseActivationReceiptCompactionResult> result =
                await inner.CompactActivationReceiptsAsync(request, cancellationToken);
            if (CompactionHostility != CompactionHostility.CommittedResult
                || !result.IsSuccess() || result.Value is null) return result;
            return result with
            {
                Value = result.Value with
                {
                    DeletedYieldReceiptCount = checked(result.Value.DeletedCount + 1),
                },
            };
        }
        public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query,
            OperationContext context, CancellationToken cancellationToken = default) => inner.ListAsync(collection, query, context, cancellationToken);
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id,
            OperationContext context, CancellationToken cancellationToken = default) => inner.GetAsync(collection, id, context, cancellationToken);
        public ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(IAtomicMutationProcessor processor,
            RecordMutationExecutionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExecuteSingleAsync(processor, request, cancellationToken);
        public ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(IAtomicMutationProcessor processor,
            RecordMutationExecutionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExecuteAtomicAsync(processor, request, cancellationToken);
        public ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(IAtomicMutationProcessor processor,
            BaseMutationRequestIdentity identity, TimeSpan resolutionTimeout, CancellationToken cancellationToken = default) =>
            inner.ResolveAtomicReceiptAsync(processor, identity, resolutionTimeout, cancellationToken);
        public ValueTask<OperationResult<BaseAtomicMutationAuthorityRequirement>> CaptureAtomicMutationAuthorityRequirementAsync(
            string applicationId, ImmutableArray<CollectionDefinition> collections, BaseAtomicMutationExecutionLimits limits,
            CancellationToken cancellationToken = default) =>
            inner.CaptureAtomicMutationAuthorityRequirementAsync(applicationId, collections, limits, cancellationToken);
        public ValueTask<OperationResult<BaseActivationDependencyResult>> ReadDependenciesAsync(BaseActivationDependencyRequest request,
            CancellationToken cancellationToken = default) => inner.ReadDependenciesAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(BaseActivationDueObservationRequest request,
            CancellationToken cancellationToken = default) => inner.ObserveDueAsync(request, cancellationToken);
        public ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(BaseDueObservationToken token, DateTimeOffset deadline,
            CancellationToken cancellationToken = default) => inner.WaitForDueChangeAsync(token, deadline, cancellationToken);
        public ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(BaseActivationClaimRequest request,
            CancellationToken cancellationToken = default) => inner.TryClaimNextAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseTransactionalActivationCandidate>> ReadTransactionalCandidateAsync(
            BaseTransactionalActivationCandidateRequest request, CancellationToken cancellationToken = default) => inner.ReadTransactionalCandidateAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(BaseActivationRenewRequest request,
            CancellationToken cancellationToken = default) => inner.RenewAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(BaseExecutorRegistrationRequest request,
            CancellationToken cancellationToken = default) => inner.RegisterExecutorAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(BaseExecutorHeartbeatRequest request,
            CancellationToken cancellationToken = default) => inner.HeartbeatExecutorAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(BaseExecutorRetirementRequest request,
            CancellationToken cancellationToken = default) => inner.RetireExecutorAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(BaseActivationTransitionRequest request,
            CancellationToken cancellationToken = default) => inner.TransitionAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(string scheduleId, int scheduleVersion,
            CancellationToken cancellationToken = default) => inner.ReadScheduleAsync(scheduleId, scheduleVersion, cancellationToken);
        public ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(BaseScheduleMutationRequest request,
            CancellationToken cancellationToken = default) => inner.MutateScheduleAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(BaseScheduleMaintenanceRequest request,
            CancellationToken cancellationToken = default) => inner.AdvanceSchedulesAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
            BaseScheduleCancellationMaintenanceRequest request, CancellationToken cancellationToken = default) => inner.AdvanceScheduleCancellationAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationAdministrationPage>> ReadAdministrationAsync(
            BaseActivationAdministrationQueryRequest request, CancellationToken cancellationToken = default) => inner.ReadAdministrationAsync(request, cancellationToken);

        public async ValueTask<OperationResult<BaseActivationMigrationCandidate>> ReadMigrationCandidateAsync(
            BaseActivationMigrationCandidateRequest request, CancellationToken cancellationToken = default)
        {
            CandidateCalls++;
            OperationResult<BaseActivationMigrationCandidate> result = await inner.ReadMigrationCandidateAsync(request, cancellationToken);
            if (!result.IsSuccess() || result.Value is null) return result;
            BaseActivationMigrationCandidate value = result.Value;
            value = Hostility switch
            {
                MigrationHostility.CandidateCanonicalBytes => value with
                {
                    CanonicalInput = "{ \"name\":\"Ada\"}"u8.ToArray().ToImmutableArray(),
                    InputChecksum = SHA256.HashData("{ \"name\":\"Ada\"}"u8).ToImmutableArray(),
                },
                MigrationHostility.CandidateChecksum => value with { InputChecksum = Enumerable.Repeat((byte)0xA5, 32).ToImmutableArray() },
                MigrationHostility.CandidateControlChecksum => value with { ControlChecksum = Enumerable.Repeat((byte)0xA6, 32).ToImmutableArray() },
                MigrationHostility.CandidateIdentity => value with { ActivationId = "hostile-activation" },
                _ => value,
            };
            return result with { Value = value };
        }

        public async ValueTask<OperationResult<BaseActivationMigrationResult>> MigrateAsync(
            BaseActivationMigrationRequest request, CancellationToken cancellationToken = default)
        {
            MigrationCalls++;
            OperationResult<BaseActivationMigrationResult> result = await inner.MigrateAsync(request, cancellationToken);
            if (!result.IsSuccess() || result.Value is null) return result;
            BaseActivationMigrationResult value = result.Value;
            value = Hostility switch
            {
                MigrationHostility.CommitIdentity => value with { ReplacementActivationId = "hostile-replacement" },
                MigrationHostility.CommitSourceControlChecksum => value with
                { SourceControlChecksum = Enumerable.Repeat((byte)0xA7, 32).ToImmutableArray() },
                MigrationHostility.CommitReplacementControlChecksum => value with
                { ReplacementControlChecksum = Enumerable.Repeat((byte)0xA8, 32).ToImmutableArray() },
                MigrationHostility.CommitAccounting => value with
                {
                    Accounting = value.Accounting with { Candidates = checked(value.Accounting.Candidates + 1) },
                },
                _ => value,
            };
            return result with { Value = value };
        }

        public ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
            BaseActivationReceiptResolutionRequest request, CancellationToken cancellationToken = default) => inner.ResolveReceiptAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationMaintenancePage>> AdvanceMaintenanceAsync(
            BaseActivationMaintenanceRequest request, CancellationToken cancellationToken = default) => inner.AdvanceMaintenanceAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationPrunePage>> PruneAsync(BaseActivationPruneRequest request,
            CancellationToken cancellationToken = default) => inner.PruneAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationIndeterminateResolution>> ResolveIndeterminateAsync(
            BaseActivationIndeterminateRequest request, CancellationToken cancellationToken = default) => inner.ResolveIndeterminateAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseActivationQuarantinePage>> ReadQuarantineAsync(BaseActivationQuarantineRequest request,
            CancellationToken cancellationToken = default) => inner.ReadQuarantineAsync(request, cancellationToken);
    }
}

[BaseActivationDtoAuthority("example.source.dto", 1, "example", "example.source.input", "example.source.result",
    typeof(MigrationJsonContext), typeof(SourceInput), typeof(SourceResult))] internal static partial class SourceActivationDtos;
[BaseActivationDtoAuthority("example.target.dto", 1, "example", "example.target.input", "example.target.result",
    typeof(MigrationJsonContext), typeof(TargetInput), typeof(TargetResult))] internal static partial class TargetActivationDtos;
internal sealed record SourceInput { [BaseField("source.name", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 64)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Name { get; init; } [BaseField("source.note", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.Nullable, MaximumUtf8Bytes = 64)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? Note { get; init; } }
internal sealed record TargetInput { [BaseField("source.name", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 64)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string DisplayName { get; init; } [BaseField("source.note", Presence = BaseFieldPresence.Optional, Nullability = BaseFieldNullability.Nullable, MaximumUtf8Bytes = 64)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public string? Note { get; init; } [BaseField("target.enabled")][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Enabled { get; init; } }
internal sealed record SourceResult { [BaseField("source.result")][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Done { get; init; } }
internal sealed record TargetResult { [BaseField("target.result")][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required bool Done { get; init; } }
internal sealed record BoundedSourceInput { [BaseField("bounded.name", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 16)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Name { get; init; } }
internal sealed record BoundedTargetInput { [BaseField("bounded.name", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 16)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Name { get; init; } [BaseField("bounded.padding", MaximumUtf8Bytes = 2048)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required string Padding { get; init; } }
internal enum MigrationMode { active, passive }
internal sealed record ConverterSourceInput
{
    [BaseField("converter.tenantId")][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("converter.nonce", MinimumBytes = 4, MaximumBytes = 4)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseBinary Nonce { get; init; }
    [BaseField("converter.mode", AllowedEnumLiterals = ["active", "passive"])][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)][JsonConverter(typeof(BaseClosedEnumJsonConverter<MigrationMode>))] public required MigrationMode Mode { get; init; }
}
internal sealed record ConverterTargetInput
{
    [BaseField("converter.tenantId")][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)][JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("converter.nonce", MinimumBytes = 4, MaximumBytes = 4)][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)] public required BaseBinary Nonce { get; init; }
    [BaseField("converter.mode", AllowedEnumLiterals = ["active", "passive"])][BaseFieldConfidentiality(BaseFieldConfidentiality.Internal)][JsonConverter(typeof(BaseClosedEnumJsonConverter<MigrationMode>))] public required MigrationMode Mode { get; init; }
}
internal sealed class SourceHandler : IBaseActivationHandler<SourceInput, SourceResult> { public ValueTask<BaseActivationHandlerResult<SourceResult>> ExecuteAsync(BaseActivationContext context, SourceInput input, CancellationToken cancellationToken) => ValueTask.FromResult<BaseActivationHandlerResult<SourceResult>>(new BaseActivationSucceeded<SourceResult> { Result = new() { Done = true } }); }
internal sealed class TargetHandler : IBaseActivationHandler<TargetInput, TargetResult> { public ValueTask<BaseActivationHandlerResult<TargetResult>> ExecuteAsync(BaseActivationContext context, TargetInput input, CancellationToken cancellationToken) => ValueTask.FromResult<BaseActivationHandlerResult<TargetResult>>(new BaseActivationSucceeded<TargetResult> { Result = new() { Done = true } }); }
internal sealed class BoundedSourceHandler : IBaseActivationHandler<BoundedSourceInput, SourceResult> { public ValueTask<BaseActivationHandlerResult<SourceResult>> ExecuteAsync(BaseActivationContext context, BoundedSourceInput input, CancellationToken cancellationToken) => ValueTask.FromResult<BaseActivationHandlerResult<SourceResult>>(new BaseActivationSucceeded<SourceResult> { Result = new() { Done = true } }); }
internal sealed class BoundedTargetHandler : IBaseActivationHandler<BoundedTargetInput, TargetResult> { public ValueTask<BaseActivationHandlerResult<TargetResult>> ExecuteAsync(BaseActivationContext context, BoundedTargetInput input, CancellationToken cancellationToken) => ValueTask.FromResult<BaseActivationHandlerResult<TargetResult>>(new BaseActivationSucceeded<TargetResult> { Result = new() { Done = true } }); }
internal sealed class ConverterSourceHandler : IBaseActivationHandler<ConverterSourceInput, SourceResult> { public ValueTask<BaseActivationHandlerResult<SourceResult>> ExecuteAsync(BaseActivationContext context, ConverterSourceInput input, CancellationToken cancellationToken) => ValueTask.FromResult<BaseActivationHandlerResult<SourceResult>>(new BaseActivationSucceeded<SourceResult> { Result = new() { Done = true } }); }
internal sealed class ConverterTargetHandler : IBaseActivationHandler<ConverterTargetInput, TargetResult> { public ValueTask<BaseActivationHandlerResult<TargetResult>> ExecuteAsync(BaseActivationContext context, ConverterTargetInput input, CancellationToken cancellationToken) => ValueTask.FromResult<BaseActivationHandlerResult<TargetResult>>(new BaseActivationSucceeded<TargetResult> { Result = new() { Done = true } }); }
[BaseActivationDtoAuthority("example.bounded.source.dto", 1, "example", "example.bounded.source.input", "example.source.result",
    typeof(MigrationJsonContext), typeof(BoundedSourceInput), typeof(SourceResult))] internal static partial class BoundedSourceActivationDtos;
[BaseActivationDtoAuthority("example.bounded.target.dto", 1, "example", "example.bounded.target.input", "example.target.result",
    typeof(MigrationJsonContext), typeof(BoundedTargetInput), typeof(TargetResult))] internal static partial class BoundedTargetActivationDtos;
[BaseActivationDtoAuthority("example.converter.source.dto", 1, "example", "example.converter.source.input", "example.source.result",
    typeof(MigrationJsonContext), typeof(ConverterSourceInput), typeof(SourceResult))] internal static partial class ConverterSourceActivationDtos;
[BaseActivationDtoAuthority("example.converter.target.dto", 1, "example", "example.converter.target.input", "example.target.result",
    typeof(MigrationJsonContext), typeof(ConverterTargetInput), typeof(TargetResult))] internal static partial class ConverterTargetActivationDtos;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceInput))][JsonSerializable(typeof(TargetInput))][JsonSerializable(typeof(SourceResult))][JsonSerializable(typeof(TargetResult))]
[JsonSerializable(typeof(BoundedSourceInput))][JsonSerializable(typeof(BoundedTargetInput))]
[JsonSerializable(typeof(ConverterSourceInput))][JsonSerializable(typeof(ConverterTargetInput))]
internal sealed partial class MigrationJsonContext : JsonSerializerContext;
