using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Base.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using HPD.Base.Tests.Operations;

#pragma warning disable HPDBASE0461 // Manual parity and hostile graph cases intentionally cross the generated-only boundary.

namespace HPD.Base.Tests.Subjects;

public sealed class L45SubjectTransactionTests
{
    [Fact]
    public async Task L48_tombstone_atomically_creates_the_required_consumer_barrier()
    {
        await using SubjectFixture fixture=Build(retirement:true);
        IBaseRecordRuntime records=fixture.Services.GetRequiredService<IBaseRecordRuntime>();PrincipalContext principal=Principal();
        OperationResult<RecordEnvelope> created=await records.CreateAsync(Private.Id,Create("retirement-user",("active",true),("tenant","tenant-a")),principal,Operation(BaseOperationKind.Create,Private.Id));Assert.True(created.IsSuccess(),created.Error?.Code);
        JsonElement encoded=await fixture.AcquireAsync("retirement-user");var reference=new BaseSubjectReference<UserSubject>(BaseSubjectId.Create(encoded.GetProperty("subjectId").GetString()!,BaseSubjectIdKind.OrdinalString),BaseSubjectAuthorityEpoch.Parse(encoded.GetProperty("authorityEpoch").GetString()!),BaseSubjectIncarnation.Parse(encoded.GetProperty("incarnation").GetString()!));
        RecordEnvelope current=(await records.GetAsync(Private.Id,new RecordId("retirement-user"),principal,Operation(BaseOperationKind.Get,Private.Id))).Value!;
        BaseSession session=fixture.Services.GetRequiredService<IBaseSessionFactory>().For(principal);BaseExportedSubjectContract<UserSubject> exporter=session.GetExportedSubjectContract<UserSubject>(fixture.Registration);
        BaseResult<BaseSubjectLifecycleFact<UserSubject>> tombstoned=await exporter.TombstoneAsync(new(){Subject=reference,ExpectedPrivateRevision=current.Metadata.Revision!.Value,Identity=BaseMutationRequestIdentity.Create("l48-tests","tombstone","barrier-create",BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("l48-barrier-create"u8)))});
        Assert.True(tombstoned is BaseSuccess<BaseSubjectLifecycleFact<UserSubject>>,tombstoned is BaseFailure<BaseSubjectLifecycleFact<UserSubject>> failed?$"{failed.Error.Code}: {failed.Error.Message}":null);
        BaseSubjectRetirementPolicy policy=fixture.Services.GetRequiredService<BaseSubjectRetirementRegistry>().Policies.Single().Definition;
        OperationResult<BaseSubjectRetirementBarrierPage> page=await fixture.Store.ReadBarriersAsync(new(){ApplicationId="test.application",ContractId="example.user",ContractVersion=1,ScopeAuthority=new(){Mode=BaseSubjectScopeQueryMode.ExactScope,ExactScope=new(){Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},InstalledAuthorityDigest=fixture.Registration.Checksum},Take=16,MaximumResultBytes=65_536,DeadlineUtc=DateTimeOffset.UtcNow.AddMinutes(1)});
        Assert.True(page.IsSuccess(),page.Error?.Code);BaseSubjectRetirementBarrier barrier=Assert.Single(page.Value!.Barriers).Barrier;
        Assert.Equal(reference.AuthorityEpoch,barrier.AuthorityEpoch);Assert.Equal(reference.Incarnation,barrier.Incarnation);Assert.Equal(BaseSubjectRetirementBarrierState.Pending,barrier.State);Assert.Equal(1,barrier.Generation);
        Assert.Equal(BaseSubjectRetirementRegistry.AcceptedSetChecksum(policy.AcceptedConsumers),barrier.RequiredConsumerSetChecksum);
    }

    [Fact]
    public async Task L48_required_delivery_acknowledges_once_and_satisfies_the_barrier()
    {
        await using SubjectFixture fixture=Build(retirement:true);PrincipalContext principal=Principal();IBaseRecordRuntime records=fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await records.CreateAsync(Private.Id,Create("ack-user",("active",true),("tenant","tenant-a")),principal,Operation(BaseOperationKind.Create,Private.Id))).IsSuccess());
        JsonElement encoded=await fixture.AcquireAsync("ack-user");var reference=new BaseSubjectReference<UserSubject>(BaseSubjectId.Create(encoded.GetProperty("subjectId").GetString()!,BaseSubjectIdKind.OrdinalString),BaseSubjectAuthorityEpoch.Parse(encoded.GetProperty("authorityEpoch").GetString()!),BaseSubjectIncarnation.Parse(encoded.GetProperty("incarnation").GetString()!));RecordEnvelope current=(await records.GetAsync(Private.Id,new("ack-user"),principal,Operation(BaseOperationKind.Get,Private.Id))).Value!;
        BaseSession session=fixture.Services.GetRequiredService<IBaseSessionFactory>().For(principal);BaseExportedSubjectContract<UserSubject> exporter=session.GetExportedSubjectContract<UserSubject>(fixture.Registration);BaseResult<BaseSubjectLifecycleFact<UserSubject>> tombstone=await exporter.TombstoneAsync(new(){Subject=reference,ExpectedPrivateRevision=current.Metadata.Revision!.Value,Identity=Identity("ack-tombstone")});Assert.IsType<BaseSuccess<BaseSubjectLifecycleFact<UserSubject>>>(tombstone);
        BaseSubjectLifecycleConsumerDefinition lifecycle=LifecycleConsumer();var lifecycleIdentity=BaseGeneratedSubjectLifecycleConsumers.Register<UserSubject>(lifecycle,fixture.Registration);BaseSubjectRetirementConsumerDefinition retirement=RetirementConsumer(BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(lifecycle),fixture.Registration.Checksum));var retirementIdentity=BaseGeneratedSubjectRetirementConsumers.Register(lifecycleIdentity,retirement);BaseInstalledSubjectRetirementConsumer<UserSubject> consumer=session.SubjectRetirements.Get(retirementIdentity);
        await using IAsyncEnumerator<BaseSubjectRequiredLifecycleDelivery<UserSubject>> deliveries=consumer.ReadRequiredAsync().GetAsyncEnumerator();Assert.True(await deliveries.MoveNextAsync());BaseSubjectRequiredLifecycleDelivery<UserSubject> delivery=deliveries.Current;
        BaseResult<BaseSubjectAcknowledgementResult> accepted=await consumer.AcknowledgeAsync(delivery.Acknowledgement,BaseSubjectAcknowledgementDisposition.Completed,delivery.AcknowledgementIdentity);Assert.True(accepted is BaseSuccess<BaseSubjectAcknowledgementResult>,accepted is BaseFailure<BaseSubjectAcknowledgementResult> failed?failed.Error.Code:null);BaseSubjectAcknowledgementResult applied=accepted.RequireValue();Assert.Equal(BaseSubjectRetirementMutationOutcome.Applied,applied.Outcome);Assert.Equal(BaseSubjectRetirementBarrierState.Satisfied,applied.BarrierState);Assert.Equal(2,applied.BarrierGeneration);
        BaseResult<BaseSubjectAcknowledgementResult> duplicate=await consumer.AcknowledgeAsync(delivery.Acknowledgement,BaseSubjectAcknowledgementDisposition.Completed,delivery.AcknowledgementIdentity);Assert.Equal(BaseSubjectRetirementMutationOutcome.Duplicate,duplicate.RequireValue().Outcome);
    }

    [Fact]
    public async Task L48_timeout_quarantines_and_identified_override_advances_once()
    {
        var clock=new LifecycleTimeProvider(new DateTimeOffset(2030,1,1,0,0,0,TimeSpan.Zero));await using SubjectFixture fixture=Build(retirement:true,timeProvider:clock);PrincipalContext principal=Principal();IBaseRecordRuntime records=fixture.Services.GetRequiredService<IBaseRecordRuntime>();Assert.True((await records.CreateAsync(Private.Id,Create("timeout-user",("active",true),("tenant","tenant-a")),principal,Operation(BaseOperationKind.Create,Private.Id))).IsSuccess());JsonElement encoded=await fixture.AcquireAsync("timeout-user");var reference=new BaseSubjectReference<UserSubject>(BaseSubjectId.Create(encoded.GetProperty("subjectId").GetString()!,BaseSubjectIdKind.OrdinalString),BaseSubjectAuthorityEpoch.Parse(encoded.GetProperty("authorityEpoch").GetString()!),BaseSubjectIncarnation.Parse(encoded.GetProperty("incarnation").GetString()!));RecordEnvelope current=(await records.GetAsync(Private.Id,new("timeout-user"),principal,Operation(BaseOperationKind.Get,Private.Id))).Value!;BaseSession session=fixture.Services.GetRequiredService<IBaseSessionFactory>().For(principal);Assert.IsType<BaseSuccess<BaseSubjectLifecycleFact<UserSubject>>>(await session.GetExportedSubjectContract<UserSubject>(fixture.Registration).TombstoneAsync(new(){Subject=reference,ExpectedPrivateRevision=current.Metadata.Revision!.Value,Identity=Identity("timeout-tombstone")}));
        BaseSubjectRetirementBarrier barrier=Assert.Single((await fixture.Store.ReadBarriersAsync(new(){ApplicationId="test.application",ContractId="example.user",ContractVersion=1,ScopeAuthority=new(){Mode=BaseSubjectScopeQueryMode.ExactScope,ExactScope=new(){Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},InstalledAuthorityDigest=fixture.Registration.Checksum},Take=4,MaximumResultBytes=65_536,DeadlineUtc=clock.GetUtcNow().AddMinutes(1)})).Value!.Barriers).Barrier;clock.Advance(TimeSpan.FromHours(1)+TimeSpan.FromTicks(1));BaseSubjectRetirementPolicy policy=fixture.Services.GetRequiredService<BaseSubjectRetirementRegistry>().Policies.Single().Definition;
        var timeoutRequest=new BaseSubjectRetirementTimeoutRequest{ContractId="example.user",ContractVersion=1,SubjectId=reference.SubjectId,AuthorityEpoch=reference.AuthorityEpoch,Incarnation=reference.Incarnation,ExpectedBarrierGeneration=barrier.Generation,ExpectedBarrierChecksum=barrier.BarrierChecksum,Identity=Identity("timeout")};var timeoutProcessor=new BaseSubjectRetirementTimeoutProcessor(new(){Request=timeoutRequest,Scope=new(){Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},RetirementPolicyChecksum=policy.PolicyChecksum,ObservedAtUtc=clock.GetUtcNow()});RecordMutationExecutionResult timeout=await fixture.Store.ExecuteAsync(timeoutProcessor,Execution(timeoutRequest.Identity,"timeout",clock));Assert.Equal(RecordMutationExecutionOutcome.Committed,timeout.Outcome);Assert.Equal(BaseSubjectRetirementBarrierState.Quarantined,timeoutProcessor.Result!.State);Assert.Equal(2,timeoutProcessor.Result.Generation);
        var overrideRequest=new BaseSubjectRetirementOverrideRequest{ContractId="example.user",ContractVersion=1,SubjectId=reference.SubjectId,AuthorityEpoch=reference.AuthorityEpoch,Incarnation=reference.Incarnation,ExpectedTombstoneSequence=barrier.TombstoneSequence,ExpectedBarrierGeneration=timeoutProcessor.Result.Generation,ExpectedBarrierChecksum=timeoutProcessor.Result.BarrierChecksum,Intent="override-subject-retirement-barrier",ChangeReference="CHG-42",Identity=Identity("override")};var overrideProcessor=new BaseSubjectRetirementOverrideProcessor(new(){Request=overrideRequest,Scope=new(){Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},RetirementPolicyChecksum=policy.PolicyChecksum,ObservedAtUtc=clock.GetUtcNow()});RecordMutationExecutionResult overridden=await fixture.Store.ExecuteAsync(overrideProcessor,Execution(overrideRequest.Identity,"override",clock));Assert.Equal(RecordMutationExecutionOutcome.Committed,overridden.Outcome);Assert.Equal(3,overrideProcessor.Result!.Generation);
        RecordEnvelope tombstoned=(await records.GetAsync(Private.Id,new("timeout-user"),principal,Operation(BaseOperationKind.Get,Private.Id))).Value!;var purgeRequest=new BaseSubjectFinalPurgeRequest{ContractId="example.user",ContractVersion=1,SubjectId=reference.SubjectId,AuthorityEpoch=reference.AuthorityEpoch,Incarnation=reference.Incarnation,ExpectedTombstoneSequence=barrier.TombstoneSequence,ExpectedPrivateRevision=tombstoned.Metadata.Revision!.Value,ExpectedBarrierGeneration=overrideProcessor.Result.Generation,ExpectedBarrierChecksum=overrideProcessor.Result.BarrierChecksum,Identity=Identity("purge")};var purgeProcessor=new BaseSubjectRetirementPurgeProcessor(new(){Request=purgeRequest,Scope=new(){Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},ContractChecksum=fixture.Registration.Checksum,RetirementPolicyChecksum=policy.PolicyChecksum,MinimumTombstoneAge=TimeSpan.Zero,ObservedAtUtc=clock.GetUtcNow(),Operation=Operation(BaseOperationKind.SubjectRetirementPurge,Private.Id)});RecordMutationExecutionResult purged=await fixture.Store.ExecuteAsync(purgeProcessor,Execution(purgeRequest.Identity,"purge",clock));Assert.Equal(RecordMutationExecutionOutcome.Committed,purged.Outcome);Assert.NotNull(purgeProcessor.Result);Assert.Equal(OperationStatus.NotFound,(await records.GetAsync(Private.Id,new("timeout-user"),principal,Operation(BaseOperationKind.Get,Private.Id))).Status);OperationResult<BaseSubjectRetirementInspection> terminal=await ((IBaseSubjectRetirementStore)fixture.Store).InspectAsync(new(){ContractId="example.user",ContractVersion=1,SubjectId=reference.SubjectId,AuthorityEpoch=reference.AuthorityEpoch,Incarnation=reference.Incarnation,ScopeAuthority=new(){Mode=BaseSubjectScopeQueryMode.ExactScope,ExactScope=new(){Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},InstalledAuthorityDigest=fixture.Registration.Checksum},IncludeTerminalSummary=true,MaximumResultBytes=65_536,DeadlineUtc=clock.GetUtcNow().AddMinutes(1)});Assert.NotNull(terminal.Value!.TerminalSummary);Assert.Null(terminal.Value.CurrentBarrier);
    }

    private static BaseMutationRequestIdentity Identity(string value){byte[] digest=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));return BaseMutationRequestIdentity.Create("l48-tests","subject-retirement",value,BaseMutationRequestFingerprint.Create(digest));}
    private static RecordMutationExecutionRequest Execution(BaseMutationRequestIdentity identity,string value,TimeProvider clock)=>new(){AcquisitionTimeout=TimeSpan.FromSeconds(2),TransactionTimeout=TimeSpan.FromSeconds(2),CommitCompletionTimeout=TimeSpan.FromSeconds(2),AtomicRequest=new(){Identity=identity,StructuralDigest=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)),ExpiresAt=clock.GetUtcNow().AddDays(1),MaxReceiptBytes=65_536}};

    [Fact]
    public void L47_consumer_graph_checksum_is_normalized_deterministic_and_duplicate_identity_fails()
    {
        SubjectFixture fixture = Build();
        BaseGeneratedSubjectRegistration subject = fixture.Services.GetRequiredService<BaseSubjectContractRegistry>().All.Single();
        BaseSubjectLifecycleConsumerDefinition left = LifecycleConsumer() with
        {
            ObservedStates = [BaseSubjectLifecycleState.Retired, BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Retired],
        };
        BaseSubjectLifecycleConsumerDefinition right = LifecycleConsumer() with
        {
            ObservedStates = [BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Retired],
        };

        BaseSubjectLifecycleConsumerDefinition normalizedLeft = BaseSubjectLifecycleRegistry.Normalize(left);
        BaseSubjectLifecycleConsumerDefinition normalizedRight = BaseSubjectLifecycleRegistry.Normalize(right);
        string leftChecksum = BaseSubjectLifecycleRegistry.Checksum(normalizedLeft, subject.Checksum);
        string rightChecksum = BaseSubjectLifecycleRegistry.Checksum(normalizedRight, subject.Checksum);

        Assert.Equal([BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Retired], normalizedLeft.ObservedStates.ToArray());
        Assert.Equal(leftChecksum, rightChecksum);
        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() =>
            new BaseSubjectLifecycleRegistry([left, right], new BaseSubjectContractRegistry([subject])));
        Assert.Equal(BaseSubjectErrorCodes.LifecycleRegistrationConflict, duplicate.Message);
    }

    [Fact]
    public async Task L47_InMemory_sequence_overflow_fails_atomically_with_the_stable_contract_error()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        OperationResult<RecordEnvelope> created = await runtime.CreateAsync(
            Private.Id, Create("overflow-user", ("active", true), ("tenant", "tenant-a")), principal,
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(created.IsSuccess(), created.Error?.Code);

        InMemoryStoreState state = fixture.Store.CaptureVectorRoot();
        KeyValuePair<string, InMemorySubjectLifetimeState> lifetime = Assert.Single(state.SubjectLifetimes);
        state.SubjectLifetimes[lifetime.Key] = lifetime.Value with { SubjectSequence = long.MaxValue };
        int factCount = state.SubjectLifecycleFacts.Count;

        OperationResult<RecordEnvelope> result = await runtime.PatchAsync(
            Private.Id, new RecordId("overflow-user"), Patch(("active", false)), principal,
            Operation(BaseOperationKind.Patch, Private.Id));

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal(BaseSubjectErrorCodes.SequenceExhausted, result.Error?.Code);
        Assert.Equal(factCount, fixture.Store.CaptureVectorRoot().SubjectLifecycleFacts.Count);
        RecordEnvelope current = (await runtime.GetAsync(
            Private.Id, new RecordId("overflow-user"), principal,
            Operation(BaseOperationKind.Get, Private.Id))).Value!;
        Assert.True(current.Payload.Fields!["active"].GetBoolean());
    }

    private sealed class UserSubject;

    [Fact]
    public async Task L47_ControlPlane_all_scope_inspection_uses_graph_installed_authority_receipt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey { Id = 9, Key = Enumerable.Repeat((byte)9, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch })
            .AddTestPolicyAuthority(new GrantingPolicy())
            .AddTestSubjectLifecycleGrant("example.user.admin", "hpd.base.application", "example.auth", "example.user.admin", "example.sqlite-user", 1, audience: HPDBaseEndpointAudience.ControlPlane)
            .AddCollection(L45SqlitePrivateUser.Collection)
            .AddExportedSubject(L45SqliteUserSubject.HPDBaseSubjectRegistration));
        await using ServiceProvider provider = services.BuildServiceProvider();
        OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        Assert.True(readiness.IsSuccess(), readiness.Error?.Code);
        string storeId = provider.GetRequiredService<IRecordStoreRegistry>().GetRegistrations().Single().Store.Capabilities.StoreId;
        BaseResult<BaseSubjectLifecycleInspectionResult> result = await provider.GetRequiredService<IHPDBaseAdministration>().InspectSubjectLifecycleAsync(
            storeId,
            Principal(),
            new BaseSubjectLifecycleInspectionRequest
            {
                ContractId = "example.sqlite-user", ContractVersion = 1,
                ScopeMode = BaseSubjectScopeQueryMode.AllAuthorizedScopes,
                IncludeTerminalReceipt = false, MaximumResultBytes = 4096, Timeout = TimeSpan.FromSeconds(2),
            });
        Assert.True(result is BaseSuccess<BaseSubjectLifecycleInspectionResult>, result is BaseFailure<BaseSubjectLifecycleInspectionResult> failure ? $"{failure.Status}: {failure.Error.Code}" : null);
    }

    [Fact]
    public async Task Capture_preserves_normal_duplicate_and_missing_record_outcomes_with_subject_contracts_installed()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        RecordCreateRequest create = Create("user-1", ("active", true), ("tenant", "tenant-a"));
        OperationResult<RecordEnvelope> initial = await runtime.CreateAsync(
            Private.Id,
            create,
            principal,
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(initial.IsSuccess(), initial.Error is null ? null : $"{initial.Error.Code}: {initial.Error.Message}");

        OperationResult<RecordEnvelope> duplicate = await runtime.CreateAsync(
            Private.Id,
            create,
            principal,
            Operation(BaseOperationKind.Create, Private.Id));
        OperationResult<RecordEnvelope> missingPatch = await runtime.PatchAsync(
            Private.Id,
            new RecordId("missing"),
            Patch(("active", false)),
            principal,
            Operation(BaseOperationKind.Patch, Private.Id));
        OperationResult<DeleteResult> missingDelete = await runtime.DeleteAsync(
            Private.Id,
            new RecordId("missing"),
            new RecordDeleteRequest(),
            principal,
            Operation(BaseOperationKind.Delete, Private.Id));

        Assert.Equal(OperationStatus.Conflict, duplicate.Status);
        Assert.NotEqual(BaseSubjectErrorCodes.ProviderContractInvalid, duplicate.Error?.Code);
        Assert.Equal(OperationStatus.NotFound, missingPatch.Status);
        Assert.Equal(OperationStatus.NotFound, missingDelete.Status);
    }

    [Fact]
    public async Task InMemory_validates_current_lifetime_and_rejects_stale_or_inactive_references()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();

        OperationResult<RecordEnvelope> privateCreate = await runtime.CreateAsync(
            Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal, Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(privateCreate.IsSuccess(), privateCreate.Error?.Code);
        JsonElement firstReference = await fixture.AcquireAsync("user-1");

        OperationResult<RecordEnvelope> accepted = await runtime.CreateAsync(
            Consumer.Id, Create("profile-1", ("owner", firstReference)), principal, Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.True(accepted.IsSuccess(), accepted.Error?.Code);

        OperationResult<RecordEnvelope> wrongTenant = await runtime.CreateAsync(
            Consumer.Id,
            Create("profile-wrong-tenant", ("owner", firstReference)),
            principal with { CurrentTenantId = "tenant-b" },
            Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, wrongTenant.Error?.Code);
        Assert.Equal(OperationStatus.ValidationFailed, wrongTenant.Status);

        OperationResult<RecordEnvelope> deactivated = await runtime.PatchAsync(
            Private.Id, new RecordId("user-1"), Patch(("active", false)), principal, Operation(BaseOperationKind.Patch, Private.Id));
        Assert.True(deactivated.IsSuccess(), deactivated.Error?.Code);
        OperationResult<RecordEnvelope> inactive = await runtime.CreateAsync(
            Consumer.Id, Create("profile-2", ("owner", firstReference)), principal, Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(OperationStatus.ValidationFailed, inactive.Status);
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, inactive.Error?.Code);

        BaseSubjectReference<UserSubject> typedFirstReference = new(
            BaseSubjectId.Create(firstReference.GetProperty("subjectId").GetString()!, BaseSubjectIdKind.OrdinalString),
            BaseSubjectAuthorityEpoch.Parse(firstReference.GetProperty("authorityEpoch").GetString()!),
            BaseSubjectIncarnation.Parse(firstReference.GetProperty("incarnation").GetString()!));
        BaseSession lifecycleSession = fixture.Services.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseExportedSubjectContract<UserSubject> exporter = lifecycleSession.GetExportedSubjectContract<UserSubject>(fixture.Registration);
        RecordEnvelope beforeTombstone = (await runtime.GetAsync(Private.Id, new RecordId("user-1"), principal, Operation(BaseOperationKind.Get, Private.Id))).Value!;
        BaseSubjectLifecycleFact<UserSubject> tombstone = (await exporter.TombstoneAsync(new()
        {
            Subject = typedFirstReference, ExpectedPrivateRevision = beforeTombstone.Metadata.Revision!.Value,
            Identity = BaseMutationRequestIdentity.Create("l47-tests", "tombstone", "stale-user-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("stale-tombstone-user-1"u8))),
        })).RequireValue();
        RecordEnvelope beforeRetirement = (await runtime.GetAsync(Private.Id, new RecordId("user-1"), principal, Operation(BaseOperationKind.Get, Private.Id))).Value!;
        BaseResult<BaseSubjectFinalRetirementResult<UserSubject>> retirement = await exporter.FinalizeRetirementAsync(new()
        {
            Subject = typedFirstReference, ExpectedTombstoneSequence = tombstone.Fact.SubjectSequence,
            ExpectedPrivateRevision = beforeRetirement.Metadata.Revision!.Value,
            Identity = BaseMutationRequestIdentity.Create("l47-tests", "retire", "stale-user-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("stale-retire-user-1"u8))),
        });
        Assert.True(retirement is BaseSuccess<BaseSubjectFinalRetirementResult<UserSubject>>, (retirement as BaseFailure<BaseSubjectFinalRetirementResult<UserSubject>>)?.Error.Code);
        OperationResult<RecordEnvelope> created = await runtime.CreateAsync(
            Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(created.IsSuccess(), created.Error?.Code);
        JsonElement secondReference = await fixture.AcquireAsync("user-1");
        Assert.NotEqual(firstReference.GetProperty("incarnation").GetString(), secondReference.GetProperty("incarnation").GetString());

        OperationResult<RecordEnvelope> stale = await runtime.CreateAsync(
            Consumer.Id, Create("profile-3", ("owner", firstReference)), principal, Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, stale.Error?.Code);
        Assert.True((await runtime.CreateAsync(Consumer.Id, Create("profile-4", ("owner", secondReference)), principal,
            Operation(BaseOperationKind.Create, Consumer.Id))).IsSuccess());
    }

    [Theory]
    [InlineData("deactivate", false)]
    [InlineData("deactivate", true)]
    [InlineData("rescope", false)]
    [InlineData("rescope", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task Mixed_atomic_batch_validates_against_final_subject_state_and_rolls_back_every_write(
        string lifecycle,
        bool lifecycleFirst)
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        OperationResult<RecordEnvelope> created = await runtime.CreateAsync(
            Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(created.IsSuccess(), created.Error?.Code);
        JsonElement reference = await fixture.AcquireAsync("user-1");
        BaseSubjectLifecycleOrderingBoundary? lifecycleHighWaterBefore = (await fixture.Store.InspectAsync(new()
        {
            ContractId = "example.user", ContractVersion = 1,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.ExactScope,
                ExactScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                InstalledAuthorityDigest = fixture.Registration.Checksum,
            },
            IncludeTerminalReceipt = false, MaximumResultBytes = 4096,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        })).Value!.HighWater;

        var consumer = new BaseRecordBatchItem
        {
            ItemId = "consumer",
            CollectionId = Consumer.Id,
            Kind = BaseRecordMutationKind.Create,
            Create = Create("profile", ("owner", reference)),
        };
        BaseRecordBatchItem subject = lifecycle switch
        {
            "deactivate" => new BaseRecordBatchItem
            {
                ItemId = "deactivate", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Patch,
                RecordId = new RecordId("user-1"), Patch = Patch(("active", false)),
            },
            "rescope" => new BaseRecordBatchItem
            {
                ItemId = "rescope", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Patch,
                RecordId = new RecordId("user-1"), Patch = Patch(("tenant", "tenant-b")),
            },
            "delete" => new BaseRecordBatchItem
            {
                ItemId = "tombstone", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Patch,
                RecordId = new RecordId("user-1"), Patch = Patch(("active", false), ("tombstoned", true)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(lifecycle)),
        };
        OperationResult<BaseRecordBatchResult> result = await runtime.BatchAsync(new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations = lifecycleFirst ? [subject, consumer] : [consumer, subject],
        }, principal, Operation(BaseOperationKind.Batch, Consumer.Id));

        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value?.Outcome);
        Assert.Contains(result.Value!.Items, item => item.Error?.Code == (lifecycle == "delete"
            ? BaseSubjectErrorCodes.LifecycleUnauthorized
            : BaseSubjectErrorCodes.ReferenceInvalid));
        Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(Consumer.Id, new RecordId("profile"), principal, Operation(BaseOperationKind.Get, Consumer.Id))).Status);
        RecordEnvelope current = (await runtime.GetAsync(Private.Id, new RecordId("user-1"), principal, Operation(BaseOperationKind.Get, Private.Id))).Value!;
        Assert.True(current.Payload.Fields!["active"].GetBoolean());
        Assert.Equal("tenant-a", current.Payload.Fields["tenant"].GetString());
        BaseSubjectLifecycleOrderingBoundary? lifecycleHighWaterAfter = (await fixture.Store.InspectAsync(new()
        {
            ContractId = "example.user", ContractVersion = 1,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.ExactScope,
                ExactScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                InstalledAuthorityDigest = fixture.Registration.Checksum,
            },
            IncludeTerminalReceipt = false, MaximumResultBytes = 4096,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        })).Value!.HighWater;
        Assert.Equal(lifecycleHighWaterBefore, lifecycleHighWaterAfter);
    }

    [Fact]
    public async Task L47_InMemory_publishes_consumer_indexed_lifecycle_facts_in_canonical_order()
    {
        bool interruptMaintenance = true;
        int completedMaintenancePages = 0;
        await using SubjectFixture fixture = Build(lifecycleConsumer: true, lifecycleMaintenancePageCompleted: (page, _) =>
        {
            completedMaintenancePages = page;
            if (interruptMaintenance) { interruptMaintenance = false; throw new OperationCanceledException(); }
            return ValueTask.CompletedTask;
        });
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        OperationResult<RecordEnvelope> created = await runtime.CreateAsync(
            Private.Id, Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(created.IsSuccess(), created.Error?.Code);
        Assert.True((await runtime.PatchAsync(Private.Id, new RecordId("user-1"), Patch(("active", false)), principal,
            Operation(BaseOperationKind.Patch, Private.Id))).IsSuccess());
        JsonElement encodedReference = await fixture.AcquireAsync("user-1");
        BaseSubjectReference<UserSubject> subjectReference = new(
            BaseSubjectId.Create(encodedReference.GetProperty("subjectId").GetString()!, BaseSubjectIdKind.OrdinalString),
            BaseSubjectAuthorityEpoch.Parse(encodedReference.GetProperty("authorityEpoch").GetString()!),
            BaseSubjectIncarnation.Parse(encodedReference.GetProperty("incarnation").GetString()!));
        RecordEnvelope beforeTombstone = (await runtime.GetAsync(Private.Id, new RecordId("user-1"), principal,
            Operation(BaseOperationKind.Get, Private.Id))).Value!;
        BaseSession session = fixture.Services.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseExportedSubjectContract<UserSubject> exporter = session.GetExportedSubjectContract<UserSubject>(fixture.Registration);
        BaseResult<BaseSubjectLifecycleFact<UserSubject>> tombstoned = await exporter.TombstoneAsync(new()
        {
            Subject = subjectReference,
            ExpectedPrivateRevision = beforeTombstone.Metadata.Revision!.Value,
            Identity = BaseMutationRequestIdentity.Create("l47-tests", "tombstone", "user-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("tombstone-user-1"u8))),
        });
        Assert.True(tombstoned is BaseSuccess<BaseSubjectLifecycleFact<UserSubject>>, (tombstoned as BaseFailure<BaseSubjectLifecycleFact<UserSubject>>)?.Error.Code);
        Assert.Equal(BaseSubjectLifecycleState.Tombstoned, tombstoned.RequireValue().Fact.Transitioned!.CurrentState);

        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleConsumer();
        OperationResult<BaseSubjectLifecycleProviderPage> page = await fixture.Store.ReadAsync(new BaseSubjectLifecycleProviderReadRequest
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = BaseSubjectContractGraph.Checksum(SubjectDefinition()), ConsumerId = consumer.Id,
            ConsumerVersion = consumer.Version, ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum),
            ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
            Take = 256, MaximumResultBytes = 1_048_576, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        Assert.True(page.IsSuccess(), page.Error?.Code);
        Assert.Equal([BaseSubjectLifecycleFactKind.Created, BaseSubjectLifecycleFactKind.Transitioned, BaseSubjectLifecycleFactKind.Transitioned], page.Value!.Facts.Select(static fact => fact.Fact.Kind));
        Assert.Equal([1L, 2L, 3L], page.Value.Facts.Select(static fact => fact.Fact.SubjectSequence));
        Assert.All(page.Value.Facts, fact => Assert.Equal(fact.Fact.AuthorityEpoch, fact.Boundary.AuthorityEpoch));
        BaseSubjectLifecycleProviderReadRequest oneFactRequest = new()
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = fixture.Registration.Checksum, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version,
            ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum),
            ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
            Take = 1, MaximumResultBytes = 1_048_576, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        };
        OperationResult<BaseSubjectLifecycleProviderPage> measuredPage = await fixture.Store.ReadAsync(oneFactRequest);
        Assert.True(measuredPage.IsSuccess(), measuredPage.Error?.Code);
        long exactResultBytes = measuredPage.Value!.Accounting.ResultBytes;
        Assert.True((await fixture.Store.ReadAsync(oneFactRequest with { MaximumResultBytes = exactResultBytes })).IsSuccess());
        OperationResult<BaseSubjectLifecycleProviderPage> oneByteShort = await fixture.Store.ReadAsync(oneFactRequest with { MaximumResultBytes = exactResultBytes - 1 });
        Assert.Equal(BaseSubjectErrorCodes.LifecycleCapacityExceeded, oneByteShort.Error?.Code);
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("l47-tests", "advance", "one", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("advance-one"u8)));
        var checkpointRequest = new BaseSubjectLifecycleProviderCheckpointRequest
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1, ConsumerId = consumer.Id,
            ConsumerVersion = consumer.Version, ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum), ProjectionGeneration = 1,
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" }, Through = page.Value.Through,
            ExpectedCheckpointGeneration = 0, Identity = identity, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        };
        var checkpointProcessor = new BaseSubjectLifecycleCheckpointProcessor(checkpointRequest);
        RecordMutationExecutionResult advanced = await fixture.Store.AdvanceCheckpointAsync(checkpointProcessor, LifecycleCheckpointExecution(checkpointRequest));
        Assert.True(advanced.Outcome == RecordMutationExecutionOutcome.Committed, advanced.Error?.Code ?? advanced.Processing?.Error?.Code); Assert.Equal(1, checkpointProcessor.Result!.CheckpointGeneration); Assert.False(checkpointProcessor.Result.Duplicate);
        var duplicateProcessor = new BaseSubjectLifecycleCheckpointProcessor(checkpointRequest);
        RecordMutationExecutionResult duplicate = await fixture.Store.AdvanceCheckpointAsync(duplicateProcessor, LifecycleCheckpointExecution(checkpointRequest));
        Assert.True(duplicate.Outcome == RecordMutationExecutionOutcome.Committed, duplicate.Error?.Code ?? duplicate.Processing?.Error?.Code); Assert.True(duplicateProcessor.Result!.Duplicate); Assert.Equal(1, duplicateProcessor.Result.CheckpointGeneration);

        BaseSubjectLifecycleOrderingBoundary retained = page.Value.Facts[^1].Boundary;
        BaseMutationRequestIdentity pruneIdentity = BaseMutationRequestIdentity.Create(
            "l47-tests", "prune", "tenant-a", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("prune-tenant-a"u8)));
        var pruneRequest = new BaseSubjectAuthorityMaintenanceExecutionRequest
        {
            Lifecycle=new(){Kind=BaseSubjectLifecycleMaintenanceKind.Prune,ContractId="example.user",ContractVersion=1,RetainedFrom=retained,ExpectedDeliveryEpoch=1,PlanChecksum=System.Security.Cryptography.SHA256.HashData("prune-plan"u8)},
            Identity = pruneIdentity,
            CombinedPlanChecksum = new byte[32],
            ExpectedStoreGeneration = 1,
            ExpectedSchemaGeneration = 1,
            ExpectedRestoreEpoch = 0,
            ExpectedScopeProtectionGeneration = 1,
            ExpectedScopeProtectionKeyId = "0",
            PageSize = 1,
            OperationTimeout = TimeSpan.FromSeconds(5),
            CommitCompletionTimeout = TimeSpan.FromSeconds(5),
        };
        pruneRequest = pruneRequest with
        {
            CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(pruneRequest),
        };
        var pruneProcessor = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult interrupted = await fixture.Store.ExecuteMaintenanceAsync(pruneProcessor, pruneRequest);
        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, interrupted.Outcome);
        Assert.Equal(BaseSubjectErrorCodes.Timeout, interrupted.Error?.Code ?? interrupted.Processing?.Error?.Code);
        Assert.Equal(1, completedMaintenancePages);
        OperationResult<RecordEnvelope> closed = await fixture.Store.GetAsync(Private, new RecordId("user-1"), Operation(BaseOperationKind.Get, Private.Id));
        Assert.Equal(OperationStatus.CapabilityUnavailable, closed.Status);
        Assert.Equal(BaseSubjectErrorCodes.MaintenanceRequired, closed.Error?.Code);

        pruneProcessor = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult pruned = await fixture.Store.ExecuteMaintenanceAsync(pruneProcessor, pruneRequest);

        Assert.True(pruned.Outcome == RecordMutationExecutionOutcome.Committed,
            pruned.Error?.Code ?? pruned.Processing?.Error?.Code);
        Assert.Equal(BaseSubjectLifecycleMaintenanceKind.Prune, pruneProcessor.LifecycleResult!.Kind);
        Assert.Equal(7, pruneProcessor.LifecycleResult.ExaminedCount);
        Assert.Equal(4, pruneProcessor.LifecycleResult.ChangedCount);
    }

    [Fact]
    public async Task L47_reconciliation_fails_before_subject_enumeration_when_no_safe_plan_is_installed()
    {
        await using SubjectFixture fixture = Build(lifecycleConsumer: true);
        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleConsumer();
        OperationResult<BaseSubjectLifecycleProviderReconciliationPage> result = await fixture.Store.ReconcileAsync(new()
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = fixture.Registration.Checksum, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version,
            ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum),
            ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
            Take = 1, MaximumResultBytes = 4096, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });

        Assert.False(result.IsSuccess());
        Assert.Equal(OperationStatus.CapabilityUnavailable, result.Status);
        Assert.Equal(BaseSubjectErrorCodes.LifecycleReconciliationUnavailable, result.Error?.Code);
    }

    [Fact]
    public async Task L47_supported_reconciliation_executes_through_the_installed_typed_handle()
    {
        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleConsumer() with { ReconciliationGrantId = "example.profile.lifecycle.reconcile" };
        await using SubjectFixture fixture = Build(lifecycleConsumers: [consumer]);
        BaseInstalledSubjectLifecycleConsumer installed = fixture.Services.GetRequiredService<BaseSubjectLifecycleRegistry>().All.Single();
        var reconciling = new ReconcilingLifecycleStore(fixture.Store, fixture.Services.GetRequiredService<BaseOpaqueTokenProtector>());
        var runtime = new DefaultBaseSubjectLifecycleRuntime(new SingleStoreRegistry(Private.Id, reconciling),
            fixture.Services.GetRequiredService<BaseSubjectContractRegistry>(), fixture.Services.GetRequiredService<IBasePolicyOrchestrator>(),
            fixture.Services.GetRequiredService<BaseOpaqueTokenProtector>(), TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new HPDBaseSubjectLifecycleOptions { CursorLifetime = TimeSpan.FromHours(1) }),
            new BaseSubjectLifecycleRuntimeLimits(2, TimeSpan.FromSeconds(1)), new BaseSubjectLifecycleOperationalState());
        await using (runtime.ConfigureAwait(false))
        {
            BaseGeneratedSubjectLifecycleConsumerIdentity<UserSubject> identity = BaseGeneratedSubjectLifecycleConsumers.Register<UserSubject>(consumer, fixture.Registration);
            BaseResult<BaseSubjectLifecycleReconciliationPage<UserSubject>> result = await runtime.ReconcileAsync(
                fixture.Services.GetRequiredService<IBaseSessionFactory>().For(Principal()), identity, installed, null, 1, CancellationToken.None);
            Assert.True(result is BaseSuccess<BaseSubjectLifecycleReconciliationPage<UserSubject>>, (result as BaseFailure<BaseSubjectLifecycleReconciliationPage<UserSubject>>)?.Error.Code);
            Assert.Equal("reconciled-user", Assert.Single(result.RequireValue().Subjects).SubjectId.Value);
        }
    }

    [Fact]
    public async Task L47_virtual_checkpoint_overtake_requires_retention_distance_and_elapsed_consumer_lag()
    {
        var clock = new LifecycleTimeProvider(DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        await using SubjectFixture fixture = Build(lifecycleConsumer: true, timeProvider: clock);
        IBaseRecordRuntime records = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await records.CreateAsync(Private.Id, Create("overtake-user", ("active", true), ("tenant", "tenant-a")),
            Principal(), Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleConsumer();
        OperationResult<BaseSubjectLifecycleProviderPage> page = await fixture.Store.ReadAsync(LifecycleReadRequest(fixture, consumer));
        Assert.True(page.IsSuccess(), page.Error?.Code);
        BaseSubjectLifecycleOrderingBoundary retainedFrom = Assert.Single(page.Value!.Facts).Boundary;
        BaseSubjectAuthorityMaintenanceExecutionRequest request = OvertakeRequest(fixture, consumer, retainedFrom);

        var premature = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult rejected = await fixture.Store.ExecuteMaintenanceAsync(premature, request);
        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, rejected.Outcome);
        Assert.Equal(BaseSubjectErrorCodes.LifecycleRegistrationConflict, rejected.Error?.Code ?? rejected.Processing?.Error?.Code);

        clock.Advance(TimeSpan.FromDays(1));
        var eligible = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult overtaken = await fixture.Store.ExecuteMaintenanceAsync(eligible, request);
        Assert.Equal(RecordMutationExecutionOutcome.Committed, overtaken.Outcome);
        Assert.Equal(1, eligible.LifecycleResult!.ChangedCount);
        OperationResult<BaseSubjectLifecycleProviderPage> closed = await fixture.Store.ReadAsync(LifecycleReadRequest(fixture, consumer));
        Assert.Equal(BaseSubjectErrorCodes.CursorOvertaken, closed.Error?.Code);
    }

    [Fact]
    public async Task L47_inmemory_seeks_the_protected_scope_index_before_fact_hydration()
    {
        await using SubjectFixture fixture=Build(lifecycleConsumer:true);IBaseRecordRuntime records=fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await records.CreateAsync(Private.Id,Create("tenant-a-user",("active",true),("tenant","tenant-a")),Principal(),Operation(BaseOperationKind.Create,Private.Id))).IsSuccess());
        PrincipalContext tenantB=Principal() with{CurrentTenantId="tenant-b"};OperationContext operationB=Operation(BaseOperationKind.Create,Private.Id) with{TenantId="tenant-b"};
        Assert.True((await records.CreateAsync(Private.Id,Create("tenant-b-user",("active",true),("tenant","tenant-b")),tenantB,operationB)).IsSuccess());
        OperationResult<BaseSubjectLifecycleProviderPage> page=await fixture.Store.ReadAsync(LifecycleReadRequest(fixture,LifecycleConsumer()));
        Assert.True(page.IsSuccess(),page.Error?.Code);Assert.Equal("tenant-a-user",Assert.Single(page.Value!.Facts).Fact.SubjectId.Value);Assert.Equal(1,page.Value.Accounting.RowsSought);Assert.Equal(1,page.Value.Accounting.RowsHydrated);
    }

    [Fact]
    public async Task L47_noncooperative_provider_read_times_out_retains_one_slot_and_recovers_after_late_completion()
    {
        await using SubjectFixture fixture = Build(lifecycleConsumer: true, scopeRotationKeys: true,
            lifecycleReadTimeout: TimeSpan.FromMilliseconds(100));
        IBaseRecordRuntime records = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await records.CreateAsync(Private.Id, Create("slow-user", ("active", true), ("tenant", "tenant-a")),
            Principal(), Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        BaseSubjectContractRegistry contracts = fixture.Services.GetRequiredService<BaseSubjectContractRegistry>();
        BaseInstalledSubjectLifecycleConsumer installed = fixture.Services.GetRequiredService<BaseSubjectLifecycleRegistry>().All.Single();
        var hanging = new HangingLifecycleStore(fixture.Store);
        var operational = new BaseSubjectLifecycleOperationalState();
        var runtime = new DefaultBaseSubjectLifecycleRuntime(
            new SingleStoreRegistry(Private.Id, hanging), contracts,
            fixture.Services.GetRequiredService<IBasePolicyOrchestrator>(),
            fixture.Services.GetRequiredService<BaseOpaqueTokenProtector>(), TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new HPDBaseSubjectLifecycleOptions { CursorLifetime = TimeSpan.FromHours(1) }),
            new BaseSubjectLifecycleRuntimeLimits(1, TimeSpan.FromMilliseconds(100)), operational);
        await using (runtime.ConfigureAwait(false))
        {
            BaseSession session = fixture.Services.GetRequiredService<IBaseSessionFactory>().For(Principal());
            BaseResult<BaseUntypedSubjectLifecyclePage> timedOut = await runtime.ReadUntypedAsync(session, installed, null, 1, CancellationToken.None);
            Assert.Equal(BaseSubjectErrorCodes.Timeout, Assert.IsType<BaseFailure<BaseUntypedSubjectLifecyclePage>>(timedOut).Error.Code);
            Assert.Equal(1, hanging.ReadCalls);
            Assert.Equal(1, operational.Quarantined);
            HealthDescriptor health = Assert.Single(await new BaseSubjectLifecycleHealthContributor(operational, TimeProvider.System).GetHealthAsync());
            Assert.Equal(HealthStatus.Degraded, health.Status);

            BaseResult<BaseUntypedSubjectLifecyclePage> capacityTimedOut = await runtime.ReadUntypedAsync(session, installed, null, 1, CancellationToken.None);
            Assert.Equal(BaseSubjectErrorCodes.Timeout, Assert.IsType<BaseFailure<BaseUntypedSubjectLifecyclePage>>(capacityTimedOut).Error.Code);
            Assert.Equal(1, hanging.ReadCalls);

            await hanging.ReleaseAsync();
            Assert.True(SpinWait.SpinUntil(() => operational.Quarantined == 0, TimeSpan.FromSeconds(1)));
            BaseResult<BaseUntypedSubjectLifecyclePage> recovered = await runtime.ReadUntypedAsync(session, installed, null, 1, CancellationToken.None);
            Assert.True(recovered is BaseSuccess<BaseUntypedSubjectLifecyclePage>,
                (recovered as BaseFailure<BaseUntypedSubjectLifecyclePage>)?.Error.Code);
            Assert.Equal(2, hanging.ReadCalls);
        }
    }

    [Fact]
    public async Task L47_runtime_rejects_hostile_page_identity_scope_interval_and_accounting_evidence()
    {
        await using SubjectFixture fixture = Build(lifecycleConsumer: true, scopeRotationKeys: true);
        IBaseRecordRuntime records = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await records.CreateAsync(Private.Id, Create("hostile-user", ("active", true), ("tenant", "tenant-a")),
            Principal(), Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        BaseInstalledSubjectLifecycleConsumer installed = fixture.Services.GetRequiredService<BaseSubjectLifecycleRegistry>().All.Single();
        BaseSubjectContractRegistry contracts = fixture.Services.GetRequiredService<BaseSubjectContractRegistry>();
        BaseSession session = fixture.Services.GetRequiredService<IBaseSessionFactory>().For(Principal());

        await Reject(page => page with
        {
            Facts = [page.Facts[0] with
            {
                Boundary = page.Facts[0].Boundary with { AuthorityEpoch = new BaseSubjectAuthorityEpoch(new byte[16]) },
            }],
        });
        await Reject(page => page with { Facts = [page.Facts[0], page.Facts[0]] });
        await Reject(page => page with { Intervals = [] });
        await Reject(page => page with { Accounting = page.Accounting with { ResultBytes = checked(page.Accounting.ResultBytes + 1) } });
        await Reject(page => page with
        {
            Scope = page.Scope with { IndexDigest = System.Security.Cryptography.SHA256.HashData("substituted-scope"u8) },
        });

        async Task Reject(Func<BaseSubjectLifecycleProviderPage, BaseSubjectLifecycleProviderPage> transform)
        {
            await using var runtime = new DefaultBaseSubjectLifecycleRuntime(
                new SingleStoreRegistry(Private.Id, new TransformingLifecycleStore(fixture.Store, transform)), contracts,
                fixture.Services.GetRequiredService<IBasePolicyOrchestrator>(),
                fixture.Services.GetRequiredService<BaseOpaqueTokenProtector>(), TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new HPDBaseSubjectLifecycleOptions()),
                BaseSubjectLifecycleRuntimeLimits.Default,
                new BaseSubjectLifecycleOperationalState());
            BaseResult<BaseUntypedSubjectLifecyclePage> result = await runtime.ReadUntypedAsync(session, installed, null, 1, CancellationToken.None);
            BaseFailure<BaseUntypedSubjectLifecyclePage> failure = Assert.IsType<BaseFailure<BaseUntypedSubjectLifecyclePage>>(result);
            Assert.Equal(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, failure.Error.Code);
        }
    }

    [Fact]
    public async Task L47_canonical_facts_are_deduplicated_while_consumers_receive_distinct_observed_states()
    {
        BaseSubjectLifecycleConsumerDefinition active = LifecycleConsumer() with
        {
            Id = "example.active.lifecycle", ObservedStates = [BaseSubjectLifecycleState.Active],
            DeliveryGrantId = "example.active.lifecycle.read",
        };
        BaseSubjectLifecycleConsumerDefinition inactive = LifecycleConsumer() with
        {
            Id = "example.inactive.lifecycle", ObservedStates = [BaseSubjectLifecycleState.Inactive],
            DeliveryGrantId = "example.inactive.lifecycle.read",
        };
        await using SubjectFixture fixture = Build(lifecycleConsumers: [active, inactive]);
        IBaseRecordRuntime records = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await records.CreateAsync(Private.Id, Create("split-user", ("active", true), ("tenant", "tenant-a")),
            Principal(), Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        Assert.True((await records.PatchAsync(Private.Id, new RecordId("split-user"), Patch(("active", false)),
            Principal(), Operation(BaseOperationKind.Patch, Private.Id))).IsSuccess());

        OperationResult<BaseSubjectLifecycleProviderPage> activePage = await Read(active);
        OperationResult<BaseSubjectLifecycleProviderPage> inactivePage = await Read(inactive);
        Assert.Equal(BaseSubjectLifecycleState.Active, Assert.Single(activePage.Value!.Facts).MatchedObservedState);
        Assert.Equal(BaseSubjectLifecycleState.Inactive, Assert.Single(inactivePage.Value!.Facts).MatchedObservedState);
        Assert.NotEqual(activePage.Value.Facts[0].Boundary, inactivePage.Value.Facts[0].Boundary);
        OperationResult<BaseSubjectLifecycleProviderInspection> inspection = await fixture.Store.InspectAsync(new()
        {
            ContractId = "example.user", ContractVersion = 1,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.ExactScope,
                ExactScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                InstalledAuthorityDigest = fixture.Registration.Checksum,
            },
            IncludeTerminalReceipt = false, MaximumResultBytes = 4096, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        Assert.Equal(2, inspection.Value!.Consumers.Length);

        ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> Read(BaseSubjectLifecycleConsumerDefinition consumer) => fixture.Store.ReadAsync(new()
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = fixture.Registration.Checksum, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version,
            ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum),
            ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
            Take = 16, MaximumResultBytes = 1_048_576, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
    }

    [Fact]
    public async Task L47_InMemory_rebuild_and_consumer_removal_use_bounded_identified_staging()
    {
        await using SubjectFixture fixture = Build(lifecycleConsumer: true);
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        Assert.True((await runtime.CreateAsync(Private.Id, Create("staged-user", ("active", true), ("tenant", "tenant-a")), Principal(), Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleConsumer();

        BaseSubjectAuthorityMaintenanceExecutionRequest rebuild = Maintenance(BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection, "rebuild", projection: 1);
        var rebuildProcessor = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult rebuilt = await fixture.Store.ExecuteMaintenanceAsync(rebuildProcessor, rebuild);
        Assert.Equal(RecordMutationExecutionOutcome.Committed, rebuilt.Outcome);
        Assert.Equal(2, rebuildProcessor.LifecycleResult!.ProjectionGeneration);

        BaseSubjectAuthorityMaintenanceExecutionRequest removal = Maintenance(BaseSubjectLifecycleMaintenanceKind.RemoveConsumer, "remove", projection: 2);
        var removalProcessor = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult removed = await fixture.Store.ExecuteMaintenanceAsync(removalProcessor, removal);
        Assert.Equal(RecordMutationExecutionOutcome.Committed, removed.Outcome);
        Assert.Null(removalProcessor.LifecycleResult!.ProjectionGeneration);
        OperationResult<BaseSubjectLifecycleProviderPage> denied = await fixture.Store.ReadAsync(new()
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1, ContractChecksum = fixture.Registration.Checksum,
            ConsumerId = consumer.Id, ConsumerVersion = consumer.Version, ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum), ProjectionGeneration = 2,
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" }, Take = 1, MaximumResultBytes = 4096, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        Assert.False(denied.IsSuccess());
        Assert.Equal(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, denied.Error?.Code);

        BaseSubjectAuthorityMaintenanceExecutionRequest Maintenance(BaseSubjectLifecycleMaintenanceKind kind, string suffix, long projection)
        {
            BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("l47-tests", suffix, suffix, BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(suffix))));
            var request = new BaseSubjectAuthorityMaintenanceExecutionRequest
            {
                Lifecycle=new(){Kind=kind,ContractId="example.user",ContractVersion=1,ConsumerId=consumer.Id,ConsumerVersion=consumer.Version,ExpectedProjectionGeneration=projection,ExpectedDeliveryEpoch=1,PlanChecksum=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{suffix}-plan"))}, Identity = identity, CombinedPlanChecksum = new byte[32], ExpectedStoreGeneration = 1, ExpectedSchemaGeneration = 1,
                ExpectedRestoreEpoch = 0, ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "0", PageSize = 1,
                OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            };
            return request with { CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(request) };
        }
    }

    [Fact]
    public async Task L47_InMemory_scope_rotation_is_bounded_atomic_and_invalidates_old_cursor_authority()
    {
        await using SubjectFixture fixture = Build(lifecycleConsumer: true, scopeRotationKeys: true);
        IBaseRecordRuntime records = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await records.CreateAsync(Private.Id, Create("rotation-user", ("active", true), ("tenant", "tenant-a")), principal, Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        Assert.True((await records.PatchAsync(Private.Id, new RecordId("rotation-user"), Patch(("active", false)), principal, Operation(BaseOperationKind.Patch, Private.Id))).IsSuccess());
        BaseInstalledSubjectLifecycleConsumer installed = fixture.Services.GetRequiredService<BaseSubjectLifecycleRegistry>().All.Single();
        IBaseSubjectLifecycleRuntime lifecycle = fixture.Services.GetRequiredService<IBaseSubjectLifecycleRuntime>();
        BaseSession session = fixture.Services.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseResult<BaseUntypedSubjectLifecyclePage> before = await lifecycle.ReadUntypedAsync(session, installed, null, 1, CancellationToken.None);
        BaseSubjectLifecycleCursor oldCursor = Assert.IsType<BaseSuccess<BaseUntypedSubjectLifecyclePage>>(before).Value.Next!;
        PrincipalContext otherTenant = new()
        {
            AuthenticationState = PrincipalAuthenticationState.Service,
            SubjectKind = AccessSubjectKind.ServicePrincipal,
            SubjectId = "service-1",
            CurrentTenantId = "tenant-b",
        };
        BaseSession otherTenantSession = fixture.Services.GetRequiredService<IBaseSessionFactory>().For(otherTenant);
        BaseFailure<BaseUntypedSubjectLifecyclePage> crossScope = Assert.IsType<BaseFailure<BaseUntypedSubjectLifecyclePage>>(
            await lifecycle.ReadUntypedAsync(otherTenantSession, installed, oldCursor, 1, CancellationToken.None));
        Assert.Equal(BaseSubjectErrorCodes.LifecycleUnauthorized, crossScope.Error.Code);
        BaseSubjectLifecycleConsumerDefinition consumer = LifecycleConsumer();
        OperationResult<BaseSubjectLifecycleProviderPage> foreignSeek = await fixture.Store.ReadAsync(new()
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = fixture.Registration.Checksum, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version,
            ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum),
            ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-b" },
            Take = 1, MaximumResultBytes = 4096, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        Assert.True(foreignSeek.IsSuccess(), foreignSeek.Error?.Code);
        Assert.Empty(foreignSeek.Value!.Facts);
        Assert.Equal(0, foreignSeek.Value.Accounting.RowsHydrated);

        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("l47-tests", "scope-rotation", "scope-rotation", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("scope-rotation"u8)));
        var request = new BaseSubjectAuthorityMaintenanceExecutionRequest
        {
            Lifecycle=new(){Kind=BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection,ExpectedDeliveryEpoch=1,PlanChecksum=System.Security.Cryptography.SHA256.HashData("rotate-plan"u8)}, Identity = identity, CombinedPlanChecksum = new byte[32],
            ExpectedStoreGeneration = 1, ExpectedSchemaGeneration = 1, ExpectedRestoreEpoch = 0,
            ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "0", ReplacementScopeProtectionKeyId = "1", PageSize = 1,
            OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
        };
        request = request with { CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(request) };
        var processor = new BaseSubjectAuthorityMaintenanceProcessor();
        RecordMutationExecutionResult rotated = await fixture.Store.ExecuteMaintenanceAsync(processor, request);
        Assert.True(rotated.Outcome == RecordMutationExecutionOutcome.Committed, rotated.Error?.Code ?? rotated.Processing?.Error?.Code);
        Assert.Equal(2, processor.LifecycleResult!.DeliveryEpoch);
        Assert.Equal(2, processor.LifecycleResult.ProjectionGeneration);

        BaseResult<BaseUntypedSubjectLifecyclePage> resumed = await lifecycle.ReadUntypedAsync(session, installed, oldCursor, 1, CancellationToken.None);
        BaseFailure<BaseUntypedSubjectLifecyclePage> failure = Assert.IsType<BaseFailure<BaseUntypedSubjectLifecyclePage>>(resumed);
        Assert.Equal(OperationStatus.Conflict, failure.Status);
        Assert.Equal(BaseSubjectErrorCodes.CursorOvertaken, failure.Error.Code);
    }

    [Fact]
    public async Task L47_zero_consumer_graph_preserves_canonical_fact_without_delivery_authority()
    {
        await using SubjectFixture fixture = Build(lifecycleConsumer: false);
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        OperationResult<RecordEnvelope> created = await runtime.CreateAsync(
            Private.Id, Create("user-zero", ("active", true), ("tenant", "tenant-a")), Principal(),
            Operation(BaseOperationKind.Create, Private.Id));
        Assert.True(created.IsSuccess(), created.Error?.Code);

        OperationResult<BaseSubjectLifecycleProviderInspection> inspection = await fixture.Store.InspectAsync(new()
        {
            ContractId = "example.user", ContractVersion = 1,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.ExactScope,
                ExactScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                InstalledAuthorityDigest = fixture.Registration.Checksum,
            },
            IncludeTerminalReceipt = false, MaximumResultBytes = 4096,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });

        Assert.True(inspection.IsSuccess(), inspection.Error?.Code);
        Assert.Empty(inspection.Value!.Consumers);
        Assert.NotNull(inspection.Value.EarliestRetained);
        Assert.Equal(inspection.Value.EarliestRetained, inspection.Value.HighWater);

        OperationResult<BaseSubjectLifecycleProviderPage> delivery = await fixture.Store.ReadAsync(new()
        {
            ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = fixture.Registration.Checksum, ConsumerId = "uninstalled", ConsumerVersion = 1,
            ConsumerChecksum = new string('0', 64), ProjectionGeneration = 1,
            Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
            Take = 1, MaximumResultBytes = 4096, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });
        Assert.False(delivery.IsSuccess());
        Assert.Equal(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, delivery.Error?.Code);
    }

    [Fact]
    public async Task L47_provider_rejects_uninstalled_all_scope_inspection_authority()
    {
        await using SubjectFixture fixture = Build();
        OperationResult<BaseSubjectLifecycleProviderInspection> inspection = await fixture.Store.InspectAsync(new()
        {
            ContractId = "example.user",
            ContractVersion = 1,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.AllAuthorizedScopes,
                InstalledAuthorityDigest = new string('0', 64),
            },
            IncludeTerminalReceipt = false,
            MaximumResultBytes = 4096,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });

        Assert.False(inspection.IsSuccess());
        Assert.Equal(OperationStatus.PolicyDenied, inspection.Status);
        Assert.Equal(BaseSubjectErrorCodes.LifecycleUnauthorized, inspection.Error?.Code);
    }

    [Fact]
    public async Task L47_provider_accepts_only_the_immutable_installed_all_scope_inspection_receipt()
    {
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await using SubjectFixture fixture = Build(allScopeInspectionDigest: digest);
        OperationResult<BaseSubjectLifecycleProviderInspection> inspection = await fixture.Store.InspectAsync(new()
        {
            ContractId = "example.user", ContractVersion = 1,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.AllAuthorizedScopes,
                InstalledAuthorityDigest = digest,
            },
            IncludeTerminalReceipt = false, MaximumResultBytes = 4096,
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
        });

        Assert.True(inspection.IsSuccess(), inspection.Error?.Code);
        Assert.Null(inspection.Value!.TerminalReceipt);
    }

    [Fact]
    public async Task Missing_exact_validation_grant_fails_before_store_resolution()
    {
        BaseGeneratedSubjectRegistration registration = BaseGeneratedSubjects.Register<UserSubject>(SubjectDefinition());
        FieldDefinition referenceField = Consumer.Fields!.Single() with
        {
            SubjectReference = Consumer.Fields!.Single().SubjectReference! with { ContractChecksum = registration.Checksum },
        };
        using ServiceProvider services = OperationTestServices.Build(
            fields: [referenceField],
            configureServices: registrations =>
            {
                registrations.AddSingleton(new BaseSubjectContractRegistry([registration]));
                registrations.AddSingleton<IBasePolicyOrchestrator>(new MissingGrantPolicy());
                registrations.AddSingleton<IBaseStoreExecutionResolver>(new ThrowingResolver());
            });
        JsonElement reference = JsonSerializer.Deserialize<JsonElement>(
            "{\"subjectId\":\"user-1\",\"authorityEpoch\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"incarnation\":\"BBBBBBBBBBBBBBBBBBBBBA\"}");

        OperationResult<RecordEnvelope> result = await services.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
            "items", Create("consumer", ("owner", reference)), Principal(), Operation(BaseOperationKind.Create, "items"));

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, result.Error?.Code);
    }

    [Fact]
    public async Task Ordinary_mixed_delete_recreate_is_rejected_and_preserves_the_old_lifetime()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(
            Private.Id,
            Create("user-1", ("active", true), ("tenant", "tenant-a")),
            principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement oldReference = await fixture.AcquireAsync("user-1");

        OperationResult<BaseRecordBatchResult> result = await runtime.BatchAsync(
            new BaseRecordBatchRequest
            {
                Mode = BaseRecordBatchExecutionMode.Atomic,
                Operations =
                [
                    new BaseRecordBatchItem
                    {
                        ItemId = "consumer", CollectionId = Consumer.Id, Kind = BaseRecordMutationKind.Create,
                        Create = Create("profile", ("owner", oldReference)),
                    },
                    new BaseRecordBatchItem
                    {
                        ItemId = "tombstone", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Patch,
                        RecordId = new RecordId("user-1"), Patch = Patch(("active", false), ("tombstoned", true)),
                    },
                    new BaseRecordBatchItem
                    {
                        ItemId = "retire", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Delete,
                        RecordId = new RecordId("user-1"), Delete = new RecordDeleteRequest(),
                    },
                    new BaseRecordBatchItem
                    {
                        ItemId = "recreate", CollectionId = Private.Id, Kind = BaseRecordMutationKind.Create,
                        Create = Create("user-1", ("active", true), ("tenant", "tenant-a")),
                    },
                ],
            },
            principal,
            Operation(BaseOperationKind.Batch, Consumer.Id));

        Assert.Equal(BaseRecordBatchOutcome.RolledBack, result.Value?.Outcome);
        Assert.Contains(result.Value!.Items, item => item.Error?.Code == BaseSubjectErrorCodes.LifecycleUnauthorized);
        JsonElement stillCurrent = await fixture.AcquireAsync("user-1");
        Assert.Equal(
            oldReference.GetProperty("incarnation").GetString(),
            stillCurrent.GetProperty("incarnation").GetString());
        Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(
            Consumer.Id,
            new RecordId("profile"),
            principal,
            Operation(BaseOperationKind.Get, Consumer.Id))).Status);
    }

    [Fact]
    public async Task InMemory_rotation_rewrites_current_references_and_invalidates_the_old_epoch()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(
            Private.Id,
            Create("user-1", ("active", true), ("tenant", "tenant-a")),
            principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement oldReference = await fixture.AcquireAsync("user-1");
        Assert.True((await runtime.CreateAsync(
            Consumer.Id,
            Create("profile-1", ("owner", oldReference)),
            principal,
            Operation(BaseOperationKind.Create, Consumer.Id))).IsSuccess());

        OperationResult<BaseSubjectEpochRotationResult> rotation = await fixture.Store.RotateEpochAsync(
            new BaseSubjectEpochRotationRequest
            {
                ContractId = "example.user",
                ContractVersion = 1,
                ExpectedStateGeneration = 1,
                DestructiveIntent = "rotate-subject-authority-epoch",
            });

        Assert.True(rotation.IsSuccess(), rotation.Error?.Code);
        Assert.Equal(1, rotation.Value!.RewrittenReferences);
        RecordEnvelope rewritten = (await runtime.GetAsync(
            Consumer.Id,
            new RecordId("profile-1"),
            principal,
            Operation(BaseOperationKind.Get, Consumer.Id))).Value!;
        Assert.NotEqual(
            oldReference.GetProperty("authorityEpoch").GetString(),
            rewritten.Payload.Fields!["owner"].GetProperty("authorityEpoch").GetString());
        Assert.Equal(
            oldReference.GetProperty("incarnation").GetString(),
            rewritten.Payload.Fields["owner"].GetProperty("incarnation").GetString());
        OperationResult<RecordEnvelope> stale = await runtime.CreateAsync(
            Consumer.Id,
            Create("profile-2", ("owner", oldReference)),
            principal,
            Operation(BaseOperationKind.Create, Consumer.Id));
        Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, stale.Error?.Code);
        BaseMutationJournalPage journal = await fixture.Store.ReadMutationJournalAsync(
            new BaseMutationJournalReadRequest { Limit = 16 });
        Assert.Equal(BaseSubjectAuthorityPublicationKind.EpochRotation,
            journal.Entries[^1].SubjectAuthorityPublication?.Kind);
    }

    [Fact]
    public async Task Identified_duplicate_replays_the_stored_result_without_revalidating_a_retired_subject()
    {
        await using SubjectFixture fixture = Build();
        IBaseRecordRuntime runtime = fixture.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = Principal();
        Assert.True((await runtime.CreateAsync(
            Private.Id,
            Create("user-1", ("active", true), ("tenant", "tenant-a")),
            principal,
            Operation(BaseOperationKind.Create, Private.Id))).IsSuccess());
        JsonElement reference = await fixture.AcquireAsync("user-1");
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "subject-tests",
            "identified-reference",
            "request-1",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("subject-receipt"u8)));
        var request = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            RequestIdentity = identity,
            Operations =
            [
                new BaseRecordBatchItem
                {
                    ItemId = "consumer",
                    CollectionId = Consumer.Id,
                    Kind = BaseRecordMutationKind.Create,
                    Create = Create("profile-identified", ("owner", reference)),
                },
            ],
        };

        OperationResult<BaseRecordBatchResult> committed = await runtime.BatchAsync(
            request,
            principal,
            Operation(BaseOperationKind.Batch, Consumer.Id));
        Assert.Equal(BaseMutationRequestDisposition.Committed, committed.Value?.RequestDisposition);
        Assert.True((await runtime.PatchAsync(
            Private.Id,
            new RecordId("user-1"),
            Patch(("active", false)),
            principal,
            Operation(BaseOperationKind.Patch, Private.Id))).IsSuccess());

        OperationResult<BaseRecordBatchResult> duplicate = await runtime.BatchAsync(
            request,
            principal,
            Operation(BaseOperationKind.Batch, Consumer.Id));

        Assert.Equal(BaseMutationRequestDisposition.Duplicate, duplicate.Value?.RequestDisposition);
        Assert.Equal(committed.Value?.Items[0].Revision, duplicate.Value?.Items[0].Revision);
    }

    [Fact]
    public async Task Generated_graph_executes_subject_lifecycle_and_validation_through_SQLite()
    {
        string database = Path.Combine(Path.GetTempPath(), $"hpd-base-l45-{Guid.NewGuid():N}.db");
        try
        {
            BaseCollection<L45SqlitePrivateUser> privateCollection = L45SqlitePrivateUser.Collection;
            var services = new ServiceCollection().AddLogging();
            services.AddHPDBase(builder => builder
                .AddTestPolicyAuthority<GrantingPolicy>()
                .AddTestStaticGrant("system.private")
                .AddTestStaticGrant("example.user.validate")
                .AddTestStaticGrant("example.user.acquire")
                .AddTestSubjectLifecycleGrant("example.sqlite-profile.lifecycle.read", "l45.sqlite.application", "example.profiles", "example.sqlite-profile.lifecycle", "example.sqlite-user", 1)
                .AddTestSubjectLifecycleGrant("base.subjectLifecycle.feed.read", "l45.sqlite.application", "example.profiles", "base.subjectLifecycle.feed.read", "example.sqlite-user", 1)
                .AddTestSubjectLifecycleGrant("base.subjectLifecycle.feed.checkpoint", "l45.sqlite.application", "example.profiles", "base.subjectLifecycle.feed.checkpoint", "example.sqlite-user", 1)
                .AddTestSubjectLifecycleGrant("base.subjectLifecycle.tombstone", "l45.sqlite.application", "example.auth", "base.subjectLifecycle.tombstone", "example.sqlite-user", 1)
                .AddTestSubjectLifecycleGrant("base.subjectLifecycle.finalizeRetirement", "l45.sqlite.application", "example.auth", "base.subjectLifecycle.finalizeRetirement", "example.sqlite-user", 1)
                .ConfigureSchema(options =>
                {
                    options.ApplicationId = "l45.sqlite.application";
                    options.PlanProtectionKey = Enumerable.Repeat((byte)0x45, 32).ToArray();
                })
                .AddCollection(privateCollection)
                .AddCollection(L45SqliteProfile.Collection)
                .AddExportedSubject(L45SqliteUserSubject.HPDBaseSubjectRegistration)
                .AddSubjectLifecycleConsumer(SqliteLifecycleConsumer())
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 47, Key = Enumerable.Repeat((byte)0x47, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch,
                })
                .AddRead(L45AcquireSqliteUser.Definition)
                .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
                {
                    Id = "example.sqlite-user.acquire.v1",
                    Version = 1,
                    ContractId = "example.sqlite-user",
                    ContractVersion = 1,
                    RegisteredReadId = "example.sqlite-user.acquire",
                    RequiredGrantId = "example.user.acquire",
                    Audience = HPDBaseEndpointAudience.Application,
                    MaximumResults = 1,
                })
                .UseStore(SqliteStore.Configure(options =>
                {
                    options.StoreId = "l45-sqlite";
                    options.DataSource = database;
                })));

            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "l45-sqlite" });
            Assert.True(planned.IsSuccess(), planned.Error?.Code);
            OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
                new BaseSchemaApplyRequest { ProtectedArtifact = planned.Value!.ProtectedArtifact });
            Assert.True(applied.IsSuccess(), applied.Error?.Code);
            OperationResult<BaseApplicationReadiness> readiness = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
            Assert.True(readiness.IsSuccess(), readiness.Error?.Code);
            var publicationStore = (IBaseSubjectPublicationStore)provider.GetRequiredService<IRecordStoreRegistry>()
                .GetStoreForCollection(privateCollection.Id)!;
            BaseSubjectCurrentPublicationState publication = Assert.Single(
                (await publicationStore.ReadCurrentSubjectPublicationsAsync()).Value!);
            Assert.Equal(L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum, publication.ContractChecksum);

            IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
            PrincipalContext principal = Principal();
            OperationResult<RecordEnvelope> createdSubject = await runtime.CreateAsync(privateCollection.Id,
                Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
                Operation(BaseOperationKind.Create, privateCollection.Id));
            Assert.True(createdSubject.IsSuccess(), createdSubject.Error?.Code);

            BaseSession session = provider.GetRequiredService<IBaseSessionFactory>().For(principal);
            BaseResult<L45AcquireSqliteUser.Row[]> acquired = await session.Reads.ToArrayAsync(
                L45AcquireSqliteUser.Handle,
                new L45AcquireSqliteUser { UserId = BaseRecordId<L45SqlitePrivateUser>.Create("user-1") });
            L45AcquireSqliteUser.Row[] rows = acquired.RequireValue();
            BaseSubjectReference<L45SqliteUserSubject> typedReference = Assert.Single(rows).Reference;
            JsonElement reference = JsonSerializer.SerializeToElement(typedReference);
            OperationResult<RecordEnvelope> accepted = await runtime.CreateAsync(L45SqliteProfile.Collection.Id,
                Create("profile-1", ("owner", reference)), principal,
                Operation(BaseOperationKind.Create, L45SqliteProfile.Collection.Id));
            Assert.True(accepted.IsSuccess(), accepted.Error?.Code);

            Assert.True((await runtime.PatchAsync(privateCollection.Id, new RecordId("user-1"), Patch(("active", false)),
                principal, Operation(BaseOperationKind.Patch, privateCollection.Id))).IsSuccess());
            OperationResult<RecordEnvelope> rejected = await runtime.CreateAsync(L45SqliteProfile.Collection.Id,
                Create("profile-2", ("owner", reference)), principal,
                Operation(BaseOperationKind.Create, L45SqliteProfile.Collection.Id));
            Assert.Equal(BaseSubjectErrorCodes.ReferenceInvalid, rejected.Error?.Code);
            Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(L45SqliteProfile.Collection.Id,
                new RecordId("profile-2"), principal, Operation(BaseOperationKind.Get, L45SqliteProfile.Collection.Id))).Status);

            Assert.True((await runtime.PatchAsync(
                privateCollection.Id,
                new RecordId("user-1"),
                Patch(("active", true)),
                principal,
                Operation(BaseOperationKind.Patch, privateCollection.Id))).IsSuccess());
            OperationResult<BaseRecordBatchResult> recreate = await runtime.BatchAsync(
                new BaseRecordBatchRequest
                {
                    Mode = BaseRecordBatchExecutionMode.Atomic,
                    Operations =
                    [
                        new BaseRecordBatchItem
                        {
                            ItemId = "profile-recreate", CollectionId = L45SqliteProfile.Collection.Id,
                            Kind = BaseRecordMutationKind.Create,
                            Create = Create("profile-3", ("owner", reference)),
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "tombstone", CollectionId = privateCollection.Id,
                            Kind = BaseRecordMutationKind.Patch, RecordId = new RecordId("user-1"),
                            Patch = Patch(("active", false), ("tombstoned", true)),
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "retire", CollectionId = privateCollection.Id,
                            Kind = BaseRecordMutationKind.Delete, RecordId = new RecordId("user-1"),
                            Delete = new RecordDeleteRequest(),
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "recreate", CollectionId = privateCollection.Id,
                            Kind = BaseRecordMutationKind.Create,
                            Create = Create("user-1", ("active", true), ("tenant", "tenant-a")),
                        },
                    ],
                },
                principal,
                Operation(BaseOperationKind.Batch, L45SqliteProfile.Collection.Id));
            Assert.Equal(BaseRecordBatchOutcome.RolledBack, recreate.Value?.Outcome);
            Assert.Contains(recreate.Value!.Items, item => item.Error?.Code == BaseSubjectErrorCodes.LifecycleUnauthorized);
            Assert.Equal(OperationStatus.NotFound, (await runtime.GetAsync(
                L45SqliteProfile.Collection.Id,
                new RecordId("profile-3"),
                principal,
                Operation(BaseOperationKind.Get, L45SqliteProfile.Collection.Id))).Status);

            RecordEnvelope beforeTombstone = (await runtime.GetAsync(privateCollection.Id, new RecordId("user-1"), principal,
                Operation(BaseOperationKind.Get, privateCollection.Id))).Value!;
            BaseMutationRequestIdentity tombstoneIdentity = BaseMutationRequestIdentity.Create("l47-sqlite", "tombstone", "user-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("l47-sqlite-tombstone"u8)));
            BaseExportedSubjectContract<L45SqliteUserSubject> exporter = L45SqliteUserSubject.Contract(session);
            BaseResult<BaseSubjectLifecycleFact<L45SqliteUserSubject>> tombstoned = await exporter.TombstoneAsync(new()
            {
                Subject = typedReference,
                ExpectedPrivateRevision = beforeTombstone.Metadata.Revision!.Value,
                Identity = tombstoneIdentity,
            });
            Assert.Equal(BaseSubjectLifecycleState.Tombstoned, tombstoned.RequireValue().Fact.Transitioned!.CurrentState);
            BaseResult<BaseSubjectLifecycleFact<L45SqliteUserSubject>> tombstoneReplay = await exporter.TombstoneAsync(new()
            {
                Subject = typedReference,
                ExpectedPrivateRevision = beforeTombstone.Metadata.Revision!.Value,
                Identity = tombstoneIdentity,
            });
            Assert.Equal(tombstoned.RequireValue().Fact.SubjectSequence, tombstoneReplay.RequireValue().Fact.SubjectSequence);

            RecordEnvelope beforeRetirement = (await runtime.GetAsync(privateCollection.Id, new RecordId("user-1"), principal,
                Operation(BaseOperationKind.Get, privateCollection.Id))).Value!;
            BaseMutationRequestIdentity retirementIdentity = BaseMutationRequestIdentity.Create("l47-sqlite", "retire", "user-1", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("l47-sqlite-retire"u8)));
            BaseResult<BaseSubjectFinalRetirementResult<L45SqliteUserSubject>> retired = await exporter.FinalizeRetirementAsync(new()
            {
                Subject = typedReference,
                ExpectedTombstoneSequence = tombstoned.RequireValue().Fact.SubjectSequence,
                ExpectedPrivateRevision = beforeRetirement.Metadata.Revision!.Value,
                Identity = retirementIdentity,
            });
            Assert.True(retired is BaseSuccess<BaseSubjectFinalRetirementResult<L45SqliteUserSubject>>, (retired as BaseFailure<BaseSubjectFinalRetirementResult<L45SqliteUserSubject>>)?.Error.Code);
            Assert.False(retired.RequireValue().Duplicate);
            BaseResult<BaseSubjectFinalRetirementResult<L45SqliteUserSubject>> retirementReplay = await exporter.FinalizeRetirementAsync(new()
            {
                Subject = typedReference,
                ExpectedTombstoneSequence = tombstoned.RequireValue().Fact.SubjectSequence,
                ExpectedPrivateRevision = beforeRetirement.Metadata.Revision!.Value,
                Identity = retirementIdentity,
            });
            Assert.True(retirementReplay.RequireValue().Duplicate);

            BaseSubjectLifecycleConsumerDefinition lifecycleConsumer = SqliteLifecycleConsumer();
            var lifecycleStore = (IBaseSubjectLifecycleStore)provider.GetRequiredService<IRecordStoreRegistry>().GetStore("l45-sqlite")!;
            OperationResult<BaseSubjectLifecycleProviderInspection> terminal = await lifecycleStore.InspectAsync(new()
            {
                ContractId = "example.sqlite-user", ContractVersion = 1,
                ScopeAuthority = new BaseSubjectScopeQueryAuthority
                {
                    Mode = BaseSubjectScopeQueryMode.ExactScope,
                    ExactScope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                    InstalledAuthorityDigest = L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum,
                },
                SubjectId = BaseSubjectId.Create("user-1", BaseSubjectIdKind.OrdinalString),
                IncludeTerminalReceipt = true, MaximumResultBytes = 4096,
                DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            });
            Assert.True(terminal.IsSuccess(), terminal.Error?.Code);
            Assert.Equal(typedReference.AuthorityEpoch, terminal.Value!.TerminalReceipt!.RetiredAuthorityEpoch);
            Assert.Equal(typedReference.Incarnation, terminal.Value.TerminalReceipt.RetiredIncarnation);
            Assert.Equal(retired.RequireValue().RetiredSubjectSequence, terminal.Value.TerminalReceipt.RetiredSubjectSequence);

            OperationResult<RecordEnvelope> recreatedSubject = await runtime.CreateAsync(privateCollection.Id,
                Create("user-1", ("active", true), ("tenant", "tenant-a")), principal,
                Operation(BaseOperationKind.Create, privateCollection.Id));
            Assert.True(recreatedSubject.IsSuccess(), recreatedSubject.Error?.Code);
            L45AcquireSqliteUser.Row recreatedReference = Assert.Single((await session.Reads.ToArrayAsync(
                L45AcquireSqliteUser.Handle,
                new L45AcquireSqliteUser { UserId = BaseRecordId<L45SqlitePrivateUser>.Create("user-1") })).RequireValue());
            Assert.Equal(typedReference.AuthorityEpoch, recreatedReference.Reference.AuthorityEpoch);
            Assert.NotEqual(typedReference.Incarnation, recreatedReference.Reference.Incarnation);

            await MakeFirstTwoLifecycleFactsShareCommitPositionAsync(database);
            BaseSubjectLifecycleProviderReadRequest firstPageRequest = LifecycleRead(after: null, take: 1);
            OperationResult<BaseSubjectLifecycleProviderPage> firstBoundaryPage = await lifecycleStore.ReadAsync(firstPageRequest);
            Assert.True(firstBoundaryPage.IsSuccess(), firstBoundaryPage.Error?.Code);
            BaseSubjectLifecycleProviderFact firstBoundaryFact = Assert.Single(firstBoundaryPage.Value!.Facts);
            long sqliteExactResultBytes = firstBoundaryPage.Value.Accounting.ResultBytes;
            Assert.True((await lifecycleStore.ReadAsync(firstPageRequest with { MaximumResultBytes = sqliteExactResultBytes })).IsSuccess());
            OperationResult<BaseSubjectLifecycleProviderPage> sqliteOneByteShort = await lifecycleStore.ReadAsync(
                firstPageRequest with { MaximumResultBytes = sqliteExactResultBytes - 1 });
            Assert.Equal(BaseSubjectErrorCodes.LifecycleCapacityExceeded, sqliteOneByteShort.Error?.Code);
            OperationResult<BaseSubjectLifecycleProviderPage> secondBoundaryPage = await lifecycleStore.ReadAsync(
                LifecycleRead(firstBoundaryFact.Boundary, take: 1));
            Assert.True(secondBoundaryPage.IsSuccess(), secondBoundaryPage.Error?.Code);
            BaseSubjectLifecycleProviderFact secondBoundaryFact = Assert.Single(secondBoundaryPage.Value!.Facts);
            Assert.Equal(firstBoundaryFact.Boundary.CommitPosition, secondBoundaryFact.Boundary.CommitPosition);
            Assert.NotEqual(firstBoundaryFact.Boundary.SubjectSequence, secondBoundaryFact.Boundary.SubjectSequence);

            OperationResult<BaseSubjectLifecycleProviderPage> lifecyclePage = await lifecycleStore.ReadAsync(new BaseSubjectLifecycleProviderReadRequest
            {
                ApplicationId = "l45.sqlite.application", ContractId = "example.sqlite-user", ContractVersion = 1,
                ContractChecksum = L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum,
                ConsumerId = lifecycleConsumer.Id, ConsumerVersion = lifecycleConsumer.Version,
                ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(lifecycleConsumer), L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum),
                ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                Take = 256, MaximumResultBytes = 1_048_576, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            });
            Assert.True(lifecyclePage.IsSuccess(), lifecyclePage.Error?.Code);
            Assert.Contains(lifecyclePage.Value!.Facts, static value => value.Fact.Kind == BaseSubjectLifecycleFactKind.Created);
            Assert.Contains(lifecyclePage.Value.Facts, static value => value.Fact.Transitioned?.CurrentState == BaseSubjectLifecycleState.Inactive);
            Assert.Contains(lifecyclePage.Value.Facts, static value => value.Fact.Transitioned?.CurrentState == BaseSubjectLifecycleState.Tombstoned);
            Assert.Contains(lifecyclePage.Value.Facts, static value => value.Fact.Kind == BaseSubjectLifecycleFactKind.Retired);
            Assert.Contains(lifecyclePage.Value.Facts, value => value.Fact.Kind == BaseSubjectLifecycleFactKind.Created
                && value.Fact.Incarnation.Equals(recreatedReference.Reference.Incarnation)
                && value.Fact.SubjectSequence == 1);
            BaseGeneratedSubjectLifecycleConsumerIdentity<L45SqliteUserSubject> generatedConsumer = BaseGeneratedSubjectLifecycleConsumers.Register<L45SqliteUserSubject>(lifecycleConsumer, L45SqliteUserSubject.HPDBaseSubjectRegistration);
            BaseInstalledSubjectLifecycleConsumer<L45SqliteUserSubject> handle = session.SubjectLifecycle.Get(generatedConsumer);
            BaseResult<BaseSubjectLifecyclePage<L45SqliteUserSubject>> typedPage = await handle.ReadAsync(take: 2);
            Assert.IsType<BaseSuccess<BaseSubjectLifecyclePage<L45SqliteUserSubject>>>(typedPage); Assert.Equal(2, typedPage.RequireValue().Facts.Length);
            await using IAsyncEnumerator<BaseSubjectLifecycleDelivery<L45SqliteUserSubject>> deliveries = handle.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
            Assert.True(await deliveries.MoveNextAsync()); BaseSubjectLifecycleDelivery<L45SqliteUserSubject> delivery = deliveries.Current;
            BaseResult<BaseSubjectLifecycleCheckpointResult> substitutedAdvance = await handle.AdvanceAsync(delivery.Checkpoint,
                BaseMutationRequestIdentity.Create("subject-lifecycle:example.sqlite-profile.lifecycle", "subjectLifecycle.advance", "substituted", BaseMutationRequestFingerprint.Create(new byte[32])));
            Assert.Equal(BaseSubjectErrorCodes.LifecycleContractInvalid, Assert.IsType<BaseFailure<BaseSubjectLifecycleCheckpointResult>>(substitutedAdvance).Error.Code);
            BaseResult<BaseSubjectLifecycleCheckpointResult> handleAdvance = await handle.AdvanceAsync(delivery.Checkpoint, delivery.AdvanceIdentity);
            Assert.True(handleAdvance is BaseSuccess<BaseSubjectLifecycleCheckpointResult>, (handleAdvance as BaseFailure<BaseSubjectLifecycleCheckpointResult>)?.Error.Code); Assert.Equal(1, handleAdvance.RequireValue().CheckpointGeneration);
            BaseResult<BaseSubjectLifecycleCheckpointResult> handleReplay = await handle.AdvanceAsync(delivery.Checkpoint, delivery.AdvanceIdentity);
            Assert.Equal(OperationStatus.Ok, handleReplay.Status); Assert.True(handleReplay.RequireValue().Duplicate);
            BaseMutationRequestIdentity checkpointIdentity = BaseMutationRequestIdentity.Create("l47-sqlite", "advance", "one", BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("l47-sqlite-advance"u8)));
            var checkpointRequest = new BaseSubjectLifecycleProviderCheckpointRequest { ApplicationId = "l45.sqlite.application", ContractId = "example.sqlite-user", ContractVersion = 1, ConsumerId = lifecycleConsumer.Id, ConsumerVersion = lifecycleConsumer.Version, ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(lifecycleConsumer), L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum), ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" }, Through = lifecyclePage.Value.Through, ExpectedCheckpointGeneration = 1, Identity = checkpointIdentity, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1) };
            var sqliteCheckpointProcessor = new BaseSubjectLifecycleCheckpointProcessor(checkpointRequest);
            RecordMutationExecutionResult sqliteAdvance = await lifecycleStore.AdvanceCheckpointAsync(sqliteCheckpointProcessor, LifecycleCheckpointExecution(checkpointRequest));
            Assert.Equal(RecordMutationExecutionOutcome.Committed, sqliteAdvance.Outcome); Assert.Equal(2, sqliteCheckpointProcessor.Result!.CheckpointGeneration);
            var sqliteDuplicateProcessor = new BaseSubjectLifecycleCheckpointProcessor(checkpointRequest);
            RecordMutationExecutionResult sqliteDuplicate = await lifecycleStore.AdvanceCheckpointAsync(sqliteDuplicateProcessor, LifecycleCheckpointExecution(checkpointRequest));
            Assert.Equal(RecordMutationExecutionOutcome.Committed, sqliteDuplicate.Outcome); Assert.True(sqliteDuplicateProcessor.Result!.Duplicate);

            BaseSubjectLifecycleProviderReadRequest LifecycleRead(BaseSubjectLifecycleOrderingBoundary? after, int take) => new()
            {
                ApplicationId = "l45.sqlite.application", ContractId = "example.sqlite-user", ContractVersion = 1,
                ContractChecksum = L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum,
                ConsumerId = lifecycleConsumer.Id, ConsumerVersion = lifecycleConsumer.Version,
                ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(lifecycleConsumer), L45SqliteUserSubject.HPDBaseSubjectRegistration.Checksum),
                ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                After = after, Take = take, MaximumResultBytes = 1_048_576, DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            };
        }
        finally
        {
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
        }
    }

    private static async ValueTask MakeFirstTwoLifecycleFactsShareCommitPositionAsync(string database)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        var rows = new List<(long Position, string Subject, byte[] Epoch, byte[] Incarnation, long Sequence)>();
        await using (Microsoft.Data.Sqlite.SqliteCommand select = connection.CreateCommand())
        {
            select.CommandText = "SELECT commit_position,subject_id,authority_epoch,incarnation,subject_sequence FROM hpd_base_subject_lifecycle_facts ORDER BY commit_position,subject_id,authority_epoch,incarnation,subject_sequence LIMIT 2;";
            await using Microsoft.Data.Sqlite.SqliteDataReader reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt64(0), reader.GetString(1), (byte[])reader.GetValue(2), (byte[])reader.GetValue(3), reader.GetInt64(4)));
        }
        Assert.Equal(2, rows.Count);
        (long firstPosition, _, _, _, _) = rows[0];
        (long secondPosition, string subject, byte[] epoch, byte[] incarnation, long sequence) = rows[1];
        Assert.NotEqual(firstPosition, secondPosition);
        await using Microsoft.Data.Sqlite.SqliteTransaction transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync();
        foreach (string table in new[] { "hpd_base_subject_lifecycle_memberships", "hpd_base_subject_lifecycle_facts" })
        {
            await using Microsoft.Data.Sqlite.SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {table} SET commit_position=$first WHERE commit_position=$second AND subject_id=$subject AND authority_epoch=$epoch AND incarnation=$incarnation AND subject_sequence=$sequence;";
            update.Parameters.AddWithValue("$first", firstPosition);
            update.Parameters.AddWithValue("$second", secondPosition);
            update.Parameters.AddWithValue("$subject", subject);
            update.Parameters.Add("$epoch", Microsoft.Data.Sqlite.SqliteType.Blob).Value = epoch;
            update.Parameters.Add("$incarnation", Microsoft.Data.Sqlite.SqliteType.Blob).Value = incarnation;
            update.Parameters.AddWithValue("$sequence", sequence);
            Assert.True(await update.ExecuteNonQueryAsync() > 0);
        }
        await transaction.CommitAsync();
    }

    private static SubjectFixture Build(bool lifecycleConsumer = false, string? allScopeInspectionDigest = null, Func<int, CancellationToken, ValueTask>? lifecycleMaintenancePageCompleted = null, bool scopeRotationKeys = false, TimeSpan? lifecycleReadTimeout = null, BaseSubjectLifecycleConsumerDefinition[]? lifecycleConsumers = null, TimeProvider? timeProvider = null, bool retirement = false)
    {
        timeProvider ??= TimeProvider.System;
        BaseGeneratedSubjectRegistration registration = BaseGeneratedSubjects.Register<UserSubject>(SubjectDefinition(retirement));
        BaseExportedSubjectDefinition subject = registration.Definition;
        CollectionDefinition[] collections = [Private, Consumer with
        {
            Fields = Consumer.Fields!.Select(field => field.SubjectReference is null ? field : field with
            {
                SubjectReference = field.SubjectReference with { ContractChecksum = registration.Checksum },
            }).ToArray(),
        }];
        BaseSubjectLifecycleConsumerDefinition[] installedLifecycleConsumers = lifecycleConsumers
            ?? (lifecycleConsumer||retirement ? [LifecycleConsumer(lifecycleReadTimeout)] : []);
        BaseSubjectRetirementConsumerDefinition[] retirementConsumers=[];BaseSubjectRetirementPolicy[] retirementPolicies=[];
        if(retirement)
        {
            string lifecycleChecksum=BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(installedLifecycleConsumers.Single()),registration.Checksum);
            BaseSubjectRetirementConsumerDefinition consumer=RetirementConsumer(lifecycleChecksum);string consumerChecksum=BaseSubjectRetirementRegistry.ConsumerChecksum(BaseSubjectRetirementRegistry.Normalize(consumer));
            BaseSubjectRetirementPolicy policy=RetirementPolicy(consumer,consumerChecksum);policy=policy with{PolicyChecksum=BaseSubjectRetirementRegistry.PolicyChecksum(policy with{PolicyChecksum=string.Empty})};
            retirementConsumers=[consumer];retirementPolicies=[policy];
        }
        var services = new ServiceCollection();
        var lifecycleTokens = new BaseOpaqueTokenProtector(Microsoft.Extensions.Options.Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey { Id = 0, Key = Enumerable.Repeat((byte)7, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch },
            DecryptionKeys = scopeRotationKeys ? [new BaseOpaqueTokenKey { Id = 1, Key = Enumerable.Repeat((byte)9, 32).ToArray(), IssueNotBefore = DateTimeOffset.UnixEpoch }] : [],
        }), timeProvider);
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddSingleton(lifecycleTokens);
        services.AddSingleton<IBaseDescriptorContributor>(new CollectionsContributor(collections));
        services.AddSingleton(new BaseCollectionRegistry(collections.ToDictionary(static value => value.Id, StringComparer.Ordinal)));
        services.AddTestSubjectLifecyclePolicyAuthority(new GrantingPolicy(),
            TestPolicyAuthorityExtensions.TestRuntimeGrant("system.private"),
            TestPolicyAuthorityExtensions.TestRuntimeGrant("example.user.validate"),
            TestPolicyAuthorityExtensions.TestRuntimeGrant("example.user.acquire"),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("base.subjectLifecycle.tombstone", "hpd.base.application", "example.auth", "base.subjectLifecycle.tombstone", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("base.subjectLifecycle.finalizeRetirement", "hpd.base.application", "example.auth", "base.subjectLifecycle.finalizeRetirement", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("example.profile.lifecycle.read", "hpd.base.application", "example.profiles", "example.profile.lifecycle", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("base.subjectLifecycle.feed.read", "hpd.base.application", "example.profiles", "base.subjectLifecycle.feed.read", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("base.subjectLifecycle.feed.checkpoint", "hpd.base.application", "example.profiles", "base.subjectLifecycle.feed.checkpoint", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("example.profile.lifecycle.reconcile", "hpd.base.application", "example.profiles", "example.profile.lifecycle", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("base.subjectLifecycle.reconcile.read", "hpd.base.application", "example.profiles", "base.subjectLifecycle.reconcile.read", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("example.profile.retirement.ack", "hpd.base.application", "example.profiles", "example.profile.lifecycle", "example.user", 1),
            TestPolicyAuthorityExtensions.TestSubjectLifecycleGrant("base.subjectRetirement.acknowledge", "hpd.base.application", "example.profiles", "base.subjectRetirement.acknowledge", "example.user", 1));
        services.AddSingleton(new BaseSubjectContractRegistry([registration]));
        if (installedLifecycleConsumers.Length != 0)
        {
            var lifecycleRegistry=new BaseSubjectLifecycleRegistry(installedLifecycleConsumers,new BaseSubjectContractRegistry([registration]));services.AddSingleton(lifecycleRegistry);
            if(retirement)services.AddSingleton(new BaseSubjectRetirementRegistry(retirementConsumers,retirementPolicies,lifecycleRegistry));
        }
        services.AddHPDBaseRuntime();
        services.AddSingleton<IBaseSessionFactory, DefaultBaseSessionFactory>();
        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        var storeOptions = new HPDBaseInMemoryStoreOptions
        {
            StoreId = "subject-transaction",
            Collections = collections,
            CollectionIds = collections.Select(static value => value.Id).ToArray(),
            ExportedSubjects = [subject],
            SubjectLifecycleConsumers = installedLifecycleConsumers,
            SubjectRetirementConsumers=retirementConsumers,SubjectRetirementPolicies=retirementPolicies,
            SubjectLifecycleMaintenancePageCompleted = lifecycleMaintenancePageCompleted,
            SubjectLifecycleInspectionAuthorities = allScopeInspectionDigest is null ? [] : [new BaseSubjectLifecycleInspectionAuthority
            {
                ContractId = subject.Id, ContractVersion = subject.Version, OwningModuleId = subject.OwningModuleId,
                GrantId = subject.AdministrationGrantId, Digest = allScopeInspectionDigest,
            }],
        };
        InMemoryRecordStore store = new(storeOptions, lifecycleTokens, timeProvider);
        provider.GetRequiredService<IRecordStoreRegistry>().Add(new RecordStoreRegistration
        {
            StoreId = store.Capabilities.StoreId,
            Store = store,
            CollectionIds = collections.Select(static value => value.Id).ToArray(),
        });
        return new SubjectFixture(provider, store, subject, registration);
    }

    private static BaseSubjectLifecycleConsumerDefinition LifecycleConsumer(TimeSpan? readTimeout = null) => new()
    {
        Id = "example.profile.lifecycle", Version = 1, OwningModuleId = "example.profiles", Audience = BaseSubjectLifecycleConsumerAudience.Service,
        ContractId = "example.user", ContractVersion = 1,
        ObservedStates = [BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Inactive, BaseSubjectLifecycleState.Tombstoned, BaseSubjectLifecycleState.Retired],
        DeliveryGrantId = "example.profile.lifecycle.read",
        Limits = new BaseSubjectLifecycleConsumerLimits { MaximumFactsPerPage = 256, MaximumResultBytes = 1_048_576, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(5) },
    };

    private static BaseSubjectRetirementConsumerDefinition RetirementConsumer(string lifecycleChecksum)=>new()
    {
        ConsumerId="example.profile.lifecycle",ConsumerVersion=1,OwningModuleId="example.profiles",Audience=BaseSubjectLifecycleConsumerAudience.Service,LifecycleConsumerChecksum=lifecycleChecksum,
        RetirementProfileId="example.profile.retirement",RetirementProfileVersion=1,RetirementProfileChecksum=new string('a',64),Participation=BaseSubjectRetirementParticipation.RequiredBeforePurge,
        AcknowledgementGrantId="example.profile.retirement.ack",Limits=new(){MaximumAcknowledgementsPerCommit=16,MaximumAcknowledgementRequestBytes=65_536,MaximumReceiptBytes=65_536,AcknowledgementTimeout=TimeSpan.FromSeconds(2),ReceiptResolutionTimeout=TimeSpan.FromSeconds(2)},
    };

    private static BaseSubjectRetirementPolicy RetirementPolicy(BaseSubjectRetirementConsumerDefinition consumer,string checksum)=>new()
    {
        ContractId="example.user",ContractVersion=1,AcceptedConsumers=[new(){ConsumerId=consumer.ConsumerId,ConsumerVersion=consumer.ConsumerVersion,OwningModuleId=consumer.OwningModuleId,Audience=consumer.Audience,LifecycleConsumerChecksum=consumer.LifecycleConsumerChecksum,RetirementProfileId=consumer.RetirementProfileId,RetirementProfileVersion=consumer.RetirementProfileVersion,RetirementProfileChecksum=consumer.RetirementProfileChecksum,Participation=consumer.Participation,AcknowledgementGrantId=consumer.AcknowledgementGrantId,Limits=consumer.Limits,RetirementConsumerChecksum=checksum}],CoordinationWindow=TimeSpan.FromHours(1),TimeoutBehavior=BaseSubjectRetirementTimeoutBehavior.Quarantine,PurgeRetention=new(){MinimumTombstoneAge=TimeSpan.Zero},PolicyChecksum=new string('0',64),
    };

    private static BaseSubjectLifecycleProviderReadRequest LifecycleReadRequest(SubjectFixture fixture, BaseSubjectLifecycleConsumerDefinition consumer) => new()
    {
        ApplicationId = "test.application", ContractId = "example.user", ContractVersion = 1,
        ContractChecksum = fixture.Registration.Checksum, ConsumerId = consumer.Id, ConsumerVersion = consumer.Version,
        ConsumerChecksum = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), fixture.Registration.Checksum),
        ProjectionGeneration = 1, Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
        Take = 256, MaximumResultBytes = 1_048_576, DeadlineUtc = DateTimeOffset.Parse("2030-01-01T00:00:00Z"),
    };

    private static BaseSubjectAuthorityMaintenanceExecutionRequest OvertakeRequest(
        SubjectFixture fixture, BaseSubjectLifecycleConsumerDefinition consumer, BaseSubjectLifecycleOrderingBoundary retainedFrom)
    {
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create("l47-tests", "overtake", "tenant-a",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("overtake-tenant-a"u8)));
        var request = new BaseSubjectAuthorityMaintenanceExecutionRequest
        {
            Lifecycle=new(){Kind=BaseSubjectLifecycleMaintenanceKind.MarkCheckpointOvertaken,ContractId="example.user",ContractVersion=1,ConsumerId=consumer.Id,ConsumerVersion=consumer.Version,Scope=new BaseOwnedSubjectScopeEvidence{Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"},RetainedFrom=retainedFrom,ExpectedProjectionGeneration=1,ExpectedDeliveryEpoch=1,PlanChecksum=System.Security.Cryptography.SHA256.HashData("overtake-plan"u8)}, Identity = identity, CombinedPlanChecksum = new byte[32],
            ExpectedStoreGeneration = 1, ExpectedSchemaGeneration = 1, ExpectedRestoreEpoch = 0,
            ExpectedScopeProtectionGeneration = 1, ExpectedScopeProtectionKeyId = "0", PageSize = 1,
            OperationTimeout = TimeSpan.FromSeconds(5), CommitCompletionTimeout = TimeSpan.FromSeconds(5),
        };
        return request with { CombinedPlanChecksum = BaseSubjectAuthorityMaintenanceProcessor.PlanChecksum(request) };
    }

    private static BaseSubjectLifecycleConsumerDefinition SqliteLifecycleConsumer() => new()
    {
        Id = "example.sqlite-profile.lifecycle", Version = 1, OwningModuleId = "example.profiles", Audience = BaseSubjectLifecycleConsumerAudience.Service,
        ContractId = "example.sqlite-user", ContractVersion = 1,
        ObservedStates = [BaseSubjectLifecycleState.Active, BaseSubjectLifecycleState.Inactive, BaseSubjectLifecycleState.Tombstoned, BaseSubjectLifecycleState.Retired],
        DeliveryGrantId = "example.sqlite-profile.lifecycle.read",
        Limits = new BaseSubjectLifecycleConsumerLimits { MaximumFactsPerPage = 256, MaximumResultBytes = 1_048_576, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5) },
    };

    private static RecordMutationExecutionRequest LifecycleCheckpointExecution(BaseSubjectLifecycleProviderCheckpointRequest request) => new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5),
        AtomicRequest = new BaseAtomicMutationExecutionRequest
        {
            Identity = request.Identity,
            StructuralDigest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"l47-checkpoint:{request.ConsumerId}:{request.ExpectedCheckpointGeneration}")),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            MaxReceiptBytes = 4096,
        },
    };

    private static readonly CollectionDefinition Private = new()
    {
        Id = "private.users", Name = "private.users", Kind = BaseCollectionKinds.Document,
        System = true, Exposed = false, SystemOwnerModuleId = "example.auth",
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition { Id = "user.active", ApplicationName = "active", WireName = "active", Type = BaseFieldTypes.Boolean, Required = true, Nullable = false },
            new FieldDefinition { Id = "user.tombstoned", ApplicationName = "tombstoned", WireName = "tombstoned", Type = BaseFieldTypes.Boolean, Required = true, Nullable = false },
            new FieldDefinition { Id = "user.tenant", ApplicationName = "tenant", WireName = "tenant", Type = BaseFieldTypes.String, Required = true, Nullable = false },
        ],
    };

    private static readonly CollectionDefinition Consumer = new()
    {
        Id = "profiles", Name = "profiles", Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
        MutationMode = BaseCollectionMutationMode.Mutable,
        Fields =
        [
            new FieldDefinition
            {
                Id = "profile.owner", ApplicationName = "owner", WireName = "owner", Type = BaseFieldTypes.Object, Required = true, Nullable = false,
                SubjectReference = new BaseSubjectReferenceDefinition
                {
                    ContractId = "example.user", ContractVersion = 1, ContractChecksum = new string('0', 64),
                    Requirement = BaseSubjectReferenceRequirement.Active,
                    Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot,
                },
            },
        ],
    };

    private static BaseExportedSubjectDefinition SubjectDefinition(bool coordinatedRetirement=false) => new()
    {
        Id = "example.user", Version = 1, OwningModuleId = "example.auth",
        SubjectIdKind = BaseSubjectIdKind.OrdinalString, MaximumSubjectIdUtf8Bytes = 64,
        Scope = BaseSubjectScopeKind.Tenant, AcquisitionGrantId = "example.user.acquire",
        ValidationGrantId = "example.user.validate", AdministrationGrantId = "example.user.admin", TombstoneFieldId = "user.tombstoned",
        SupportsCoordinatedRetirement = coordinatedRetirement, Audiences = [HPDBaseEndpointAudience.Application],
        ValidationPlan = new BaseSubjectValidationPlanDefinition
        {
            Id = "example.user.validate.v1", Version = 1, ContractId = "example.user", ContractVersion = 1,
            ContractChecksum = new string('0', 64), PrivateCollectionId = Private.Id,
            SubjectId = BaseSubjectIdBinding.RecordId,
            Active = new BaseSubjectActiveBinding { Kind = BaseSubjectActiveBindingKind.RequiredBooleanField, FieldId = "user.active", ActiveValue = true },
            Scope = new BaseSubjectScopeBinding { Kind = BaseSubjectScopeBindingKind.RequiredTenantField, FieldId = "user.tenant" },
            Access = BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,
            Limits = BaseSubjectValidationLimits.Default,
        },
    };

    private static RecordCreateRequest Create(string id, params (string Name, object Value)[] fields)
    {
        if (fields.Any(static field => field.Name == "active") && fields.All(static field => field.Name != "tombstoned"))
            fields = [.. fields, ("tombstoned", false)];
        return new() { RequestedId = new RecordId(id), Payload = Payload(fields) };
    }

    private static RecordPatchRequest Patch(params (string Name, object Value)[] fields) => new() { Patch = Payload(fields) };

    private static async ValueTask<JsonElement> AcquireAsync(
        IRelationalReadStore store,
        BaseExportedSubjectDefinition subject,
        string privateCollectionId,
        string id)
    {
        OperationResult<BaseRelationalReadExecutionResult> result = await store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
        {
            Plan = new BaseRelationalReadPlan
            {
                Id = "test.acquire", SchemaGeneration = 1,
                Sources = [new BaseRelationalReadSource { Id = "subjects", CollectionId = privateCollectionId }],
                Predicate = new BaseRelationalPredicate
                {
                    Kind = FilterNodeKind.Compare, Operator = FilterOperator.Equal,
                    Left = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "subjects", FieldId = "base.recordId" },
                    Right = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = BaseQueryValue.From(id) },
                },
                Projection = [new BaseRelationalReadProjection
                {
                    FieldId = "reference",
                    Operand = new BaseRelationalOperand
                    {
                        Kind = BaseRelationalOperandKind.SubjectReference, SourceId = "subjects",
                        SubjectContractId = subject.Id, SubjectContractVersion = subject.Version,
                    },
                }],
                Parameters = [],
                Budgets = new BaseRelationalReadBudgets { MaxResultRows = 1, MaxResultBytes = 4096, MaxOperations = 16 },
            },
            ParameterValues = [],
            SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "subjects", CollectionId = privateCollectionId }],
            Operation = Operation(BaseOperationKind.SubjectAcquire, privateCollectionId),
            AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1),
            MaxResultRows = 1, MaxResultBytes = 4096,
        });
        Assert.True(result.IsSuccess(), result.Error?.Code);
        QueryValue value = Assert.Single(Assert.Single(result.Value!.Result.Rows).Fields).Value;
        return JsonSerializer.Deserialize<JsonElement>(
            $$"""{"subjectId":"{{value.SubjectId}}","authorityEpoch":"{{value.SubjectAuthorityEpoch}}","incarnation":"{{value.SubjectIncarnation}}"}""");
    }

    private static RecordPayload Payload(params (string Name, object Value)[] fields) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = fields.ToDictionary(static value => value.Name, static value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal),
    };

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Service,
        SubjectKind = AccessSubjectKind.ServicePrincipal,
        SubjectId = "service-1",
        CurrentTenantId = "tenant-a",
    };

    private static OperationContext Operation(BaseOperationKind kind, string collectionId) => new()
    {
        ApplicationId = "test.application", Operation = kind, CollectionId = collectionId,
        Audience = HPDBaseEndpointAudience.Application, Mode = OperationMode.System,
    };

    private sealed class CollectionsContributor(CollectionDefinition[] collections) : IBaseDescriptorContributor
    {
        public string Id => "l45.collections";
        public void Contribute(IBaseDescriptorContributionBuilder builder)
        {
            foreach (CollectionDefinition collection in collections) builder.AddCollection(collection);
        }
    }

    private sealed class GrantingPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PolicyDecision.Allow());
        }
    }

    private sealed class HangingLifecycleStore(IBaseSubjectLifecycleStore inner)
        : FakeRecordStore("l47-hanging"), IBaseSubjectLifecycleStore
    {
        private readonly TaskCompletionSource<OperationResult<BaseSubjectLifecycleProviderPage>> _late =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private BaseSubjectLifecycleProviderReadRequest? _heldRequest;
        private int _released;
        public int ReadCalls { get; private set; }

        public ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> ReadAsync(
            BaseSubjectLifecycleProviderReadRequest request,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            if (Volatile.Read(ref _released) != 0)
                return inner.ReadAsync(request, cancellationToken);
            _heldRequest = request;
            return new(_late.Task);
        }

        public async Task ReleaseAsync()
        {
            BaseSubjectLifecycleProviderReadRequest request = _heldRequest ?? throw new InvalidOperationException();
            OperationResult<BaseSubjectLifecycleProviderPage> result = await inner.ReadAsync(request, CancellationToken.None);
            Volatile.Write(ref _released, 1);
            _late.TrySetResult(result);
            await Task.Yield();
        }

        public ValueTask<RecordMutationExecutionResult> AdvanceCheckpointAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest execution, CancellationToken cancellationToken = default) =>
            inner.AdvanceCheckpointAsync(processor, execution, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileAsync(BaseSubjectLifecycleProviderReconciliationRequest request, CancellationToken cancellationToken = default) =>
            inner.ReconcileAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderInspection>> InspectAsync(BaseSubjectLifecycleProviderInspectionRequest request, CancellationToken cancellationToken = default) =>
            inner.InspectAsync(request, cancellationToken);
    }

    private sealed class ReconcilingLifecycleStore(IBaseSubjectLifecycleStore inner, BaseOpaqueTokenProtector tokens)
        : FakeRecordStore("l47-reconciling"), IBaseSubjectLifecycleStore
    {
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileAsync(
            BaseSubjectLifecycleProviderReconciliationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BaseProtectedSubjectScope scope = new BaseSubjectScopeProtector(tokens).Protect(request.Scope);
            BaseCurrentSubjectLifecycle current = new()
            {
                SubjectId = BaseSubjectId.Create("reconciled-user", BaseSubjectIdKind.OrdinalString),
                AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)3, 16).ToArray()),
                Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)4, 24).ToArray()),
                State = BaseSubjectLifecycleState.Active, SubjectSequence = 1,
            };
            long bytes = 96L + System.Text.Encoding.UTF8.GetByteCount(current.SubjectId.Value);
            return ValueTask.FromResult(OperationResults.Ok(new BaseSubjectLifecycleProviderReconciliationPage
            {
                Scope = scope, Subjects = [current], NextSubjectId = null, CapturedHighWater = null,
                ProjectionGeneration = request.ProjectionGeneration, Intervals = [],
                Accounting = new() { RowsSought = 1, RowsHydrated = 1, ResultBytes = bytes, TransientBytes = bytes },
            }));
        }
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> ReadAsync(BaseSubjectLifecycleProviderReadRequest request, CancellationToken cancellationToken = default) => inner.ReadAsync(request, cancellationToken);
        public ValueTask<RecordMutationExecutionResult> AdvanceCheckpointAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest execution, CancellationToken cancellationToken = default) => inner.AdvanceCheckpointAsync(processor, execution, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderInspection>> InspectAsync(BaseSubjectLifecycleProviderInspectionRequest request, CancellationToken cancellationToken = default) => inner.InspectAsync(request, cancellationToken);
    }

    private sealed class TransformingLifecycleStore(
        IBaseSubjectLifecycleStore inner,
        Func<BaseSubjectLifecycleProviderPage, BaseSubjectLifecycleProviderPage> transform)
        : FakeRecordStore("l47-transforming"), IBaseSubjectLifecycleStore
    {
        public async ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> ReadAsync(BaseSubjectLifecycleProviderReadRequest request, CancellationToken cancellationToken = default)
        {
            OperationResult<BaseSubjectLifecycleProviderPage> result = await inner.ReadAsync(request, cancellationToken);
            return !result.IsSuccess() || result.Value is null ? result : OperationResults.Ok(transform(result.Value));
        }
        public ValueTask<RecordMutationExecutionResult> AdvanceCheckpointAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest execution, CancellationToken cancellationToken = default) => inner.AdvanceCheckpointAsync(processor, execution, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileAsync(BaseSubjectLifecycleProviderReconciliationRequest request, CancellationToken cancellationToken = default) => inner.ReconcileAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectLifecycleProviderInspection>> InspectAsync(BaseSubjectLifecycleProviderInspectionRequest request, CancellationToken cancellationToken = default) => inner.InspectAsync(request, cancellationToken);
    }

    private sealed class SingleStoreRegistry(string collectionId, IRecordStore store) : IRecordStoreRegistry
    {
        public void Add(RecordStoreRegistration registration) => throw new NotSupportedException();
        public IRecordStore? GetStore(string storeId) => store;
        public IRecordStore? GetStoreForCollection(string requestedCollectionId) =>
            string.Equals(collectionId, requestedCollectionId, StringComparison.Ordinal) ? store : null;
        public RecordStoreRegistration? GetRegistration(string storeId) => null;
        public RecordStoreRegistration? GetRegistrationForCollection(string requestedCollectionId) => null;
        public RecordStoreRegistration[] GetRegistrations() => [];
    }

    private sealed class MissingGrantPolicy : IBasePolicyOrchestrator
    {
        public ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadAsync(BasePolicyRequest request, CancellationToken cancellationToken = default) =>
            Allow(cancellationToken);
        public ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(BasePolicyRequest request, CancellationToken cancellationToken = default) =>
            Allow(cancellationToken);
        private static ValueTask<OperationResult<BasePolicyEvaluation>> Allow(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(OperationResults.Ok(new BasePolicyEvaluation
            {
                Decision = PolicyDecision.Allow() with { Audit = new PolicyAuditInfo { MatchedGrantIds = ["different.grant"] } },
            }));
        }
    }

    private sealed class ThrowingResolver : IBaseStoreExecutionResolver
    {
        public OperationResult<BaseResolvedMutationStore> Resolve(CollectionDefinition collection, BaseRecordMutationKind operation, OperationContext context) =>
            throw new InvalidOperationException("Provider resolution occurred before subject authorization.");
    }

    private sealed class LifecycleTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan elapsed) => now = checked(now + elapsed);
    }

    private sealed class SubjectFixture(ServiceProvider services, InMemoryRecordStore store, BaseExportedSubjectDefinition subject, BaseGeneratedSubjectRegistration registration) : IAsyncDisposable
    {
        internal ServiceProvider Services { get; } = services;
        internal InMemoryRecordStore Store { get; } = store;
        internal BaseGeneratedSubjectRegistration Registration { get; } = registration;
        internal async ValueTask<JsonElement> AcquireAsync(string id)
        {
            var operand = new BaseRelationalOperand
            {
                Kind = BaseRelationalOperandKind.SubjectReference, SourceId = "subjects",
                SubjectContractId = subject.Id, SubjectContractVersion = subject.Version,
            };
            OperationResult<BaseRelationalReadExecutionResult> result = await Store.ExecuteReadAsync(new BaseRelationalReadExecutionRequest
            {
                Plan = new BaseRelationalReadPlan
                {
                    Id = "test.acquire", SchemaGeneration = 1,
                    Sources = [new BaseRelationalReadSource { Id = "subjects", CollectionId = Private.Id }],
                    Predicate = new BaseRelationalPredicate
                    {
                        Kind = FilterNodeKind.Compare, Operator = FilterOperator.Equal,
                        Left = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.RecordId, SourceId = "subjects", FieldId = "base.recordId" },
                        Right = new BaseRelationalOperand { Kind = BaseRelationalOperandKind.Literal, Literal = BaseQueryValue.From(id) },
                    },
                    Projection = [new BaseRelationalReadProjection { FieldId = "reference", Operand = operand }],
                    Parameters = [],
                    Budgets = new BaseRelationalReadBudgets { MaxResultRows = 1, MaxResultBytes = 4096, MaxOperations = 16 },
                },
                ParameterValues = [], SourcePolicies = [new BaseRelationalReadSourcePolicy { SourceId = "subjects", CollectionId = Private.Id }],
                Operation = Operation(BaseOperationKind.SubjectAcquire, Private.Id),
                AcquisitionTimeout = TimeSpan.FromSeconds(1), ExecutionTimeout = TimeSpan.FromSeconds(1), MaxResultRows = 1, MaxResultBytes = 4096,
            });
            Assert.True(result.IsSuccess(), result.Error?.Code);
            QueryValue value = Assert.Single(Assert.Single(result.Value!.Result.Rows).Fields).Value;
            return JsonSerializer.Deserialize<JsonElement>($$"""{"subjectId":"{{value.SubjectId}}","authorityEpoch":"{{value.SubjectAuthorityEpoch}}","incarnation":"{{value.SubjectIncarnation}}"}""");
        }
        public ValueTask DisposeAsync()
        {
            Services.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

[BaseCollection("l45.private-users", typeof(L45SqliteJsonContext), SystemOwnerModuleId = "example.auth")]
internal sealed partial record L45SqlitePrivateUser
{
    [BaseField("user.active")]
    public required bool Active { get; init; }

    [BaseField("user.tombstoned")]
    public required bool Tombstoned { get; init; }

    [BaseField("user.tenant")]
    public required string Tenant { get; init; }
}

[BaseExportedSubject("example.sqlite-user", OwningModuleId = "example.auth",
    PrivateRecordType = typeof(L45SqlitePrivateUser), AcquisitionGrantId = "example.user.acquire",
    ValidationGrantId = "example.user.validate", AdministrationGrantId = "example.user.admin", ValidationPlanId = "example.sqlite-user.validate.v1",
    Scope = BaseSubjectScopeKind.Tenant, ActiveFieldId = "user.active", TombstoneFieldId = "user.tombstoned", ScopeFieldId = "user.tenant")]
internal sealed partial class L45SqliteUserSubject;

[BaseCollection("l45.profiles", typeof(L45SqliteJsonContext))]
internal sealed partial record L45SqliteProfile
{
    [BaseField("profile.owner")]
    [BaseSubjectReference(typeof(L45SqliteUserSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
    public required BaseSubjectReference<L45SqliteUserSubject> Owner { get; init; }
}

[BaseRead("example.sqlite-user.acquire", typeof(L45SqliteJsonContext),
    SourceAuthority = BaseRegisteredReadSourceAuthority.System,
    Disclosure = BaseRegisteredReadDisclosure.ConfidentialProjection,
    RequiredGrantId = "example.user.acquire",
    SystemSourceIds = ["l45.private-users"])]
internal sealed partial record L45AcquireSqliteUser
{
    [BaseReadParameter("example.sqlite-user.acquire.user-id")]
    public required BaseRecordId<L45SqlitePrivateUser> UserId { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("example.sqlite-user.acquire.reference")]
        public required BaseSubjectReference<L45SqliteUserSubject> Reference { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<L45AcquireSqliteUser, Row> read)
    {
        read.From(L45SqlitePrivateUser.Collection, "users", out BaseReadSource<L45SqlitePrivateUser> user)
            .Where(user.RecordId.Equal(read.Parameter(Parameters.UserId)))
            .ProjectSubjectReference(Row.Fields.Reference, user, L45SqliteUserSubject.HPDBaseSubjectRegistration);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(L45SqlitePrivateUser))]
[JsonSerializable(typeof(L45SqliteProfile))]
[JsonSerializable(typeof(L45AcquireSqliteUser))]
[JsonSerializable(typeof(L45AcquireSqliteUser.Row), TypeInfoPropertyName = "L45AcquireSqliteUserRow")]
internal sealed partial class L45SqliteJsonContext : JsonSerializerContext;
