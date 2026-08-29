using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base.Tests.Subjects;

#pragma warning disable HPDBASE0461

public sealed class L48SubjectRetirementTests
{
    [Fact]
    public void Required_evidence_expiry_intersects_checkpoint_lag_deadline_and_absolute_ceiling()
    {
        DateTimeOffset issued = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.Equal(issued.AddDays(1), DefaultBaseSubjectRetirementRuntime.RequiredEvidenceExpiry(issued, TimeSpan.FromDays(1), issued.AddDays(7)));
        Assert.Equal(issued.AddHours(12), DefaultBaseSubjectRetirementRuntime.RequiredEvidenceExpiry(issued, TimeSpan.FromDays(1), issued.AddHours(12)));
        Assert.Equal(issued.AddDays(30), DefaultBaseSubjectRetirementRuntime.RequiredEvidenceExpiry(issued, TimeSpan.FromDays(60), issued.AddDays(45)));
    }
    [Fact]
    public void Required_participation_requires_exact_bilateral_graph_agreement()
    {
        (BaseSubjectLifecycleRegistry lifecycle, BaseSubjectRetirementConsumerDefinition consumer, BaseSubjectRetirementPolicy policy) = Graph();
        BaseSubjectRetirementRegistry installed = new([consumer], [policy], lifecycle);
        Assert.Single(installed.Consumers);
        Assert.Single(installed.Policies);

        BaseSubjectRetirementPolicy substituted = policy with
        {
            AcceptedConsumers = [policy.AcceptedConsumers[0] with { RetirementProfileVersion = 2 }],
        };
        Assert.Equal(BaseSubjectRetirementErrorCodes.RegistrationConflict,
            Assert.Throws<InvalidOperationException>(() => new BaseSubjectRetirementRegistry([consumer], [substituted], lifecycle)).Message);
    }

    [Fact]
    public void Barrier_checksum_binds_authority_epoch_and_every_acknowledgement()
    {
        BaseSubjectRetirementBarrier barrier = Barrier();
        string initial = BaseSubjectRetirementRegistry.BarrierChecksum(barrier, []);
        string acknowledged = BaseSubjectRetirementRegistry.BarrierChecksum(barrier,
            [BaseSubjectRetirementRegistry.AcknowledgementChecksumInput("consumer", 1, Hex('a'), 2, BaseSubjectAcknowledgementDisposition.Completed, 7)]);
        string restored = BaseSubjectRetirementRegistry.BarrierChecksum(barrier with
        {
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)2, 16).ToArray()),
        }, []);
        Assert.NotEqual(initial, acknowledged);
        Assert.NotEqual(initial, restored);
    }

    [Fact]
    public void Publication_union_rejects_missing_additional_and_subject_bearing_restore_payloads()
    {
        BaseSubjectRetirementPublicationFact restore = BaseSubjectRetirementRegistry.SealPublication(new()
        {
            Position = new BaseSubjectRetirementPosition(1), Kind = BaseSubjectRetirementPublicationKind.RestoreTransformed,
            Restore = new BaseSubjectRetirementRestorePublication
            {
                ContractId = "example.subject", ContractVersion = 1, RestoreEpoch = 2,
                PreviousControlGeneration = 4, PublishedControlGeneration = 5,
                TransformedBarrierCount = 1, TransformedAcknowledgementCount = 1,
                TransformationChecksum = Hex('b'),
            },
        });
        BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow { Scope = null, Fact = restore });
        Assert.Throws<InvalidDataException>(() => BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow
        {
            Scope = new BaseProtectedSubjectScope { Kind = BaseSubjectScopeKind.Tenant, IndexDigest = new byte[32], ProtectedCanonicalValue = [1] },
            Fact = restore,
        }));
        Assert.Throws<InvalidDataException>(() => BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow
        {
            Scope = null, Fact = restore with { Purged = Purged() },
        }));
        Assert.Throws<InvalidDataException>(() => BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow
        {
            Scope = null, Fact = restore with { AuditAction = "base.subjectRetirement.subject.purged" },
        }));
        Assert.Throws<InvalidDataException>(() => BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow
        {
            Scope = null, Fact = restore with { InvalidationEventId = "subject-retirement:2" },
        }));
    }

    [Fact]
    public void Shared_maintenance_receipt_is_the_only_permitted_dual_payload_shape()
    {
        BaseSubjectLifecycleMaintenanceResult lifecycle = new()
        {
            Kind = BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection, ExaminedCount = 2, ChangedCount = 2,
            CanonicalBytes = 8, RollingChecksum = Hex('c'), DeliveryEpoch = 2, ProjectionGeneration = 2, Duplicate = false,
        };
        BaseSubjectRetirementMaintenanceResult retirement = new()
        {
            Kind = BaseSubjectRetirementMaintenanceKind.RotateScopeProtection,
            Outcome = BaseSubjectRetirementMutationOutcome.Applied, ExaminedCount = 1, ChangedCount = 1,
            CanonicalBytes = 8, RollingChecksum = Hex('c'), PublishedBarrierControlGeneration = 2,
        };
        BaseAtomicReceiptResult result = new()
        {
            Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance, Mutations = [], SubjectLifecycleMaintenance = lifecycle,
            SubjectRetirement = new BaseSubjectRetirementReceiptResult { Operation = BaseSubjectRetirementReceiptOperation.Maintenance, Maintenance = retirement },
        };
        BaseAtomicReceiptResult owned = BaseAtomicReceiptWire.From(result).Materialize();
        Assert.NotNull(owned.SubjectRetirement?.Maintenance);
        Assert.Throws<InvalidOperationException>(() => BaseAtomicReceiptWire.From(result with
        {
            SubjectRetirement = new BaseSubjectRetirementReceiptResult
            {
                Operation = BaseSubjectRetirementReceiptOperation.Timeout,
                Timeout = new BaseSubjectRetirementTimeoutResult { Outcome = BaseSubjectRetirementMutationOutcome.Applied, State = BaseSubjectRetirementBarrierState.TimedOut, Generation = 2, BarrierChecksum = Hex('d') },
            },
        }));
    }

    [Fact]
    public void Subject_tombstone_receipt_round_trips_exact_owned_transition_evidence()
    {
        DateTimeOffset tombstonedAt = new(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);
        BaseAtomicReceiptResult receipt = new()
        {
            Kind = BaseAtomicReceiptResultKind.SubjectTombstone,
            Mutations = [],
            SubjectTombstone = new BaseAtomicSubjectTombstoneReceiptResult
            {
                SubjectContractId = "example.subject",
                SubjectContractVersion = 1,
                Fact = new BaseOwnedSubjectLifecycleFact
                {
                    CommitPosition = new BaseMutationJournalPosition(7),
                    ContractId = "example.subject",
                    ContractVersion = 1,
                    ContractChecksum = Hex('a'),
                    Scope = new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Tenant, Value = "tenant-a" },
                    SubjectId = BaseSubjectId.Create("subject-a", BaseSubjectIdKind.OrdinalString),
                    AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)2, 16).ToArray()),
                    Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)3, 24).ToArray()),
                    SubjectSequence = 2,
                    ContractStateGeneration = 3,
                    DeliveryEpoch = 4,
                    Kind = BaseSubjectLifecycleFactKind.Transitioned,
                    PreviousState = BaseSubjectLifecycleState.Active,
                    CurrentState = BaseSubjectLifecycleState.Tombstoned,
                },
                PrivateRevision = new RevisionToken("revision-2"),
                TombstonedAt = tombstonedAt,
            },
        };

        BaseAtomicReceiptResult restored = BaseAtomicReceiptWire.From(receipt).Materialize();

        Assert.Equal(BaseAtomicReceiptResultKind.SubjectTombstone, restored.Kind);
        Assert.Equal("revision-2", restored.SubjectTombstone!.PrivateRevision.Value);
        Assert.Equal(tombstonedAt, restored.SubjectTombstone.TombstonedAt);
        Assert.Equal(2, restored.SubjectTombstone.Fact.SubjectSequence);
        Assert.Equal("tenant-a", restored.SubjectTombstone.Fact.Scope.Value);
        Assert.Throws<InvalidOperationException>(() => BaseAtomicReceiptWire.From(receipt with
        {
            SubjectTombstone = receipt.SubjectTombstone with
            {
                Fact = receipt.SubjectTombstone.Fact with { CurrentState = BaseSubjectLifecycleState.Active },
            },
        }));
    }

    [Fact]
    public void Evidence_token_binds_complete_delivery_authority_key_and_expiry()
    {
        (BaseSubjectLifecycleRegistry lifecycle,BaseSubjectRetirementConsumerDefinition consumer,BaseSubjectRetirementPolicy policy)=Graph();BaseInstalledSubjectRetirementConsumer installed=new BaseSubjectRetirementRegistry([consumer],[policy],lifecycle).Consumers.Single();var clock=new MutableClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));using var tokens=new BaseOpaqueTokenProtector(Microsoft.Extensions.Options.Options.Create(new HPDBaseTokenProtectionOptions{ActiveKey=new(){Id=7,Key=Enumerable.Repeat((byte)4,32).ToArray(),IssueNotBefore=DateTimeOffset.UnixEpoch}}),clock);var codec=new BaseSubjectRetirementEvidenceCodec(tokens,clock);var scope=new BaseOwnedSubjectScopeEvidence{Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"};var subject=BaseSubjectId.Create("subject-a",BaseSubjectIdKind.OrdinalString);var epoch=new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)2,16).ToArray());var incarnation=new BaseSubjectIncarnation(Enumerable.Repeat((byte)3,24).ToArray());var boundary=new BaseSubjectLifecycleOrderingBoundary{CommitPosition=new(11),SubjectId=subject,AuthorityEpoch=epoch,Incarnation=incarnation,SubjectSequence=4};var payload=new BaseSubjectRetirementEvidencePayload(BaseSubjectRetirementParticipation.RequiredBeforePurge,"consumer",1,installed.Checksum,"example.subject",1,Hex('1'),"store-a",2,3,4,5,boundary,subject,epoch,incarnation,4,SHA256.HashData("fact"u8),SHA256.HashData("membership"u8),SHA256.HashData("grant"u8),3,7,new(){Generation=2,Checksum=Hex('a')},clock.GetUtcNow(),clock.GetUtcNow().AddHours(1));byte[] binding=BaseSubjectRetirementEvidenceCodec.Binding("app-a",installed,scope);byte[] encoded=codec.Protect(payload,binding);Assert.True(codec.TryRead(encoded,BaseSubjectRetirementParticipation.RequiredBeforePurge,binding,BaseSubjectIdKind.OrdinalString,out BaseSubjectRetirementEvidencePayload? decoded));Assert.Equal(payload.StoreInstanceId,decoded!.StoreInstanceId);Assert.Equal(boundary,decoded.OrderingBoundary);Assert.False(codec.TryRead(encoded,BaseSubjectRetirementParticipation.RequiredBeforePurge,SHA256.HashData("wrong"u8),BaseSubjectIdKind.OrdinalString,out _));clock.Advance(TimeSpan.FromHours(1));Assert.False(codec.TryRead(encoded,BaseSubjectRetirementParticipation.RequiredBeforePurge,binding,BaseSubjectIdKind.OrdinalString,out _));
    }

    [Fact]
    public void Hostile_barrier_page_scope_checksum_order_and_accounting_fail_closed()
    {
        BaseGeneratedSubjectRegistration contract=Subject();using var tokens=new BaseOpaqueTokenProtector(Microsoft.Extensions.Options.Options.Create(new HPDBaseTokenProtectionOptions{ActiveKey=new(){Id=7,Key=Enumerable.Repeat((byte)4,32).ToArray(),IssueNotBefore=DateTimeOffset.UnixEpoch}}));var scopes=new BaseSubjectScopeProtector(tokens);var owned=new BaseOwnedSubjectScopeEvidence{Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"};BaseProtectedSubjectScope scope=scopes.Protect(owned,7);BaseSubjectRetirementBarrier barrier=Barrier();barrier=barrier with{ContractId=contract.Definition.Id,ContractVersion=contract.Definition.Version,BarrierChecksum=BaseSubjectRetirementRegistry.BarrierChecksum(barrier with{ContractId=contract.Definition.Id,ContractVersion=contract.Definition.Version},[])};long bytes=System.Text.Encoding.UTF8.GetByteCount($"{barrier.ContractId}\0{barrier.ContractVersion}\0{barrier.SubjectId.Value}\0{barrier.AuthorityEpoch.ToBase64Url()}\0{barrier.Incarnation.ToBase64Url()}\0{barrier.TombstoneSequence}\0{barrier.RequiredConsumerSetChecksum}\0{barrier.CreatedAtUtc.UtcTicks}\0{barrier.DeadlineUtc.UtcTicks}\0{(int)barrier.State}\0{barrier.Generation}\0{barrier.BarrierChecksum}");var row=new BaseSubjectRetirementBarrierRow{Scope=scope,Barrier=barrier,AcknowledgementChecksumInputs=[]};var key=new BaseSubjectRetirementBarrierKey{ScopeKind=scope.Kind,ScopeIndexDigest=scope.IndexDigest,ContractId=barrier.ContractId,ContractVersion=barrier.ContractVersion,SubjectId=barrier.SubjectId,AuthorityEpoch=barrier.AuthorityEpoch,Incarnation=barrier.Incarnation};var intervals=BaseSubjectRetirementReadIntervals.Create(contract.Definition.Id,contract.Definition.Version,null,scope,null,key);long intervalBytes=intervals.Sum(i=>(long)i.LowerInclusive.Length+i.UpperInclusive.Length);var page=new BaseSubjectRetirementBarrierPage{Barriers=[row],Next=null,CapturedBarrierGeneration=1,Intervals=intervals,Accounting=new(){BarrierRows=1,AcknowledgementRows=0,ResultBytes=bytes,EvidenceBytes=intervalBytes,TransientBytes=checked(bytes+intervalBytes)}};Assert.True(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page,contract,null,null,4,owned));Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page with{Intervals=[intervals[0] with{LogicalAccessPathId="foreign"}]},contract,null,null,4,owned));Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page with{Intervals=[intervals[0] with{UpperInclusive=[1]}]},contract,null,null,4,owned));Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page with{Barriers=[row with{Scope=scope with{IndexDigest=SHA256.HashData("foreign"u8)}}]},contract,null,null,4,owned));Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page with{Barriers=[row with{Barrier=barrier with{BarrierChecksum=Hex('e')}}]},contract,null,null,4,owned));Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page with{Accounting=page.Accounting with{EvidenceBytes=checked(intervalBytes+1),TransientBytes=checked(bytes+intervalBytes+1)}},contract,null,null,4,owned));Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateBarrierPage(scopes,page with{Next=new(){ScopeKind=scope.Kind,ScopeIndexDigest=scope.IndexDigest,ContractId=barrier.ContractId,ContractVersion=barrier.ContractVersion,SubjectId=BaseSubjectId.Create("other",BaseSubjectIdKind.OrdinalString),AuthorityEpoch=barrier.AuthorityEpoch,Incarnation=barrier.Incarnation}},contract,null,null,4,owned));
    }

    [Fact]
    public void Hostile_retirement_inspection_identity_scope_exclusivity_and_accounting_fail_closed()
    {
        using var tokens=new BaseOpaqueTokenProtector(Microsoft.Extensions.Options.Options.Create(new HPDBaseTokenProtectionOptions{ActiveKey=new(){Id=7,Key=Enumerable.Repeat((byte)4,32).ToArray(),IssueNotBefore=DateTimeOffset.UnixEpoch}}));
        var scopes=new BaseSubjectScopeProtector(tokens);var owned=new BaseOwnedSubjectScopeEvidence{Kind=BaseSubjectScopeKind.Tenant,Value="tenant-a"};BaseProtectedSubjectScope protectedScope=scopes.Protect(owned,7);
        BaseSubjectRetirementBarrier barrier=Barrier();barrier=barrier with{BarrierChecksum=BaseSubjectRetirementRegistry.BarrierChecksum(barrier,[])};
        long bytes=System.Text.Encoding.UTF8.GetByteCount($"{barrier.ContractId}\0{barrier.ContractVersion}\0{barrier.SubjectId.Value}\0{barrier.AuthorityEpoch.ToBase64Url()}\0{barrier.Incarnation.ToBase64Url()}\0{barrier.TombstoneSequence}\0{barrier.RequiredConsumerSetChecksum}\0{barrier.CreatedAtUtc.UtcTicks}\0{barrier.DeadlineUtc.UtcTicks}\0{(int)barrier.State}\0{barrier.Generation}\0{barrier.BarrierChecksum}");
        var request=new BaseSubjectRetirementInspectionRequest{ContractId=barrier.ContractId,ContractVersion=barrier.ContractVersion,SubjectId=barrier.SubjectId,AuthorityEpoch=barrier.AuthorityEpoch,Incarnation=barrier.Incarnation,ScopeAuthority=new(){Mode=BaseSubjectScopeQueryMode.ExactScope,ExactScope=owned,InstalledAuthorityDigest=Hex('a')},IncludeTerminalSummary=true,MaximumResultBytes=65_536,DeadlineUtc=DateTimeOffset.UtcNow.AddMinutes(1)};
        var inspection=new BaseSubjectRetirementInspection{Scope=protectedScope,CurrentBarrier=barrier,TerminalSummary=null,AcknowledgementChecksumInputs=[],Accounting=new(){BarrierRows=1,AcknowledgementRows=0,ResultBytes=bytes,EvidenceBytes=0,TransientBytes=bytes}};
        Assert.True(DefaultBaseSubjectRetirementRuntime.ValidateInspection(scopes,inspection,request,owned));
        Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateInspection(scopes,inspection with{Scope=protectedScope with{IndexDigest=SHA256.HashData("foreign"u8)}},request,owned));
        Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateInspection(scopes,inspection with{CurrentBarrier=barrier with{SubjectId=BaseSubjectId.Create("other",BaseSubjectIdKind.OrdinalString)}},request,owned));
        Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateInspection(scopes,inspection with{TerminalSummary=new(){ContractId=barrier.ContractId,ContractVersion=barrier.ContractVersion,SubjectId=barrier.SubjectId,AuthorityEpoch=barrier.AuthorityEpoch,Incarnation=barrier.Incarnation,TombstoneSequence=barrier.TombstoneSequence,RetiredPosition=new(1),PurgedAtUtc=DateTimeOffset.UtcNow,TerminalReceiptChecksum=Hex('b')}},request,owned));
        Assert.False(DefaultBaseSubjectRetirementRuntime.ValidateInspection(scopes,inspection with{Accounting=inspection.Accounting with{ResultBytes=bytes+1,TransientBytes=bytes+1}},request,owned));
    }

    [Fact]
    public async Task Noncooperative_provider_work_retains_quarantine_until_late_completion()
    {
        var state=new BaseSubjectRetirementOperationalState();using var slots=new SemaphoreSlim(1,1);var completion=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);await Assert.ThrowsAsync<TimeoutException>(async()=>await DefaultBaseSubjectRetirementRuntime.InvokeProviderAsync<int>(_=>new(completion.Task),TimeSpan.FromMilliseconds(20),CancellationToken.None,slots,state));Assert.Equal(0,state.Active);Assert.Equal(1,state.Quarantined);Assert.Equal(0,slots.CurrentCount);completion.SetResult(7);await SpinWaitAsync(()=>state.Quarantined==0);Assert.Equal(1,slots.CurrentCount);
    }

    [Theory]
    [MemberData(nameof(InsufficientCapabilities))]
    public void Every_retirement_provider_capability_is_intersected_at_readiness(Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability> reduce)
    {
        (BaseSubjectLifecycleRegistry lifecycle,BaseSubjectRetirementConsumerDefinition consumer,BaseSubjectRetirementPolicy policy)=Graph();var registry=new BaseSubjectRetirementRegistry([consumer],[policy],lifecycle);
        Assert.True(BaseSubjectRetirementCapabilityContract.Supports(registry,BaseSubjectRetirementProviderCapabilities.BuiltIn));
        Assert.False(BaseSubjectRetirementCapabilityContract.Supports(registry,reduce(BaseSubjectRetirementProviderCapabilities.BuiltIn)));
    }

    public static IEnumerable<object[]> InsufficientCapabilities()
    {
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{TransactionalBarrierSupported=false})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{TransactionalFinalPurgeSupported=false})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumRequiredConsumersPerContract=0})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumAcknowledgementsPerCommit=0})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumPendingBarriers=0})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumCoordinationWindow=TimeSpan.FromMinutes(59)})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumAdministrationPageSize=255})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumResultBytes=1_048_575})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumRetirementProjectionsPerCommit=255})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumBarrierReadsPerCommit=255})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumAcknowledgementReadsPerCommit=0})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumPublicationsPerCommit=255})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumEvidenceBytes=1_048_575})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumPublicationBytes=1_048_575})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumTransientBytes=31_999_999})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumAcquisitionTimeout=TimeSpan.FromMilliseconds(4_999)})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumTransactionTimeout=TimeSpan.FromMilliseconds(29_999)})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumCommitCompletionTimeout=TimeSpan.FromMilliseconds(29_999)})];
        yield return [new Func<BaseSubjectRetirementCapability,BaseSubjectRetirementCapability>(value=>value with{MaximumReceiptResolutionTimeout=TimeSpan.FromMilliseconds(29_999)})];
    }

    private static (BaseSubjectLifecycleRegistry Lifecycle, BaseSubjectRetirementConsumerDefinition Consumer, BaseSubjectRetirementPolicy Policy) Graph()
    {
        BaseGeneratedSubjectRegistration subject = BaseGeneratedSubjects.Register<object>(new BaseExportedSubjectDefinition
        {
            Id = "example.subject", Version = 1, OwningModuleId = "example.exporter", Scope = BaseSubjectScopeKind.Tenant,
            SubjectIdKind = BaseSubjectIdKind.OrdinalString, MaximumSubjectIdUtf8Bytes = 128,
            TombstoneFieldId="tombstoned",TombstoneMetadata=new(){Instant=new(){Kind=BaseSubjectTombstoneMetadataBindingKind.NotStored},Sequence=new(){Kind=BaseSubjectTombstoneMetadataBindingKind.NotStored}},FinalRetirementExecutionMode=BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded,SupportsCoordinatedRetirement=true,Audiences=[HPDBaseEndpointAudience.Application],
            ValidationPlan = new BaseSubjectValidationPlanDefinition { Id="example.subject.validation",Version=1,ContractId = "example.subject", ContractVersion = 1, ContractChecksum = Hex('1'), PrivateCollectionId = "private.subjects", SubjectId=BaseSubjectIdBinding.RecordId,Active=new(){Kind=BaseSubjectActiveBindingKind.RequiredBooleanField,FieldId="active",ActiveValue=true},Scope=new(){Kind=BaseSubjectScopeBindingKind.RequiredTenantField,FieldId="tenant"},Access=BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,Limits=BaseSubjectValidationLimits.Default },
            AcquisitionGrantId = "subject.acquire", ValidationGrantId = "subject.validate", AdministrationGrantId = "subject.admin",
        });
        BaseSubjectLifecycleConsumerDefinition lifecycleDefinition = new()
        {
            Id = "consumer", Version = 1, OwningModuleId = "example.consumer", Audience = BaseSubjectLifecycleConsumerAudience.Service,
            ContractId = "example.subject", ContractVersion = 1, ObservedStates = [BaseSubjectLifecycleState.Tombstoned], DeliveryGrantId = "consumer.read",
            Limits = new BaseSubjectLifecycleConsumerLimits { MaximumFactsPerPage = 16, MaximumResultBytes = 65_536, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(1) },
        };
        var lifecycle = new BaseSubjectLifecycleRegistry([lifecycleDefinition], new BaseSubjectContractRegistry([subject]));
        string lifecycleChecksum = lifecycle.All.Single().Checksum;
        BaseSubjectRetirementConsumerDefinition consumer = new()
        {
            ConsumerId = "consumer", ConsumerVersion = 1, OwningModuleId = "example.consumer", Audience = BaseSubjectLifecycleConsumerAudience.Service,
            LifecycleConsumerChecksum = lifecycleChecksum, RetirementProfileId = "consumer.retirement", RetirementProfileVersion = 1,
            RetirementProfileChecksum = Hex('2'), Participation = BaseSubjectRetirementParticipation.RequiredBeforePurge,
            AcknowledgementGrantId = "consumer.ack", Limits = new BaseSubjectRetirementConsumerLimits { MaximumAcknowledgementsPerCommit = 16, MaximumAcknowledgementRequestBytes = 65_536, MaximumReceiptBytes = 65_536, AcknowledgementTimeout = TimeSpan.FromSeconds(1), ReceiptResolutionTimeout = TimeSpan.FromSeconds(1) },
        };
        string consumerChecksum = BaseSubjectRetirementRegistry.ConsumerChecksum(BaseSubjectRetirementRegistry.Normalize(consumer));
        BaseAcceptedRetirementConsumer accepted = new()
        {
            ConsumerId = consumer.ConsumerId, ConsumerVersion = consumer.ConsumerVersion, OwningModuleId = consumer.OwningModuleId, Audience = consumer.Audience,
            LifecycleConsumerChecksum = consumer.LifecycleConsumerChecksum, RetirementProfileId = consumer.RetirementProfileId, RetirementProfileVersion = consumer.RetirementProfileVersion,
            RetirementProfileChecksum = consumer.RetirementProfileChecksum, Participation = consumer.Participation, AcknowledgementGrantId = consumer.AcknowledgementGrantId,
            Limits = consumer.Limits, RetirementConsumerChecksum = consumerChecksum,
        };
        BaseSubjectRetirementPolicy policy = new()
        {
            ContractId = "example.subject", ContractVersion = 1, AcceptedConsumers = [accepted], CoordinationWindow = TimeSpan.FromHours(1),
            TimeoutBehavior = BaseSubjectRetirementTimeoutBehavior.Quarantine, PurgeRetention = new BaseSubjectPurgeRetentionPolicy { MinimumTombstoneAge = TimeSpan.Zero }, FinalPurgeExecutionMode = BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded, PolicyChecksum = Hex('0'),
        };
        policy = policy with { PolicyChecksum = BaseSubjectRetirementRegistry.PolicyChecksum(policy with { PolicyChecksum = string.Empty }) };
        return (lifecycle, consumer, policy);
    }

    private static BaseGeneratedSubjectRegistration Subject()=>BaseGeneratedSubjects.Register<object>(new BaseExportedSubjectDefinition{Id="example.subject",Version=1,OwningModuleId="example.exporter",Scope=BaseSubjectScopeKind.Tenant,SubjectIdKind=BaseSubjectIdKind.OrdinalString,MaximumSubjectIdUtf8Bytes=128,TombstoneFieldId="tombstoned",TombstoneMetadata=new(){Instant=new(){Kind=BaseSubjectTombstoneMetadataBindingKind.NotStored},Sequence=new(){Kind=BaseSubjectTombstoneMetadataBindingKind.NotStored}},FinalRetirementExecutionMode=BaseSubjectFinalExecutionMode.OrdinaryOrActivationGuarded,SupportsCoordinatedRetirement=true,Audiences=[HPDBaseEndpointAudience.Application],ValidationPlan=new(){Id="example.subject.validation",Version=1,ContractId="example.subject",ContractVersion=1,ContractChecksum=Hex('1'),PrivateCollectionId="private.subjects",SubjectId=BaseSubjectIdBinding.RecordId,Active=new(){Kind=BaseSubjectActiveBindingKind.RequiredBooleanField,FieldId="active",ActiveValue=true},Scope=new(){Kind=BaseSubjectScopeBindingKind.RequiredTenantField,FieldId="tenant"},Access=BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,Limits=BaseSubjectValidationLimits.Default},AcquisitionGrantId="subject.acquire",ValidationGrantId="subject.validate",AdministrationGrantId="subject.admin"});

    private static BaseSubjectRetirementBarrier Barrier() => new()
    {
        ContractId = "example.subject", ContractVersion = 1, SubjectId = BaseSubjectId.Create("subject-1", BaseSubjectIdKind.OrdinalString),
        AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)1, 16).ToArray()), Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)1, 24).ToArray()),
        TombstoneSequence = 2, RequiredConsumerSetChecksum = Hex('e'), CreatedAtUtc = DateTimeOffset.UnixEpoch, DeadlineUtc = DateTimeOffset.UnixEpoch.AddHours(1),
        State = BaseSubjectRetirementBarrierState.Pending, Generation = 1, BarrierChecksum = Hex('f'),
    };

    private static BaseSubjectPurgedPublication Purged() => new()
    {
        ContractId = "example.subject", ContractVersion = 1, SubjectId = BaseSubjectId.Create("subject-1", BaseSubjectIdKind.OrdinalString),
        AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)1,16).ToArray()), Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)1,24).ToArray()), TombstoneSequence = 2,
        FinalBarrierGeneration = 1, FinalBarrierChecksum = Hex('a'), TerminalReceiptChecksum = Hex('b'), RetiredLifecyclePosition = new BaseMutationJournalPosition(1),
    };

    private static string Hex(char value) => new(value, 64);
    private sealed class MutableClock(DateTimeOffset now):TimeProvider{private DateTimeOffset _now=now;public override DateTimeOffset GetUtcNow()=>_now;internal void Advance(TimeSpan value)=>_now=_now.Add(value);}
    private static async Task SpinWaitAsync(Func<bool> predicate){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));while(!predicate()){await Task.Delay(5,timeout.Token);}}
}
