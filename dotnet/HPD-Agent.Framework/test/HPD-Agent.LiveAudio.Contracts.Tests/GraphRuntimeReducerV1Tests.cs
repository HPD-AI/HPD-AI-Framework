using System.Formats.Cbor;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeReducerV1Tests
{
    [Fact]
    public async Task RealReplacementAtomicTransition_FencesPreviouslyActiveRuntime()
    {
        var f=await GraphReplacementAdmissionCoordinatorV1Tests.Fixture.CreateAsync();
        var active=new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active,f.GraphGeneration,f.Source.Fingerprint,
            f.Installation,f.Authority,OperationId.Create(),new JournalPositionV1(f.Session,f.Installation.Sequence+1),
            new JournalPositionV1(f.Session,f.Installation.Sequence+1),null);
        var prepareOperation=OperationId.Create();
        var prepare=new GraphReplacementJournalCommandV1.Prepare(prepareOperation,f.Installation,f.Source.Fingerprint,
            f.Target,f.TargetGrant.CurrentFact,f.Authority,f.Observed,f.Deadline);
        var prepared=Assert.IsType<GraphReplacementAdmissionResultV1.Admitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(f.Journal,f.Request(prepare,f.Authority)));
        var commit=new GraphReplacementJournalCommandV1.Commit(prepareOperation,prepared.Result.Position);
        var committed=Assert.IsType<GraphReplacementAdmissionResultV1.Admitted>(
            await GraphReplacementAdmissionCoordinatorV1.AdmitAsync(f.Journal,f.Request(commit,f.Authority)));
        Assert.NotNull(committed.GraphTransition);
        var operation=OperationId.Create();var retire=new GraphRuntimeCommandV1.Retire(operation,active.LastRuntimeFact,
            active.ActivationFact,GraphRuntimeEffectHashesV1.Retire(f.Session,operation,active.ActivationFact));
        var body=GraphRuntimeCodecsV1.EncodeCommand(retire);
        var payload=GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(f.Session,f.Authority,body));
        var registration=GraphRuntimePayloadRegistrationsV1.Command;
        var proposal=new ProposedAuthorityFactV1(GraphRuntimeFactIdsV1.Command(f.Session,operation,retire.Kind),null,
            OwnerSliceId.S2,registration.Schema,payload,AuthorityPayloadHashV1.Compute(registration.SchemaToken,
                registration.Schema,payload),new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(30));
        var append=Assert.IsType<AppendAuthorityResultV1.Committed>(await f.Journal.AppendAsync(new AppendAuthorityBatchV1(
            f.Session,committed.GraphTransition!.Position.Sequence,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));
        var admitted=Assert.Single(append.Envelopes).Position;
        var verified=Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session));
        Assert.Equal(admitted.Sequence,verified.SnapshotThrough);
        var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(verified);
        Assert.IsType<GraphRuntimeEvaluationV1.GenerationReplaced>(
            GraphRuntimeReducerV1.Evaluate(active,retire,admitted,f.Authority,evidence));
    }

    [Fact]
    public void PreinstallVerifiedCurrent_ClassifiesByCommandWithoutGenerationReplacement()
    {
        var authority=Authority();var replay=new CurrentAuthorityVectorSnapshotV1(Session(),authority.Axes.Select(x=>x.Value),10);
        var fold=new GraphReplacementJournalFoldResultV1.Current(10,replay,null,[]);
        var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(new GraphReplacementSnapshotReadResultV1.Verified(fold,10));
        var operation=OperationId.FromValue(Id(8));var position=Position(3);var generation=((AuthorityAxisValueV1.Graph)authority.Axes.Single().Value).Value;
        var activate=new GraphRuntimeCommandV1.Activate(operation,position,position,Hash(4),generation,Position(2),Hash(7));
        var rejected=Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(null,activate,Position(5),authority,evidence,null));
        Assert.Equal("runtime-activation-proof-invalid",rejected.SafeCode.ToString());
        var retire=new GraphRuntimeCommandV1.Retire(operation,position,position,GraphRuntimeEffectHashesV1.Retire(Session(),operation,position));
        rejected=Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(null,retire,Position(5),authority,evidence));
        Assert.Equal("runtime-not-active",rejected.SafeCode.ToString());
    }

    [Fact]
    public async Task OptionalAxis_ClaimedDriftRejectsWhileUnclaimedDriftAllowsCapability()
    {
        var f=await Fixture.CreateAsync();var operation=OperationId.Create();var install=f.Installation.Position;
        var hash=GraphRuntimeEffectHashesV1.Activate(f.Session,operation,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact);
        var command=new GraphRuntimeCommandV1.Activate(operation,install,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,hash);
        var admitted=await f.AppendCommandAsync(command);
        var verified=Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session));
        var current=Assert.IsType<GraphReplacementJournalFoldResultV1.Current>(verified.Fold);
        var liveActivity=ActivityGenerationId.Create();var replay=new CurrentAuthorityVectorSnapshotV1(f.Session,[new AuthorityAxisValueV1.Graph(f.GraphGeneration),new AuthorityAxisValueV1.Activity(liveActivity)],verified.SnapshotThrough);
        var driftFold=new GraphReplacementJournalFoldResultV1.Current(current.SnapshotThrough,replay,current.State,current.PendingCommands,current.TargetCommandFact,current.TargetResultFact,current.InstallationFact,current.Wire,current.TargetTransitionFact);
        var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(new GraphReplacementSnapshotReadResultV1.Verified(driftFold,verified.SnapshotThrough));
        Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(GraphRuntimeReducerV1.Evaluate(null,command,admitted.Position,f.Authority,evidence,f.Grant));
        var claimed=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(f.GraphGeneration),new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create())]);
        var stale=Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(null,command,admitted.Position,claimed,evidence,f.Grant));
        Assert.Equal("authority-vector-stale",stale.SafeCode.ToString());
        var active=new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active,f.GraphGeneration,f.Plan.Fingerprint,f.Grant.CurrentFact,f.Authority,OperationId.Create(),new JournalPositionV1(f.Session,4),new JournalPositionV1(f.Session,4),null);
        var retireOperation=OperationId.Create();var retire=new GraphRuntimeCommandV1.Retire(retireOperation,active.LastRuntimeFact,active.ActivationFact,GraphRuntimeEffectHashesV1.Retire(f.Session,retireOperation,active.ActivationFact));
        Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(GraphRuntimeReducerV1.Evaluate(active,retire,admitted.Position,f.Authority,evidence));
        stale=Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(active,retire,admitted.Position,claimed,evidence));
        Assert.Equal("authority-vector-stale",stale.SafeCode.ToString());
    }

    [Fact]
    public async Task RealJournal_ActivateProofCoversCommandAndResolveThenRetire()
    {
        var f=await Fixture.CreateAsync();var operation=OperationId.Create();var install=f.Installation.Position;
        var before=GraphRuntimeCurrentGraphEvidenceV1.From(Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session)));
        var hash=GraphRuntimeEffectHashesV1.Activate(f.Session,operation,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact);
        var command=new GraphRuntimeCommandV1.Activate(operation,install,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,hash);
        var admitted=await f.AppendCommandAsync(command);
        Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(null,command,admitted.Position,f.Authority,before,f.Grant));
        var verified=Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session));
        var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(verified);
        var evaluated=GraphRuntimeReducerV1.Evaluate(null,command,admitted.Position,f.Authority,evidence,f.Grant);Assert.True(evaluated is GraphRuntimeReducerV1.EffectRequired,evaluated.ToString());var required=(GraphRuntimeReducerV1.EffectRequired)evaluated;
        var activateRequest=Assert.IsType<GraphRuntimeEffectRequestV1.Activate>(GraphRuntimeEffectRequestV1.From(required));
        Assert.Equal(operation,activateRequest.OperationId);Assert.Equal(GraphRuntimeCommandKindV1.Activate,activateRequest.Kind);
        Assert.Equal(hash,activateRequest.RequestHash);Assert.Equal(install,activateRequest.GraphAuthorityFact);
        Assert.Equal(f.Plan.Fingerprint,activateRequest.TopologyFingerprint);Assert.Equal(f.GraphGeneration,activateRequest.GraphGeneration);
        Assert.Equal(f.Grant.CurrentFact,activateRequest.CapacityGrantFact);
        var active=Assert.IsType<GraphRuntimeResolutionV1.Applied>(GraphRuntimeReducerV1.Resolve(required,new GraphRuntimeEffectResolutionV1.Completed(Hash(9)),new JournalPositionV1(f.Session,6))).Snapshot;
        Assert.Equal(GraphRuntimePhaseV1.Active,active.Phase);
        await f.AppendFactAsync(new GraphRuntimeFactV1(admitted.Position,command.ExpectedPredecessor,install,GraphRuntimeOutcomeV1.Activated,active,Hash(9),null),5);
        var retireOperation=OperationId.Create();var retire=new GraphRuntimeCommandV1.Retire(retireOperation,active.LastRuntimeFact,active.ActivationFact,GraphRuntimeEffectHashesV1.Retire(f.Session,retireOperation,active.ActivationFact));
        var retireEnvelope=await f.AppendCommandAsync(retire,6);evidence=GraphRuntimeCurrentGraphEvidenceV1.From(Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session)));
        var wrongPredecessor=new GraphRuntimeCommandV1.Retire(retireOperation,new JournalPositionV1(f.Session,5),active.ActivationFact,retire.EffectRequestHash);
        Assert.IsType<GraphRuntimeEvaluationV1.Conflict>(GraphRuntimeReducerV1.Evaluate(active,wrongPredecessor,retireEnvelope.Position,f.Authority,evidence));
        var wrongActive=new GraphRuntimeCommandV1.Retire(retireOperation,active.LastRuntimeFact,new JournalPositionV1(f.Session,5),GraphRuntimeEffectHashesV1.Retire(f.Session,retireOperation,new JournalPositionV1(f.Session,5)));
        Assert.IsType<GraphRuntimeEvaluationV1.Conflict>(GraphRuntimeReducerV1.Evaluate(active,wrongActive,retireEnvelope.Position,f.Authority,evidence));
        var wrongHash=new GraphRuntimeCommandV1.Retire(retireOperation,active.LastRuntimeFact,active.ActivationFact,Hash(11));
        Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(active,wrongHash,retireEnvelope.Position,f.Authority,evidence));
        var retireRequired=Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(GraphRuntimeReducerV1.Evaluate(active,retire,retireEnvelope.Position,f.Authority,evidence));
        var retireRequest=Assert.IsType<GraphRuntimeEffectRequestV1.Retire>(GraphRuntimeEffectRequestV1.From(retireRequired));
        Assert.Equal(retireOperation,retireRequest.OperationId);Assert.Equal(GraphRuntimeCommandKindV1.Retire,retireRequest.Kind);
        Assert.Equal(retire.EffectRequestHash,retireRequest.RequestHash);Assert.Equal(active.ActivationFact,retireRequest.ActiveRuntimeFact);
        var retired=Assert.IsType<GraphRuntimeResolutionV1.Applied>(GraphRuntimeReducerV1.Resolve(retireRequired,new GraphRuntimeEffectResolutionV1.Completed(Hash(10)),new JournalPositionV1(f.Session,8))).Snapshot;
        Assert.Equal(GraphRuntimePhaseV1.Retired,retired.Phase);Assert.Equal(new JournalPositionV1(f.Session,7),retired.Retirement!.RetireCommandFact);
    }

    [Fact]
    public async Task RealJournal_ActivationHostileProofTableFailsClosed()
    {
        var f=await Fixture.CreateAsync();var operation=OperationId.Create();var install=f.Installation.Position;
        GraphRuntimeCommandV1.Activate Command(Hash256 fingerprint,GraphGenerationId generation,JournalPositionV1 grant,Hash256 request)=>new(operation,install,install,fingerprint,generation,grant,request);
        var correctHash=GraphRuntimeEffectHashesV1.Activate(f.Session,operation,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact);
        var exact=Command(f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,correctHash);var admitted=await f.AppendCommandAsync(exact);
        var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session)));
        void ProofInvalid(GraphRuntimeCommandV1.Activate c,CapacityGrantSnapshotV1? g){var rejected=Assert.IsType<GraphRuntimeEvaluationV1.Rejected>(GraphRuntimeReducerV1.Evaluate(null,c,admitted.Position,f.Authority,evidence,g));Assert.Equal("runtime-activation-proof-invalid",rejected.SafeCode.ToString());}
        ProofInvalid(Command(Hash(12),f.GraphGeneration,f.Grant.CurrentFact,correctHash),f.Grant);
        ProofInvalid(Command(f.Plan.Fingerprint,GraphGenerationId.Create(),f.Grant.CurrentFact,correctHash),f.Grant);
        ProofInvalid(Command(f.Plan.Fingerprint,f.GraphGeneration,new JournalPositionV1(f.Session,2),correctHash),f.Grant);
        ProofInvalid(Command(f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,Hash(13)),f.Grant);
        ProofInvalid(exact,null);
        ProofInvalid(exact,GrantLike(f.Grant,CapacityGrantStateV1.Settled,f.Authority,f.Grant.Balances));
        var wrongAuthority=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        ProofInvalid(exact,GrantLike(f.Grant,CapacityGrantStateV1.Active,wrongAuthority,f.Grant.Balances));
        ProofInvalid(exact,GrantLike(f.Grant,CapacityGrantStateV1.Active,f.Authority,[]));
        var other=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var otherAuthority=ExpectedAuthorityVectorV1.Create(other,[new AuthorityAxisValueV1.Graph(f.GraphGeneration)]);
        ProofInvalid(exact,new CapacityGrantSnapshotV1(f.Grant.GrantId,f.Grant.OperationId,otherAuthority,new JournalPositionV1(other,2),new JournalPositionV1(other,3),f.Grant.ExpiresAt,CapacityGrantStateV1.Active,f.Grant.Balances));
    }
    [Fact]
    public void RuntimeReplacement_FencesBeforeCommandAndCapacityProof()
    {
        var fold=new GraphReplacementJournalFoldResultV1.RuntimeReplaced(RuntimeGenerationId.FromValue(Id(9)),12);
        var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(new GraphReplacementSnapshotReadResultV1.Verified(fold,12));
        var operation=OperationId.FromValue(Id(8));var active=Position(5);
        var command=new GraphRuntimeCommandV1.Retire(operation,active,active,GraphRuntimeEffectHashesV1.Retire(Session(),operation,active));
        Assert.IsType<GraphRuntimeEvaluationV1.GenerationReplaced>(GraphRuntimeReducerV1.Evaluate(null,command,Position(6),Authority(),evidence));
    }

    [Fact]
    public void ClosedEffectAndResultConstructorsRejectDefaults()
    {
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeEffectResolutionV1.Completed(default));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeEffectResolutionV1.Refused(default));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeResolutionV1.Applied(null!,GraphRuntimeOutcomeV1.Activated,Hash(1)));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeResolutionV1.Rejected(null,default));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeEvaluationV1.Rejected(null,default));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeEvaluationV1.Conflict(null,default));
    }

    [Fact]
    public void ForgedSameAssemblyCapability_IsRejected()
    {
        var operation=OperationId.FromValue(Id(8));var active=Position(5);var command=new GraphRuntimeCommandV1.Retire(operation,active,active,GraphRuntimeEffectHashesV1.Retire(Session(),operation,active));
        var forged=new GraphRuntimeReducerV1.EffectRequired(new object(),command,null,Authority(),Position(6),active);
        Assert.Throws<ArgumentException>(()=>GraphRuntimeEffectRequestV1.From(forged));
        var rejected=Assert.IsType<GraphRuntimeResolutionV1.Rejected>(GraphRuntimeReducerV1.Resolve(forged,new GraphRuntimeEffectResolutionV1.Completed(Hash(9)),Position(7)));
        Assert.Equal("runtime-result-position-invalid",rejected.SafeCode.ToString());
    }

    [Fact]
    public void EvidenceFactory_AcceptsOnlyExactVerifiedCoverage()
    {
        var fold=new GraphReplacementJournalFoldResultV1.RuntimeReplaced(RuntimeGenerationId.FromValue(Id(9)),12);
        Assert.IsType<GraphRuntimeCurrentGraphEvidenceV1.RuntimeReplaced>(GraphRuntimeCurrentGraphEvidenceV1.From(new GraphReplacementSnapshotReadResultV1.Verified(fold,12)));
        Assert.Throws<ArgumentException>(()=>new GraphReplacementSnapshotReadResultV1.Verified(fold,11));
    }

    [Fact]
    public async Task ActiveConflictAndResolveRefusalPreserveExactPriorSnapshot()
    {
        var f=await Fixture.CreateAsync();var operation=OperationId.Create();var install=f.Installation.Position;var request=GraphRuntimeEffectHashesV1.Activate(f.Session,operation,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact);var command=new GraphRuntimeCommandV1.Activate(operation,install,install,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,request);var admitted=await f.AppendCommandAsync(command);var evidence=GraphRuntimeCurrentGraphEvidenceV1.From(Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(await GraphReplacementSnapshotReaderV1.ReadAsync(f.Journal,f.Session)));var required=Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(GraphRuntimeReducerV1.Evaluate(null,command,admitted.Position,f.Authority,evidence,f.Grant));var active=Assert.IsType<GraphRuntimeResolutionV1.Applied>(GraphRuntimeReducerV1.Resolve(required,new GraphRuntimeEffectResolutionV1.Completed(Hash(9)),new JournalPositionV1(f.Session,6))).Snapshot;
        Assert.IsType<GraphRuntimeEvaluationV1.Conflict>(GraphRuntimeReducerV1.Evaluate(active,command,admitted.Position,f.Authority,evidence,f.Grant));
        var refused=Assert.IsType<GraphRuntimeResolutionV1.Rejected>(GraphRuntimeReducerV1.Resolve(required,new GraphRuntimeEffectResolutionV1.Refused(new BoundedAscii("effect-refused")),new JournalPositionV1(f.Session,6)));
        Assert.Null(refused.Snapshot);Assert.Equal("effect-refused",refused.SafeCode.ToString());
        Assert.IsType<GraphRuntimeResolutionV1.Rejected>(GraphRuntimeReducerV1.Resolve(required,new GraphRuntimeEffectResolutionV1.Completed(Hash(9)),admitted.Position));
    }

    private static ExpectedAuthorityVectorV1 Authority()=>ExpectedAuthorityVectorV1.Create(Session(),[new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(5)))]);
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.FromValue(Id(1)),LiveSessionId.FromValue(Id(2)));private static JournalPositionV1 Position(long n)=>new(Session(),n);
    private static StableId128 Id(byte n)=>StableId128.FromBytes(Enumerable.Repeat(n,16).ToArray());private static Hash256 Hash(byte n){Hash256.TryCreate(Enumerable.Repeat(n,32).ToArray(),out var h);return h;}
    private static CapacityGrantSnapshotV1 GrantLike(CapacityGrantSnapshotV1 source,CapacityGrantStateV1 state,ExpectedAuthorityVectorV1 authority,IReadOnlyList<CapacityChargeBalanceV1> balances)=>new(source.GrantId,source.OperationId,authority,source.GrantedAt,source.CurrentFact,source.ExpiresAt,state,balances);

    internal sealed class Fixture
    {
        internal SessionAuthorityStampV1 Session=new(RuntimeGenerationId.Create(),LiveSessionId.Create());internal GraphGenerationId GraphGeneration=GraphGenerationId.Create();internal ExpectedAuthorityVectorV1 Authority=null!;internal InMemoryAuthorityJournalV1 Journal=null!;internal CapacityGrantSnapshotV1 Grant=null!;internal GraphTopologyPlanV1 Plan=null!;internal AuthorityFactEnvelopeV1 Installation=null!;private readonly CorrelationEnvelopeV1 _correlation=new(TenantId.Create());private readonly ClockDomainId _clock=ClockDomainId.Create();private readonly BootId _boot=BootId.Create();
        internal static async Task<Fixture>CreateAsync(){var f=new Fixture();f.Authority=ExpectedAuthorityVectorV1.Create(f.Session,[new AuthorityAxisValueV1.Graph(f.GraphGeneration)]);f.Journal=new(new AuthorityPayloadAdmissionRegistryV1([new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),new CapacityReservationPayloadRegistrationV1(),new CapacitySettlementPayloadRegistrationV1(),GraphReplacementPayloadRegistrationsV1.Installed,GraphRuntimePayloadRegistrationsV1.Command,GraphRuntimePayloadRegistrationsV1.Fact]),()=>new UtcInstant(100),new AuthorityJournalCapacityV1(2,32,4*1024*1024));await f.InitAsync();var op=OperationId.Create();var request=new CapacityRequestV1(op,f.Authority,[new CapacityChargeV1(new CapacityDimensionId(3),new CapacityScopeV1(f._correlation.TenantId,null,new CapacitySubjectV1.Operation(op)),1,CapacityPurposeId.Create(),new CapacityChargeWindowV1.NoWindow())],new MonotonicStampV1(f._clock,f._boot,100),CapacityPriorityV1.Normal);var reserved=Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(f.Journal,request,new CapacityGrantExpiryV1.NoExpiry(),new CorrelationEnvelopeV1(f._correlation.TenantId,operationId:op),new MonotonicStampV1(f._clock,f._boot,90),new UtcInstant(2)));var activateOp=OperationId.Create();var body=new CapacitySettlementFactBodyV1(reserved.Grant.GrantId,activateOp,reserved.Envelope.Position,CapacitySettlementKindV1.Activated,[new CapacitySettlementChargeV1(request.Charges[0].DimensionId,request.Charges[0].Scope,request.Charges[0].Purpose,1)],new MonotonicStampV1(f._clock,f._boot,90));f.Grant=Assert.IsType<CapacityAdmissionResultV1.Settled>(await CapacityAdmissionCoordinatorV1.SettleAsync(f.Journal,f.Session,body,new CorrelationEnvelopeV1(f._correlation.TenantId,operationId:activateOp),new UtcInstant(3))).Grant;f.Plan=new(f.Session,f.GraphGeneration,f.Grant.GrantId,[new GraphTopologyNodeV1(new BoundedAscii("source"))],[],[new CapacityDimensionId(3)]);var requestInstall=new GraphTopologyInstallationRequestV1(f.Session,f.Plan,f.Grant.CurrentFact,f.Authority,f._correlation,new UtcInstant(10));f.Installation=Assert.IsType<GraphTopologyInstallationAdmissionResultV1.Installed>(await GraphTopologyInstallationAdmissionV1.InstallAsync(f.Journal,requestInstall)).Envelope;return f;}
        internal Task<AuthorityFactEnvelopeV1>AppendCommandAsync(GraphRuntimeCommandV1 command)=>AppendCommandAsync(command,Installation.Position.Sequence);
        internal async Task<AuthorityFactEnvelopeV1>AppendCommandAsync(GraphRuntimeCommandV1 command,long head){var body=GraphRuntimeCodecsV1.EncodeCommand(command);var payload=GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(Session,Authority,body));var r=GraphRuntimePayloadRegistrationsV1.Command;var proposal=new ProposedAuthorityFactV1(GraphRuntimeFactIdsV1.Command(Session,command.OperationId,command.Kind),null,OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload),_correlation,new UtcInstant(11));var result=Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(new AppendAuthorityBatchV1(Session,head,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));return Assert.Single(result.Envelopes);}
        internal async Task<AuthorityFactEnvelopeV1> AppendFactAsync(GraphRuntimeFactV1 fact,long head){var body=GraphRuntimeCodecsV1.EncodeFact(fact);var payload=GraphRuntimeCodecsV1.EncodeOuter(new GraphRuntimeOwnerPayloadV1(Session,Authority,body));var r=GraphRuntimePayloadRegistrationsV1.Fact;var proposal=new ProposedAuthorityFactV1(GraphRuntimeFactIdsV1.Result(fact.CommandFact),null,OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload),_correlation,new UtcInstant(12));var committed=Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(new AppendAuthorityBatchV1(Session,head,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));return Assert.Single(committed.Envelopes);}
        private async Task InitAsync(){var r=new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph);Span<byte>g=stackalloc byte[16];GraphGeneration.TryWriteBytes(g);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(3);w.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(w,Session);w.WriteUInt64(2);w.WriteByteString(g);w.WriteUInt64(3);w.WriteUInt64((ushort)OwnerSliceId.S2);w.WriteEndMap();var p=w.Encode();var proposal=new ProposedAuthorityFactV1(JournalFactId.Create(),null,OwnerSliceId.S2,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p),_correlation,new UtcInstant(1));Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(new AppendAuthorityBatchV1(Session,0,[],[proposal],ProposedAuthorityFactV1.MaximumPayloadBytes)));}
    }
}
