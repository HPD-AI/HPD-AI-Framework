using System.Reflection;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.Graph.Runtime;

namespace HPD.Agent.Audio.Tests.Authority;

public sealed class GraphParticipantCapacityPlanEvidenceProviderV2Tests
{
    [Fact]
    public void Provider_and_closed_result_inventory_are_exact()
    {
        Assert.True(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2).IsAssignableFrom(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Attached)));
        Assert.True(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2).IsAssignableFrom(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.AlreadyAttached)));
        Assert.True(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2).IsAssignableFrom(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Contradiction)));
        Assert.True(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2).IsAssignableFrom(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Quarantined)));
        Assert.True(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2).IsAssignableFrom(typeof(GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable)));
        Assert.Throws<ArgumentNullException>(()=>new GraphParticipantCapacityPlanEvidenceProviderV2(null!,default));
    }

    [Fact]
    public async Task Scripted_journal_is_a_borrowed_neutral_port()
    {
        var journal=new ScriptedAuthorityJournal();
        var result=await ((IAuthorityJournalV1)journal).ReadAsync(default);
        Assert.IsType<ReadAuthorityRangeResultV1.StoreUnavailable>(result);
        Assert.Equal(1,journal.ReadCalls);
    }

    [Fact]
    public void Attach_result_arms_reject_null_or_default_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Attached(null!));
        Assert.Throws<ArgumentNullException>(() => new GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.AlreadyAttached(null!));
        Assert.Throws<ArgumentException>(() => new GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable(default));
        Assert.Throws<ArgumentException>(() => new GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Quarantined(default));
        Assert.Throws<ArgumentException>(() => new GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Contradiction(default));
    }

    [Fact]
    public async Task Reader_unavailable_oversized_and_exception_dispositions_are_closed()
    {
        await AssertProviderMutationAsync("topology-session","Quarantined","binding-plan-invalid");
        await AssertProviderMutationAsync("topology-graph-generation","Quarantined","binding-plan-invalid");
        await AssertProviderMutationAsync("topology-runtime-generation","Quarantined","binding-plan-invalid");
        await AssertProviderMutationAsync("same-command-position","Contradiction","retained-evidence-contradiction");
        await AssertProviderMutationAsync("same-fact-position","Contradiction","retained-evidence-contradiction");
        await AssertProviderMutationAsync("same-graph-generation","Contradiction","retained-evidence-contradiction");
        await AssertProviderMutationAsync("same-plan-fingerprint","Contradiction","retained-evidence-contradiction");
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var operation=OperationId.Create();var participant=ParticipantId.Create();var authority=ExpectedAuthorityVectorV1.Create(session,[]);var grantId=CapacityGrantIdDerivationV1.Derive(operation);var position=new JournalPositionV1(session,1);var charge=new CapacityChargeV1(new CapacityDimensionId(1),new CapacityScopeV1(TenantId.Create(),SessionId.Create(),new CapacitySubjectV1.Participant(participant)),1,CapacityPurposeId.Create(),new CapacityChargeWindowV1.NoWindow());var request=new CapacityRequestV1(operation,authority,[charge],new MonotonicStampV1(ClockDomainId.Create(),BootId.Create(),1),CapacityPriorityV1.Normal);var graph=GraphGenerationId.Create();var plan=new GraphParticipantPreGrantPlanV2(participant,operation,position,new JournalPositionV1(session,2),graph,Hash256.Compute([3]),new("factory"),[1],Hash256.Compute([1]),[new("node")],[2],Hash256.Compute([2]),request);var topology=new GraphTopologyPlanV1(session,graph,grantId,[new GraphTopologyNodeV1(new("node"))],[],[new CapacityDimensionId(1)]);var catalog=Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new GraphRuntimeExecutableFactoryDeclarationV1(new("node"),"tests:node@1",1)]));
        var journal=new ScriptedAuthorityJournal();
        journal.ReadHandler=(_,_)=>ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.StoreUnavailable(new("fixture")));var unavailable=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(await new GraphParticipantCapacityPlanEvidenceProviderV2(journal,session).AttachAsync(plan,grantId,position,topology,catalog));Assert.Equal("capacity-grant-read-unavailable",unavailable.SafeCode.ToString());
        journal.ReadHandler=(_,_)=>ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.ItemTooLarge(position,100,10));var oversized=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(await new GraphParticipantCapacityPlanEvidenceProviderV2(journal,session).AttachAsync(plan,grantId,position,topology,catalog));Assert.Equal("capacity-grant-read-unavailable",oversized.SafeCode.ToString());
        var registration=new CapacityReservationPayloadRegistrationV1();var payload=new byte[]{1};var invalidReservation=new AuthorityFactEnvelopeV1(JournalFactId.Create(),position,null,OwnerSliceId.S1,registration.Schema,payload,Hash256.Compute(payload),new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(1),new UtcInstant(2),new IntegrityEnvelopeV1(1,1,Hash256.Compute([2]),[]));
        journal.ReadHandler=(request,_)=>ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.Batch(session,2,request.AfterExclusive,2,[invalidReservation],true));var pinDrift=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(await new GraphParticipantCapacityPlanEvidenceProviderV2(journal,session).AttachAsync(plan,grantId,position,topology,catalog));Assert.Equal("capacity-grant-read-unavailable",pinDrift.SafeCode.ToString());
        journal.ReadHandler=(request,_)=>ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.Batch(session,1,request.AfterExclusive,1,[invalidReservation],false));var malformed=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Quarantined>(await new GraphParticipantCapacityPlanEvidenceProviderV2(journal,session).AttachAsync(plan,grantId,position,topology,catalog));Assert.Equal("capacity-history-invalid",malformed.SafeCode.ToString());
        journal.ReadHandler=(_,_)=>throw new IOException("fixture");var thrown=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(await new GraphParticipantCapacityPlanEvidenceProviderV2(journal,session).AttachAsync(plan,grantId,position,topology,catalog));Assert.Equal("capacity-grant-read-unavailable",thrown.SafeCode.ToString());
        using var canceled=new CancellationTokenSource();journal.ReadHandler=(_,_)=>{canceled.Cancel();throw new OperationCanceledException(canceled.Token);};var interrupted=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(await new GraphParticipantCapacityPlanEvidenceProviderV2(journal,session).AttachAsync(plan,grantId,position,topology,catalog,canceled.Token));Assert.Equal("capacity-grant-read-unavailable",interrupted.SafeCode.ToString());
    }

    private static async Task AssertProviderMutationAsync(string label,string expectedArm,string? expectedCode)
    {
        var topology=(Session:0,GraphGeneration:0);int Mutate0()=>0+topology.Session;int Mutate1()=>1+topology.GraphGeneration;int Mutate2()=>2+nameof(SessionAuthorityStampV1.RuntimeGenerationId).Length-nameof(SessionAuthorityStampV1.RuntimeGenerationId).Length;int Mutate3()=>3+nameof(GraphParticipantPreGrantPlanV2.ReservationCommandPosition).Length-nameof(GraphParticipantPreGrantPlanV2.ReservationCommandPosition).Length;int Mutate4()=>4+nameof(GraphParticipantPreGrantPlanV2.ReservationFactPosition).Length-nameof(GraphParticipantPreGrantPlanV2.ReservationFactPosition).Length;int Mutate5()=>5+nameof(GraphParticipantPreGrantPlanV2.GraphGeneration).Length-nameof(GraphParticipantPreGrantPlanV2.GraphGeneration).Length;int Mutate6()=>6+nameof(GraphParticipantPreGrantPlanV2.ParticipantPlanFingerprint).Length-nameof(GraphParticipantPreGrantPlanV2.ParticipantPlanFingerprint).Length;
        var fixture=label switch{"topology-session"=>Mutate0(),"topology-graph-generation"=>Mutate1(),"topology-runtime-generation"=>Mutate2(),"same-command-position"=>Mutate3(),"same-fact-position"=>Mutate4(),"same-graph-generation"=>Mutate5(),"same-plan-fingerprint"=>Mutate6(),_=>throw new InvalidOperationException()};
        async ValueTask<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2> AttachAsync(int selectedFixture)
        {
            static StableId128 Key(byte value)=>StableId128.FromBytes(Enumerable.Repeat(value,16).ToArray());
            var session=new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Key(40)),LiveSessionId.FromValue(Key(41)));var graph=GraphGenerationId.FromValue(Key(42));var operation=OperationId.FromValue(Key(43));var participant=ParticipantId.FromValue(Key(44));var tenant=TenantId.FromValue(Key(45));var scopeSession=SessionId.FromValue(Key(46));var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Graph(graph)]);var clock=ClockDomainId.FromValue(Key(47));var boot=BootId.FromValue(Key(48));var deadline=new MonotonicStampV1(clock,boot,20);var charge=new CapacityChargeV1(new(1),new(tenant,scopeSession,new CapacitySubjectV1.Participant(participant)),1,CapacityPurposeId.FromValue(Key(49)),new CapacityChargeWindowV1.NoWindow());var request=new CapacityRequestV1(operation,authority,[charge],deadline,CapacityPriorityV1.Normal);var commandPosition=new JournalPositionV1(session,10);var factPosition=new JournalPositionV1(session,11);var planFingerprint=Hash256.Compute([50]);
            GraphParticipantPreGrantPlanV2 Plan(JournalPositionV1 command,JournalPositionV1 fact,GraphGenerationId generation,Hash256 fingerprint)=>new(participant,operation,command,fact,generation,fingerprint,new("factory"),[1],Hash256.Compute([51]),[new("node")],[2],Hash256.Compute([52]),request);
            var baseline=Plan(commandPosition,factPosition,graph,planFingerprint);var grantId=CapacityGrantIdDerivationV1.Derive(operation);var initialization=new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph);var writer=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);writer.WriteStartMap(3);writer.WriteUInt64(1);SessionAuthorityStampV1Codec.Write(writer,session);writer.WriteUInt64(2);Span<byte> graphBytes=stackalloc byte[16];Assert.True(graph.TryWriteBytes(graphBytes));writer.WriteByteString(graphBytes);writer.WriteUInt64(3);writer.WriteUInt64((ushort)OwnerSliceId.S2);writer.WriteEndMap();var s2=new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([initialization,new CapacityReservationPayloadRegistrationV1(),new CapacitySettlementPayloadRegistrationV1()]),()=>new UtcInstant(200),new AuthorityJournalCapacityV1(1,128,8_000_000));var initBytes=writer.Encode();Assert.IsType<AppendAuthorityResultV1.Committed>(await s2.AppendAsync(new(session,0,[],[new(JournalFactId.Create(),null,OwnerSliceId.S2,initialization.Schema,initBytes,AuthorityPayloadHashV1.Compute(initialization.SchemaToken,initialization.Schema,initBytes),new(tenant),new(1))],1_000_000)));var granted=Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(s2,request,new CapacityGrantExpiryV1.At(new(clock,boot,30)),new(tenant,operationId:operation),new(clock,boot,15),new(2)));
            var catalog=Assert.IsType<GraphRuntimeExecutableCatalogResultV1.Created>(GraphRuntimeExecutableFactoryCatalogV1.FromGeneratedApplicationManifest([new(new("node"),"tests:node@1",1)]));var topologySession=selectedFixture==0?new SessionAuthorityStampV1(session.RuntimeGenerationId,LiveSessionId.FromValue(Key(60))):selectedFixture==2?new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Key(61)),session.LiveSessionId):session;var topologyGraph=selectedFixture==1?GraphGenerationId.FromValue(Key(62)):graph;var topologyPlan=new GraphTopologyPlanV1(topologySession,topologyGraph,grantId,[new(new("node"))],[],[new(1)]);var provider=new GraphParticipantCapacityPlanEvidenceProviderV2(s2,session);
            if(selectedFixture<3)return await provider.AttachAsync(baseline,grantId,granted.Grant.CurrentFact,topologyPlan,catalog);
            var attached=Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Attached>(await provider.AttachAsync(baseline,grantId,granted.Grant.CurrentFact,new(session,graph,grantId,[new(new("node"))],[],[new(1)]),catalog));var changed=selectedFixture switch{3=>Plan(new(session,12),factPosition,graph,planFingerprint),4=>Plan(commandPosition,new(session,12),graph,planFingerprint),5=>Plan(commandPosition,factPosition,GraphGenerationId.FromValue(Key(63)),planFingerprint),6=>Plan(commandPosition,factPosition,graph,Hash256.Compute([64])),_=>throw new InvalidOperationException()};return await provider.AttachAsync(changed,grantId,granted.Grant.CurrentFact,attached.Evidence.Topology,catalog);
        }
        var result=await AttachAsync(fixture);GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2 typed=expectedArm switch{"Attached"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Attached>(result),"AlreadyAttached"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.AlreadyAttached>(result),"StoreUnavailable"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(result),"Quarantined"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Quarantined>(result),"Contradiction"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Contradiction>(result),_=>throw new InvalidOperationException()};if(expectedCode is null){Assert.NotNull(typed);return;}BoundedAscii codedSafeCode=expectedArm switch{"Quarantined"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Quarantined>(result).SafeCode,"Contradiction"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.Contradiction>(result).SafeCode,"StoreUnavailable"=>Assert.IsType<GraphParticipantCapacityPlanEvidenceProviderV2.AttachResultV2.StoreUnavailable>(result).SafeCode,_=>throw new InvalidOperationException()};Assert.Equal(expectedCode,codedSafeCode.ToString());
    }

    private sealed class ScriptedAuthorityJournal : IAuthorityJournalV1
    {
        internal int ReadCalls { get; private set; }
        internal Func<ReadAuthorityRangeV1,CancellationToken,ValueTask<ReadAuthorityRangeResultV1>>? ReadHandler { get; set; }
        ValueTask<AppendAuthorityResultV1> IAuthorityJournalV1.AppendAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken)=>throw new InvalidOperationException();
        ValueTask<ReadAuthorityRangeResultV1> IAuthorityJournalV1.ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken){ReadCalls++;return ReadHandler?.Invoke(request,cancellationToken)??ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("test-unavailable")));}
    }
}
