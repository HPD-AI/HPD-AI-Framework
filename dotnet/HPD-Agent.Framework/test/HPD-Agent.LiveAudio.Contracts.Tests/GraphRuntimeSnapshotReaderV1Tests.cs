using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeSnapshotReaderV1Tests
{
    [Fact]
    public async Task ProofBudget_FreezesExactReadFactByteAndCacheBounds()
    {
        var fixture=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        var budget=new GraphRuntimeProofBudgetV1();
        for(var i=0;i<GraphRuntimeSnapshotReaderV1.MaximumProofReads;i++)Assert.True(budget.TryBeginRead());
        Assert.False(budget.TryBeginRead());Assert.Equal(256,budget.Reads);

        budget=new GraphRuntimeProofBudgetV1();var key=(fixture.Grant.GrantId,fixture.Grant.CurrentFact);
        Assert.True(budget.TryBeginRead());Assert.True(budget.TryAccept(key,
            new CapacityGrantSnapshotAtResultV1.Exact(fixture.Grant,65_536,1)));
        Assert.Equal(65_536,budget.Facts);Assert.True(budget.TryGet(key,out var cached));Assert.Same(fixture.Grant,cached);
        Assert.False(budget.TryAccept((CapacityGrantId.Create(),fixture.Grant.CurrentFact),
            new CapacityGrantSnapshotAtResultV1.Exact(fixture.Grant,1,1)));
        Assert.False(budget.TryAccept((fixture.Grant.GrantId,
            new JournalPositionV1(fixture.Session,fixture.Grant.CurrentFact.Sequence-1)),
            new CapacityGrantSnapshotAtResultV1.Exact(fixture.Grant,1,1)));

        budget=new GraphRuntimeProofBudgetV1();Assert.True(budget.TryBeginRead());
        Assert.True(budget.TryAccept(key,new CapacityGrantSnapshotAtResultV1.Exact(fixture.Grant,1,67_108_864)));
        Assert.Equal(67_108_864UL,budget.Bytes);
        Assert.False(budget.TryAccept((CapacityGrantId.Create(),fixture.Grant.CurrentFact),
            new CapacityGrantSnapshotAtResultV1.Exact(fixture.Grant,1,1)));

        budget=new GraphRuntimeProofBudgetV1();Assert.True(budget.TryBeginRead());
        Assert.False(budget.TryAccept(key,new CapacityGrantSnapshotAtResultV1.Exact(fixture.Grant,1,ulong.MaxValue)));
    }

    [Fact]
    public async Task ReaderProofMetricOverflow_IsOutcomeUnknownAtPrefix()
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var calls=0;
        ValueTask<CapacityGrantSnapshotAtResultV1> Proof(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,
            CapacityGrantId ___,JournalPositionV1 ____,CancellationToken _____)
        { calls++;return ValueTask.FromResult<CapacityGrantSnapshotAtResultV1>(
            new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,65_537,1)); }
        var unknown=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(
            await GraphRuntimeSnapshotReaderV1.ReadAsync(f.Journal,f.Session,Proof));
        Assert.Equal("runtime-graph-proof-unknown",unknown.Code.ToString());Assert.Equal(1,calls);
    }
    [Fact]
    public async Task ExactGrantPositionProof_IsCachedAcrossInstallAndActivate()
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var operation=OperationId.Create();
        await f.AppendCommandAsync(new GraphRuntimeCommandV1.Activate(operation,f.Installation.Position,
            f.Installation.Position,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,
            GraphRuntimeEffectHashesV1.Activate(f.Session,operation,f.Installation.Position,
                f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact)));
        var calls=0;
        ValueTask<CapacityGrantSnapshotAtResultV1> Proof(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,
            CapacityGrantId grant,JournalPositionV1 through,CancellationToken ___)
        { calls++;Assert.Equal(f.Grant.GrantId,grant);Assert.Equal(f.Grant.CurrentFact,through);
          return ValueTask.FromResult<CapacityGrantSnapshotAtResultV1>(new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,4,1024)); }
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(
            await GraphRuntimeSnapshotReaderV1.ReadAsync(f.Journal,f.Session,Proof));
        Assert.IsType<GraphRuntimeJournalFoldResultV1.Current>(verified.Fold);Assert.Equal(1,calls);
    }

    [Fact]
    public async Task ResultAndProofEvidenceConstructors_RejectUnprovenValues()
    {
        var session=Session();var current=new GraphRuntimeJournalFoldResultV1.Current(2,
            new CurrentAuthorityVectorSnapshotV1(session,[],2),null,null,[]);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeSnapshotReadResultV1.Verified(current,1));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeSnapshotReadResultV1.Verified(
            new GraphRuntimeJournalFoldResultV1.InvalidHistory(new BoundedAscii("bad"),0),0));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeSnapshotReadResultV1.InvalidHistory(default,0));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeSnapshotReadResultV1.OutcomeUnknown(default,0,null));
        Assert.Throws<ArgumentNullException>(()=>new CapacityGrantSnapshotAtResultV1.Exact(null!,1,1));
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();
        Assert.Throws<ArgumentOutOfRangeException>(()=>new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,0,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,1,0));
    }
    [Fact]
    public async Task RealInstallActivateAndResolvedFact_ReplayBothProofChannels()
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var operation=OperationId.Create();
        var command=new GraphRuntimeCommandV1.Activate(operation,f.Installation.Position,f.Installation.Position,
            f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,GraphRuntimeEffectHashesV1.Activate(f.Session,
                operation,f.Installation.Position,f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact));
        var admitted=await f.AppendCommandAsync(command);
        var pendingRead=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(await GraphRuntimeSnapshotReaderV1.ReadAsync(f.Journal,f.Session));
        var pending=Assert.IsType<GraphRuntimeJournalFoldResultV1.Current>(pendingRead.Fold).Pending;
        Assert.NotNull(pending);Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(pending!.Evaluation);
        var required=(GraphRuntimeReducerV1.EffectRequired)pending.Evaluation;var resultPosition=new JournalPositionV1(f.Session,admitted.Position.Sequence+1);
        var applied=Assert.IsType<GraphRuntimeResolutionV1.Applied>(GraphRuntimeReducerV1.Resolve(required,new GraphRuntimeEffectResolutionV1.Completed(Hash(9)),resultPosition));
        await f.AppendFactAsync(new GraphRuntimeFactV1(admitted.Position,command.ExpectedPredecessor,f.Installation.Position,
            GraphRuntimeOutcomeV1.Activated,applied.Snapshot,Hash(9),null),admitted.Position.Sequence);
        var resolved=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(await GraphRuntimeSnapshotReaderV1.ReadAsync(f.Journal,f.Session));
        var current=Assert.IsType<GraphRuntimeJournalFoldResultV1.Current>(resolved.Fold);Assert.Null(current.Pending);
        Assert.Equal(GraphRuntimePhaseV1.Active,current.Snapshot!.Phase);
    }

    [Fact]
    public async Task PinnedSnapshotDrift_IsOutcomeUnknownAtVerifiedPrefix()
    {
        var session=Session();
        var drift=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(session,2,0,2,[Unrelated(session,1)],true),
                new ReadAuthorityRangeResultV1.Batch(session,3,1,3,[Unrelated(session,2),Unrelated(session,3)],false)),session));
        Assert.Equal("runtime-snapshot-drift",drift.Code.ToString());Assert.Equal(1,drift.LastVerified);
    }
    [Fact]
    public async Task EmptyPinnedSnapshot_IsVerifiedCurrent()
    {
        var session=Session();var result=await GraphRuntimeSnapshotReaderV1.ReadAsync(new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session,0,0,0,[],false)),session);
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(result);
        Assert.Equal(0,verified.SnapshotThrough);var current=Assert.IsType<GraphRuntimeJournalFoldResultV1.Current>(verified.Fold);
        Assert.Null(current.Snapshot);Assert.Null(current.Pending);
    }

    [Fact]
    public async Task FirstPagePin_IsRetainedAcrossPagesDespiteExternalGrowth()
    {
        var session=Session();var first=Unrelated(session,1);var second=Unrelated(session,2);
        var journal=new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session,2,0,2,[first],true),
            new ReadAuthorityRangeResultV1.Batch(session,2,1,2,[second],false));
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(await GraphRuntimeSnapshotReaderV1.ReadAsync(journal,session));
        Assert.Equal(2,verified.SnapshotThrough);Assert.Equal(long.MaxValue,journal.Requests[0].ThroughInclusive);
        Assert.All(journal.Requests.Skip(1),request=>Assert.Equal(2,request.ThroughInclusive));
    }

    [Fact]
    public async Task StoreExceptionAndCancellation_AreClosedUnknownOutcomes()
    {
        var session=Session();var exception=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(
            await GraphRuntimeSnapshotReaderV1.ReadAsync(new ThrowingJournal(),session));
        Assert.Equal("runtime-store-exception",exception.Code.ToString());Assert.Equal(0,exception.LastVerified);
        using var cancellation=new CancellationTokenSource();var cancelled=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(
            await GraphRuntimeSnapshotReaderV1.ReadAsync(new CancelThenReturnJournal(session,cancellation.Cancel),session,cancellation.Token));
        Assert.Equal("runtime-read-cancelled",cancelled.Code.ToString());Assert.Equal(0,cancelled.LastVerified);
    }

    [Fact]
    public async Task StoreUnavailableAndItemTooLarge_PreserveClosedUnknownPrefix()
    {
        var session=Session();
        var unavailable=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("store-down"))),session));
        Assert.Equal("store-down",unavailable.Code.ToString());Assert.Equal(0,unavailable.LastVerified);Assert.Null(unavailable.Pending);
        var oversized=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.ItemTooLarge(new JournalPositionV1(session,1),
                GraphRuntimeSnapshotReaderV1.PageBytes+1,GraphRuntimeSnapshotReaderV1.PageBytes)),session));
        Assert.Equal("runtime-item-too-large",oversized.Code.ToString());Assert.Equal(0,oversized.LastVerified);
    }

    [Fact]
    public async Task HostileEmptyContinuationAndIncompleteFinal_AreClosedUnknownOutcomes()
    {
        var session=Session();
        var empty=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(HostileBatch(session,1,0,1,[],true)),session));
        Assert.Equal("runtime-empty-continuation",empty.Code.ToString());Assert.Equal(0,empty.LastVerified);

        var first=Unrelated(session,1);
        var incomplete=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(HostileBatch(session,2,0,2,[first],false)),session));
        Assert.Equal("runtime-snapshot-incomplete",incomplete.Code.ToString());Assert.Equal(1,incomplete.LastVerified);
    }

    [Fact]
    public async Task CanonicalPageOverOneMiB_IsOutcomeUnknownBeforeReduction()
    {
        var session=Session();var facts=Enumerable.Range(1,256).Select(i=>Unrelated(session,i,4200)).ToArray();
        var unknown=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(session,256,0,256,facts,false)),session));
        Assert.Equal("runtime-page-byte-bound",unknown.Code.ToString());Assert.Equal(0,unknown.LastVerified);
    }

    [Theory]
    [InlineData(65_536,true)]
    [InlineData(65_537,false)]
    public async Task PrimaryFactBoundary_IsExact(int facts,bool accepted)
    {
        var session=Session();var result=await GraphRuntimeSnapshotReaderV1.ReadAsync(new GeneratedPagedJournal(session,facts),session);
        if(accepted){var verified=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(result);Assert.Equal(facts,verified.SnapshotThrough);}
        else {var unknown=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(result);Assert.Equal("runtime-fact-bound",unknown.Code.ToString());Assert.Equal(65_536,unknown.LastVerified);}
    }

    [Fact]
    public async Task FutureActivateProofReference_IsInvalidWithoutReadingThatProof()
    {
        var f=await GraphRuntimeJournalFoldV1Tests.ClaimedFixture.CreateAsync();var operation=OperationId.Create();
        var future=new JournalPositionV1(f.Session,10_000);var command=new GraphRuntimeCommandV1.Activate(operation,f.Installation.Position,
            f.Installation.Position,f.Plan.Fingerprint,f.Graph,future,GraphRuntimeEffectHashesV1.Activate(f.Session,operation,
                f.Installation.Position,f.Plan.Fingerprint,f.Graph,future));var c=f.Command(command);var facts=f.Facts.Append(c).ToArray();var futureCalls=0;
        ValueTask<CapacityGrantSnapshotAtResultV1> Proof(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,CapacityGrantId ___,JournalPositionV1 through,CancellationToken ____)
        {if(through==future)futureCalls++;return ValueTask.FromResult<CapacityGrantSnapshotAtResultV1>(new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,1,1));}
        var invalid=Assert.IsType<GraphRuntimeSnapshotReadResultV1.InvalidHistory>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(f.Session,c.Position.Sequence,0,c.Position.Sequence,facts,false)),f.Session,Proof));
        Assert.Equal("invalid-graph-runtime-command",invalid.Code.ToString());Assert.Equal(c.Position.Sequence-1,invalid.LastVerified);Assert.Equal(0,futureCalls);
    }

    [Fact]
    public async Task CancellationInsideInstallationProof_PreservesExactPreInstallPrefix()
    {
        var f=await GraphRuntimeJournalFoldV1Tests.ClaimedFixture.CreateAsync();using var cancellation=new CancellationTokenSource();
        ValueTask<CapacityGrantSnapshotAtResultV1> Proof(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,CapacityGrantId ___,JournalPositionV1 ____,CancellationToken _____)
        {cancellation.Cancel();return ValueTask.FromResult<CapacityGrantSnapshotAtResultV1>(new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,1,1));}
        var unknown=Assert.IsType<GraphRuntimeSnapshotReadResultV1.OutcomeUnknown>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(f.Session,f.Facts.Count,0,f.Facts.Count,f.Facts,false)),f.Session,Proof,cancellation.Token));
        Assert.Equal("runtime-proof-cancelled",unknown.Code.ToString());Assert.Equal(f.Installation.Position.Sequence-1,unknown.LastVerified);Assert.Null(unknown.Pending);
    }

    [Fact]
    public async Task ClaimedAxisRace_RetainsReceiptTerminalSnapshotAndTailPin()
    {
        var f=await GraphRuntimeJournalFoldV1Tests.ClaimedFixture.CreateAsync();var operation=OperationId.Create();
        var command=new GraphRuntimeCommandV1.Activate(operation,f.Installation.Position,f.Installation.Position,f.Plan.Fingerprint,f.Graph,
            f.Grant.CurrentFact,GraphRuntimeEffectHashesV1.Activate(f.Session,operation,f.Installation.Position,f.Plan.Fingerprint,f.Graph,f.Grant.CurrentFact));
        var c=f.Command(command);var transition=f.Transition(ActivityGenerationId.Create(),c.Position.Sequence+1);var resultPosition=new JournalPositionV1(f.Session,transition.Position.Sequence+1);var receipt=Hash(77);
        var active=new GraphRuntimeSnapshotV1(GraphRuntimePhaseV1.Active,f.Graph,f.Plan.Fingerprint,f.Grant.CurrentFact,f.Authority,operation,resultPosition,resultPosition,null);
        var terminal=f.Fact(new GraphRuntimeFactV1(c.Position,command.ExpectedPredecessor,f.Installation.Position,GraphRuntimeOutcomeV1.Activated,active,receipt,null),resultPosition.Sequence);
        var tail=f.Other(resultPosition.Sequence+1);var facts=f.Facts.Append(c).Append(transition).Append(terminal).Append(tail).ToArray();
        ValueTask<CapacityGrantSnapshotAtResultV1> Proof(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,CapacityGrantId ___,JournalPositionV1 ____,CancellationToken _____)
            =>ValueTask.FromResult<CapacityGrantSnapshotAtResultV1>(new CapacityGrantSnapshotAtResultV1.Exact(f.Grant,1,1));
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(f.Session,tail.Position.Sequence,0,tail.Position.Sequence,facts,false)),f.Session,Proof));
        var replaced=Assert.IsType<GraphRuntimeJournalFoldResultV1.AuthorityGenerationReplaced>(verified.Fold);Assert.Equal(tail.Position.Sequence,verified.SnapshotThrough);
        Assert.Equal(terminal,replaced.TerminalResultFact);Assert.Equal(active,replaced.Snapshot);Assert.Null(replaced.Pending);
    }

    [Fact]
    public async Task RuntimeTerminalAtPin_IsVerifiedAndTailIsInvalidHistory()
    {
        var session=Session();var replacement=RuntimeGenerationId.Create();var terminal=RuntimeTransition(session,replacement,1);
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResultV1.Verified>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(session,1,0,1,[terminal],false)),session));
        Assert.Equal(replacement,Assert.IsType<GraphRuntimeJournalFoldResultV1.RuntimeReplaced>(verified.Fold).Next);
        var invalid=Assert.IsType<GraphRuntimeSnapshotReadResultV1.InvalidHistory>(await GraphRuntimeSnapshotReaderV1.ReadAsync(
            new ScriptedJournal(new ReadAuthorityRangeResultV1.Batch(session,2,0,2,[terminal,Unrelated(session,2)],false)),session));
        Assert.Equal("facts-after-runtime-replacement",invalid.Code.ToString());Assert.Equal(1,invalid.LastVerified);
    }

    [Fact]
    public void ResultArmsAndPrimaryBounds_AreFrozen()
    {
        Assert.Equal(256,GraphRuntimeSnapshotReaderV1.PageFacts);Assert.Equal(1_048_576u,GraphRuntimeSnapshotReaderV1.PageBytes);
        Assert.Equal(65_536,GraphRuntimeSnapshotReaderV1.MaximumFacts);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeSnapshotReadResultV1.OutcomeUnknown(default,0,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeSnapshotReadResultV1.InvalidHistory(new BoundedAscii("bad"),-1));
    }

    [Fact]
    public async Task MalformedContiguousHistory_IsInvalidHistoryNotOutcomeUnknown()
    {
        var session=Session();var result=await GraphRuntimeSnapshotReaderV1.ReadAsync(new ScriptedJournal(
            new ReadAuthorityRangeResultV1.Batch(session,1,0,1,[MalformedRuntime(session,1)],false)),session);
        var invalid=Assert.IsType<GraphRuntimeSnapshotReadResultV1.InvalidHistory>(result);
        Assert.Equal("invalid-graph-runtime-command",invalid.Code.ToString());Assert.Equal(0,invalid.LastVerified);
    }

    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
    private static Hash256 Hash(byte value){Hash256.TryCreate(Enumerable.Repeat(value,32).ToArray(),out var hash);return hash;}
    private static AuthorityFactEnvelopeV1 Unrelated(SessionAuthorityStampV1 session,long sequence,int payloadBytes=1){var payload=payloadBytes==1?[0x80]:Enumerable.Repeat((byte)0x80,payloadBytes).ToArray();return new(JournalFactId.Create(),new JournalPositionV1(session,sequence),null,OwnerSliceId.S4,new SchemaReferenceV1(SchemaId.Create(),1,0),payload,Hash256.Compute(payload),new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(sequence),new UtcInstant(sequence),new IntegrityEnvelopeV1(1,1,Hash256.Compute([1]),[]));}
    private static AuthorityFactEnvelopeV1 MalformedRuntime(SessionAuthorityStampV1 session,long sequence){var payload=new byte[]{0x80};var r=GraphRuntimePayloadRegistrationsV1.Command;return new(JournalFactId.Create(),new JournalPositionV1(session,sequence),null,OwnerSliceId.S2,r.Schema,payload,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,payload),new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(sequence),new UtcInstant(sequence),new IntegrityEnvelopeV1(1,1,Hash256.Compute([1]),[]));}
    private static AuthorityFactEnvelopeV1 RuntimeTransition(SessionAuthorityStampV1 session,RuntimeGenerationId replacement,long sequence){Span<byte>old=stackalloc byte[16];Span<byte>next=stackalloc byte[16];session.RuntimeGenerationId.TryWriteBytes(old);replacement.TryWriteBytes(next);var p=AuthorityGenerationTransitionCodecV1.Encode(session,AuthorityAxisId.Runtime,StableId128.FromBytes(old),StableId128.FromBytes(next));var r=new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Runtime);return new(JournalFactId.Create(),new JournalPositionV1(session,sequence),null,OwnerSliceId.S1,r.Schema,p,AuthorityPayloadHashV1.Compute(r.SchemaToken,r.Schema,p),new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(sequence),new UtcInstant(sequence),new IntegrityEnvelopeV1(1,1,Hash256.Compute([1]),[]));}
    private sealed class ScriptedJournal(params ReadAuthorityRangeResultV1[] results):IAuthorityJournalV1
    {private int _index;internal List<ReadAuthorityRangeV1> Requests{get;}=[];public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken=default)=>throw new NotSupportedException();public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default){Requests.Add(request);return ValueTask.FromResult(results[_index++]);}}
    private sealed class ThrowingJournal:IAuthorityJournalV1{public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken=default)=>throw new NotSupportedException();public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default)=>throw new IOException("fixture");}
    private sealed class CancelThenReturnJournal(SessionAuthorityStampV1 session,Action cancel):IAuthorityJournalV1{public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken=default)=>throw new NotSupportedException();public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default){cancel();return ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.Batch(session,0,0,0,[],false));}}
    private sealed class GeneratedPagedJournal(SessionAuthorityStampV1 session,int count):IAuthorityJournalV1
    {
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,CancellationToken cancellationToken=default)
        {var take=(int)Math.Min(256,count-request.AfterExclusive);var facts=Enumerable.Range(1,take).Select(i=>Unrelated(session,request.AfterExclusive+i)).ToArray();return ValueTask.FromResult<ReadAuthorityRangeResultV1>(new ReadAuthorityRangeResultV1.Batch(session,count,request.AfterExclusive,count,facts,request.AfterExclusive+take<count));}
    }
    private static ReadAuthorityRangeResultV1.Batch HostileBatch(SessionAuthorityStampV1 session,long head,long after,long through,AuthorityFactEnvelopeV1[] facts,bool more)
    {
        var batch=(ReadAuthorityRangeResultV1.Batch)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ReadAuthorityRangeResultV1.Batch));
        static void Set(object target,string name,object value)=>target.GetType().GetField($"<{name}>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(target,value);
        typeof(ReadAuthorityRangeResultV1.Batch).GetField("_facts",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(batch,facts);
        Set(batch,"Session",session);Set(batch,"SnapshotHead",head);Set(batch,"AfterExclusive",after);Set(batch,"SnapshotThrough",through);Set(batch,"Facts",Array.AsReadOnly(facts));Set(batch,"HasMore",more);return batch;
    }
}
