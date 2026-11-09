using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed partial class GraphRuntimeAdmissionCoordinatorTests
{
    [Fact]
    public void AuthorityMatch_RequiresEveryClaimedAxisButIgnoresUnclaimedLiveAxes()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var graph = GraphGenerationId.Create(); var activity = ActivityGenerationId.Create();
        var live = new CurrentAuthorityVectorSnapshotV1(session,
            [new AuthorityAxisValueV1.Graph(graph), new AuthorityAxisValueV1.Activity(activity)], 3);
        var graphOnly = ExpectedAuthorityVectorV1.Create(session, [new AuthorityAxisValueV1.Graph(graph)]);
        var claimedWrong = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(graph), new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create())]);

        Assert.True(GraphRuntimeAdmissionCoordinator.AuthorityMatches(graphOnly, live));
        Assert.False(GraphRuntimeAdmissionCoordinator.AuthorityMatches(claimedWrong, live));
    }

    [Fact]
    public async Task DefaultActivationPreflight_UsesEvidenceFromRuntimeFoldAndExactCapacityProof()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var port = new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([11]));

        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal, port, request));

        Assert.Equal(["E"], port.Calls);
    }

    [Fact]
    public async Task ExecuteUnknownThenQueryNotObserved_DoesNotExecuteTwiceInOneCall()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var port = new RecordingEffectPort()
            .ThenExecute(new GraphRuntimeEffectExecutionResult.OutcomeUnknown(new BoundedAscii("timeout")))
            .ThenQuery(new GraphRuntimeEffectQueryResult.NotObserved());

        var result = Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(
            await Admit(fixture.Journal, port, request));

        Assert.Equal("runtime-effect-not-observed-after-execute", result.Code.ToString());
        Assert.Equal(["E", "Q"], port.Calls); Assert.Single(port.Executions); Assert.Single(port.Queries);
    }

    [Fact]
    public async Task RecoverySupervisor_QuarantinesTimedOutReadUntilActualCompletion()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var time=new ManualTimeProvider();
        var supervisor=new GraphRuntimeRecoveryReadSupervisor(time);var first=new TaskCompletionSource<GraphRuntimeSnapshotReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        ValueTask<GraphRuntimeSnapshotReadResult> Reader(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,CancellationToken ___)
        {calls++;return calls==1?new(first.Task):ValueTask.FromResult<GraphRuntimeSnapshotReadResult>(new GraphRuntimeSnapshotReadResult.OutcomeUnknown(new BoundedAscii("released"),0,null));}
        var pending=supervisor.ReadAsync(Reader,fixture.Journal,fixture.Session).AsTask();
        while(Volatile.Read(ref calls)==0)await Task.Yield();
        time.Advance(GraphRuntimeAdmissionCoordinator.RecoveryReadTimeout);
        var timed=Assert.IsType<GraphRuntimeSnapshotReadResult.OutcomeUnknown>(await pending);
        Assert.Equal("runtime-recovery-read-timeout",timed.Code.ToString());Assert.Equal(1,calls);
        var occupied=Assert.IsType<GraphRuntimeSnapshotReadResult.OutcomeUnknown>(await supervisor.ReadAsync(Reader,fixture.Journal,fixture.Session));
        Assert.Equal("runtime-recovery-read-occupied",occupied.Code.ToString());Assert.Equal(1,calls);
        first.SetResult(new GraphRuntimeSnapshotReadResult.OutcomeUnknown(new BoundedAscii("late"),0,null));
        GraphRuntimeSnapshotReadResult releasedRead;do{await Task.Yield();releasedRead=await supervisor.ReadAsync(Reader,fixture.Journal,fixture.Session);}
        while(releasedRead is GraphRuntimeSnapshotReadResult.OutcomeUnknown {Code:var code}&&code.ToString()=="runtime-recovery-read-occupied");
        var released=Assert.IsType<GraphRuntimeSnapshotReadResult.OutcomeUnknown>(releasedRead);
        Assert.Equal("released",released.Code.ToString());Assert.Equal(2,calls);
    }

    [Fact]
    public async Task PostCommandRecoveryTimeout_PreservesExactPendingAndOccupiedRetryDoesNotStartAnotherRead()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var expected=await fixture.AppendCommandAsync(request.Command);var time=new ManualTimeProvider();
        var supervisor=new GraphRuntimeRecoveryReadSupervisor(time);
        var blocked=new TaskCompletionSource<GraphRuntimeSnapshotReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads=0;
        async ValueTask<GraphRuntimeSnapshotReadResult> Reader(
            IAuthorityJournalV1 journal,SessionAuthorityStampV1 session,CancellationToken cancellationToken)
        {
            if(Interlocked.Increment(ref reads)==1)
                return await GraphRuntimeSnapshotReader.ReadAsync(journal,session,cancellationToken);
            return await blocked.Task;
        }
        var effects=new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.NotObserved());
        var firstTask=GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal,effects,request,Reader,
            static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null),supervisor).AsTask();
        while(Volatile.Read(ref reads)<2)await Task.Yield();
        time.Advance(GraphRuntimeAdmissionCoordinator.RecoveryReadTimeout);
        var first=Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(await firstTask);
        Assert.Equal("runtime-recovery-read-timeout",first.Code.ToString());Assert.NotNull(first.Pending);
        Assert.Equal(expected.Position,first.Pending!.Operation.CommandEnvelope.Position);AssertExactIdentity(first,request);

        var second=Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal,new RecordingEffectPort(),request,Reader,
                static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null),supervisor));
        Assert.Equal("runtime-recovery-read-occupied",second.Code.ToString());AssertExactIdentity(second,request);
        Assert.Equal(2,reads);blocked.SetResult(await GraphRuntimeSnapshotReader.ReadAsync(fixture.Journal,fixture.Session));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdvancedUnknownWithoutMatchingPending_DoesNotReuseStaleVerifiedPending(bool reportsOtherPending)
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        _=await fixture.AppendCommandAsync(request.Command);
        PendingGraphRuntimeCommand? otherPending=null;
        if(reportsOtherPending)
        {
            var otherFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var otherRequest=Request(otherFixture);
            _=await otherFixture.AppendCommandAsync(otherRequest.Command);
            var otherVerified=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(
                await GraphRuntimeSnapshotReader.ReadAsync(otherFixture.Journal,otherFixture.Session));
            otherPending=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(otherVerified.Fold).Pending;
            Assert.NotNull(otherPending);
        }
        var reads=0;
        async ValueTask<GraphRuntimeSnapshotReadResult> Reader(
            IAuthorityJournalV1 journal,SessionAuthorityStampV1 session,CancellationToken cancellationToken)
        {
            if(Interlocked.Increment(ref reads)==1)
                return await GraphRuntimeSnapshotReader.ReadAsync(journal,session,cancellationToken);
            return new GraphRuntimeSnapshotReadResult.OutcomeUnknown(new BoundedAscii("advanced-read-unknown"),
                fixture.Installation.Position.Sequence+2,otherPending);
        }
        var result=Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal,
                new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.NotObserved()),request,Reader,
                static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null)));

        Assert.Equal("advanced-read-unknown",result.Code.ToString());
        Assert.Equal(fixture.Installation.Position.Sequence+2,result.LastVerified);
        Assert.Null(result.Pending);AssertExactIdentity(result,request);Assert.Equal(2,reads);
    }

    [Fact]
    public async Task AttemptEightFinalReadTimeout_PreservesVerifiedPendingAndOccupiedRetryStartsNoRead()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var journal=new ConflictRangeJournal(fixture.Journal,2,8);var time=new ManualTimeProvider();
        var supervisor=new GraphRuntimeRecoveryReadSupervisor(time);
        var blocked=new TaskCompletionSource<GraphRuntimeSnapshotReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads=0;
        async ValueTask<GraphRuntimeSnapshotReadResult> Reader(
            IAuthorityJournalV1 source,SessionAuthorityStampV1 session,CancellationToken cancellationToken)
        {
            if(Interlocked.Increment(ref reads)<9)
                return await GraphRuntimeSnapshotReader.ReadAsync(source,session,cancellationToken);
            return await blocked.Task;
        }
        var effects=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([49]));
        var firstTask=GraphRuntimeAdmissionCoordinator.AdmitAsync(journal,effects,request,Reader,
            static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null),supervisor).AsTask();
        while(Volatile.Read(ref reads)<9)await Task.Yield();
        time.Advance(GraphRuntimeAdmissionCoordinator.RecoveryReadTimeout);
        var first=Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(await firstTask);
        Assert.Equal("runtime-recovery-read-timeout",first.Code.ToString());
        Assert.Equal(fixture.Installation.Position.Sequence+1,first.LastVerified);Assert.NotNull(first.Pending);
        Assert.Equal(request.Command.OperationId,first.Pending!.Operation.OperationId);AssertExactIdentity(first,request);
        Assert.Equal(8,journal.AppendCalls);Assert.Single(effects.Executions);

        var second=Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(journal,new RecordingEffectPort(),request,Reader,
                static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null),supervisor));
        Assert.Equal("runtime-recovery-read-occupied",second.Code.ToString());AssertExactIdentity(second,request);
        Assert.Equal(9,reads);blocked.SetResult(await GraphRuntimeSnapshotReader.ReadAsync(fixture.Journal,fixture.Session));
    }

    [Fact]
    public void OutcomeUnknown_RequiresCompleteExplicitEffectIdentity()
    {
        var operation=OperationId.Create();var hash=Hash256.FromBytes(Enumerable.Repeat((byte)7,32).ToArray());
        var complete=new GraphRuntimeAdmissionResult.OutcomeUnknown(new BoundedAscii("unknown"),0,null,
            operation,GraphRuntimeCommandKind.Activate,hash);
        Assert.Equal(operation,complete.OperationId);Assert.Equal(hash,complete.RequestHash);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.OutcomeUnknown(
            new BoundedAscii("unknown"),0,null,operation));
    }

    [Fact]
    public async Task AttemptEight_PerformsOneMandatoryFinalReadForDurableAndUnresolvedResult()
    {
        var durableFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var durableRequest=Request(durableFixture);
        var durableJournal=new ConflictRangeJournal(durableFixture.Journal,2,7);var durablePort=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([12]));
        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(durableJournal,durablePort,durableRequest));
        Assert.Equal(8,durableJournal.AppendCalls);Assert.Single(durablePort.Executions);

        var unresolvedFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var unresolvedRequest=Request(unresolvedFixture);
        var unresolvedJournal=new ConflictRangeJournal(unresolvedFixture.Journal,2,8);var unresolvedPort=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([13]));
        Assert.IsType<GraphRuntimeAdmissionResult.RetryRequired>(await Admit(unresolvedJournal,unresolvedPort,unresolvedRequest));
        Assert.Equal(8,unresolvedJournal.AppendCalls);Assert.Single(unresolvedPort.Executions);
    }

    [Fact]
    public async Task CancellationDuringEffect_DoesNotEraseCommandOrRepeatExecution()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        using var cancellation=new CancellationTokenSource();var port=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([14]));
        port.BeforeExecute=cancellation.Cancel;
        var result=await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal,port,request,
            GraphRuntimeSnapshotReader.ReadAsync,static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null),cancellation.Token);
        Assert.True(result is GraphRuntimeAdmissionResult.Applied or GraphRuntimeAdmissionResult.AlreadyAdmitted,result.ToString());
        Assert.Single(port.Executions);Assert.Equal(fixture.Installation.Position.Sequence+2,Head(fixture.Journal,fixture.Session));
    }

    [Fact]
    public async Task CommandAppendReportedContradictory_IsStillReconciledBeforeClassification()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var journal=new ContradictoryOnceJournal(fixture.Journal);var port=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([15]));
        var result=await Admit(journal,port,request);
        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(result);Assert.Equal(3,journal.AppendCalls);Assert.Single(port.Executions);
    }

    [Fact]
    public async Task PostCommandRaceWithCompetingPending_SanitizesPendingAndKeepsRequestedIdentity()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);var competitor=Request(fixture);
        var reads=0;GraphRuntimeAdmissionCoordinator.SnapshotReader reader=async(j,s,ct)=>
        {
            reads++;if(reads==2){var competing=await fixture.AppendCommandAsync(competitor.Command);var actual=await GraphRuntimeSnapshotReader.ReadAsync(j,s,ct);return actual;}
            return await GraphRuntimeSnapshotReader.ReadAsync(j,s,ct);
        };
        var result=Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(await GraphRuntimeAdmissionCoordinator.AdmitAsync(
            fixture.Journal,new RecordingEffectPort(),request,reader,static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null)));
        Assert.Null(result.Pending);Assert.Equal(request.Command.OperationId,result.OperationId);
        Assert.Equal(request.Command.Kind,result.Kind);Assert.Equal(request.Command.EffectRequestHash,result.RequestHash);
    }

    [Fact]
    public async Task RecoverySupervisor_CompletionWinsAtExactThirtySecondBoundary()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var time=new ManualTimeProvider();
        var supervisor=new GraphRuntimeRecoveryReadSupervisor(time);var completion=new TaskCompletionSource<GraphRuntimeSnapshotReadResult>();var calls=0;
        ValueTask<GraphRuntimeSnapshotReadResult> Reader(IAuthorityJournalV1 _,SessionAuthorityStampV1 __,CancellationToken ___)
        {Interlocked.Increment(ref calls);return new(completion.Task);}
        var read=supervisor.ReadAsync(Reader,fixture.Journal,fixture.Session).AsTask();while(Volatile.Read(ref calls)==0)await Task.Yield();
        var expected=new GraphRuntimeSnapshotReadResult.OutcomeUnknown(new BoundedAscii("boundary-complete"),0,null);
        time.BeforeTimers=()=>completion.SetResult(expected);time.Advance(GraphRuntimeAdmissionCoordinator.RecoveryReadTimeout);
        Assert.Same(expected,await read);
    }

    [Fact]
    public async Task RetireHappyPath_AndSecondActivateConflictUseExactDurableFacts()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var activate=Request(fixture);
        var activation=Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fixture.Journal,
            new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([21])),activate));
        var retireOperation=OperationId.Create();var retireCommand=new GraphRuntimeCommand.Retire(retireOperation,
            activation.Snapshot.LastRuntimeFact,activation.Snapshot.ActivationFact,GraphRuntimeEffectHashes.Retire(
                fixture.Session,retireOperation,activation.Snapshot.ActivationFact));
        var retireRequest=new GraphRuntimeAdmissionRequest(retireCommand,fixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create(),operationId:retireOperation),new UtcInstant(60));
        var retired=Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fixture.Journal,
            new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([22])),retireRequest));
        Assert.Equal(GraphRuntimePhase.Retired,retired.Snapshot.Phase);

        var conflictFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var first=Request(conflictFixture);
        _=await Admit(conflictFixture.Journal,new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([23])),first);
        var second=Request(conflictFixture);var zeroEffect=new RecordingEffectPort();
        Assert.IsType<GraphRuntimeAdmissionResult.Conflict>(await Admit(conflictFixture.Journal,zeroEffect,second));
        Assert.Empty(zeroEffect.Calls);
    }

    [Fact]
    public async Task InvalidActivationProof_IsDurablyRejectedWithoutEffect()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();
        var wrongFingerprint=Hash256.FromBytes(Enumerable.Repeat((byte)31,32).ToArray());
        var command=new GraphRuntimeCommand.Activate(operation,fixture.Installation.Position,fixture.Installation.Position,
            wrongFingerprint,fixture.GraphGeneration,fixture.Grant.CurrentFact,GraphRuntimeEffectHashes.Activate(
                fixture.Session,operation,fixture.Installation.Position,wrongFingerprint,fixture.GraphGeneration,fixture.Grant.CurrentFact));
        var request=new GraphRuntimeAdmissionRequest(command,fixture.Authority,new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(61));
        var port=new RecordingEffectPort();var rejected=Assert.IsType<GraphRuntimeAdmissionResult.Rejected>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal,port,request,GraphRuntimeSnapshotReader.ReadAsync,
                static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null)));
        Assert.Equal("runtime-activation-proof-invalid",rejected.SafeCode.ToString());Assert.Empty(port.Calls);
    }

    [Fact]
    public async Task StaleAuthorityAndFullOperationTable_AreNotAdmittedBeforeAppend()
    {
        var staleFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();var staleGraph=GraphGenerationId.Create();
        var staleAuthority=ExpectedAuthorityVectorV1.Create(staleFixture.Session,[new AuthorityAxisValueV1.Graph(staleGraph)]);
        var staleCommand=new GraphRuntimeCommand.Activate(operation,staleFixture.Installation.Position,staleFixture.Installation.Position,
            staleFixture.Plan.Fingerprint,staleGraph,staleFixture.Grant.CurrentFact,GraphRuntimeEffectHashes.Activate(staleFixture.Session,
                operation,staleFixture.Installation.Position,staleFixture.Plan.Fingerprint,staleGraph,staleFixture.Grant.CurrentFact));
        var staleRequest=new GraphRuntimeAdmissionRequest(staleCommand,staleAuthority,new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(62));
        var staleHead=Head(staleFixture.Journal,staleFixture.Session);Assert.IsType<GraphRuntimeAdmissionResult.NotAdmitted>(
            await Admit(staleFixture.Journal,new RecordingEffectPort(),staleRequest));Assert.Equal(staleHead,Head(staleFixture.Journal,staleFixture.Session));

        var fullFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var existing=Request(fullFixture);_=await fullFixture.AppendCommandAsync(existing.Command);
        var folded=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(await GraphRuntimeSnapshotReader.ReadAsync(fullFixture.Journal,fullFixture.Session));
        var current=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(folded.Fold);Assert.NotNull(current.Pending);var row=current.Pending.Operation;
        var full=new GraphRuntimeJournalFoldResult.Current(current.SnapshotThrough,current.Authority,current.Snapshot,null,
            Enumerable.Repeat(row,GraphRuntimeJournalFold.MaximumOperations).ToArray(),current.Graph);
        GraphRuntimeAdmissionCoordinator.SnapshotReader fullReader=(_,_,_)=>ValueTask.FromResult<GraphRuntimeSnapshotReadResult>(new GraphRuntimeSnapshotReadResult.Verified(full,current.SnapshotThrough));
        var fresh=Request(fullFixture);var fullHead=Head(fullFixture.Journal,fullFixture.Session);Assert.IsType<GraphRuntimeAdmissionResult.NotAdmitted>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fullFixture.Journal,new RecordingEffectPort(),fresh,fullReader,
                static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null)));Assert.Equal(fullHead,Head(fullFixture.Journal,fullFixture.Session));
    }

    [Fact]
    public async Task FreshCommand_IsRereadBeforeExecute_AndCompletedFactIsReconciled()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var request = Request(fixture); var port = new RecordingEffectPort().ThenExecute(
            new GraphRuntimeEffectExecutionResult.Completed([1, 2, 3]));
        port.BeforeExecute = () => Assert.Equal(fixture.Installation.Position.Sequence + 1,
            Head(fixture.Journal, fixture.Session));

        var result = Assert.IsType<GraphRuntimeAdmissionResult.Applied>(
            await Admit(fixture.Journal, port, request));

        Assert.Equal(["E"], port.Calls); Assert.Single(port.Executions); Assert.Empty(port.Queries);
        Assert.Equal(result.CommandFact.Position.Sequence + 1, result.ResultFact.Position.Sequence);
        Assert.Equal(GraphRuntimeEffectHashes.Receipt(fixture.Session, request.Command.Kind,
            request.Command.OperationId, request.Command.EffectRequestHash, [1, 2, 3]), result.ReceiptHash);
    }

    [Fact]
    public async Task RecoveredPendingCommand_QueriesFirst_AndNeverExecutesWhenCompleted()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var command = await fixture.AppendCommandAsync(request.Command);
        var port = new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.Completed([9]));

        var result = Assert.IsType<GraphRuntimeAdmissionResult.Applied>(
            await Admit(fixture.Journal, port, request));

        Assert.Equal(["Q"], port.Calls); Assert.Empty(port.Executions); Assert.Single(port.Queries);
        Assert.Equal(command.Position, result.CommandFact.Position);
        Assert.Equal(request.Command.OperationId, port.Queries[0].OperationId);
        Assert.Equal(request.Command.Kind, port.Queries[0].Kind);
        Assert.Equal(request.Command.EffectRequestHash, port.Queries[0].RequestHash);
    }

    [Fact]
    public async Task RecoveredNotObserved_IsTheOnlyQueryArmThatPermitsExecution()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        _ = await fixture.AppendCommandAsync(request.Command);
        var port = new RecordingEffectPort()
            .ThenQuery(new GraphRuntimeEffectQueryResult.NotObserved())
            .ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([7]));

        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fixture.Journal, port, request));

        Assert.Equal(["Q", "E"], port.Calls); Assert.Single(port.Queries); Assert.Single(port.Executions);
    }

    [Fact]
    public async Task ExecuteRefused_AdmitsRejected_WhileUnknownQueriesBeforeRetry()
    {
        var refusedFixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var refusedRequest = Request(refusedFixture);
        var refusedPort = new RecordingEffectPort().ThenExecute(
            new GraphRuntimeEffectExecutionResult.Refused(new BoundedAscii("provider-refused")));
        var rejected = Assert.IsType<GraphRuntimeAdmissionResult.Rejected>(
            await Admit(refusedFixture.Journal, refusedPort, refusedRequest));
        Assert.Equal("provider-refused", rejected.SafeCode.ToString()); Assert.Equal(["E"], refusedPort.Calls);

        var unknownFixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var unknownRequest = Request(unknownFixture);
        var unknownPort = new RecordingEffectPort()
            .ThenExecute(new GraphRuntimeEffectExecutionResult.OutcomeUnknown(new BoundedAscii("timeout")))
            .ThenQuery(new GraphRuntimeEffectQueryResult.Completed([4]));
        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(
            await Admit(unknownFixture.Journal, unknownPort, unknownRequest));
        Assert.Equal(["E", "Q"], unknownPort.Calls);
    }

    [Fact]
    public async Task CommandCommitThenThrow_IsRecoveredByQueryWithoutReexecution()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var journal = new CommitThenThrowJournal(fixture.Journal, 1);
        var port = new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.Completed([5]));

        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(journal, port, request));

        Assert.Equal(["Q"], port.Calls); Assert.Equal(2, journal.AppendCalls);
        Assert.Equal(fixture.Installation.Position.Sequence + 2, Head(fixture.Journal, fixture.Session));
    }

    [Fact]
    public async Task ResultCommitThenThrow_ReconcilesDurableFactWithoutSecondEffect()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var journal = new CommitThenThrowJournal(fixture.Journal, 2);
        var port = new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([6]));

        var result = await Admit(journal, port, request);

        Assert.True(result is GraphRuntimeAdmissionResult.Applied or GraphRuntimeAdmissionResult.AlreadyAdmitted,
            result.ToString()); Assert.Equal(["E"], port.Calls); Assert.Equal(2, journal.AppendCalls);
        Assert.Equal(fixture.Installation.Position.Sequence + 2, Head(fixture.Journal, fixture.Session));
    }

    [Fact]
    public async Task ProvenAbsentThrowBeforeCommit_AndSessionConflict_RetryAsFreshWithoutQuery()
    {
        var thrownFixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var thrownRequest = Request(thrownFixture);
        var thrown = new ThrowBeforeCommitJournal(thrownFixture.Journal, 1);
        var thrownPort = new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([2]));
        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(thrown, thrownPort, thrownRequest));
        Assert.Equal(["E"], thrownPort.Calls);

        var conflictFixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var conflictRequest = Request(conflictFixture);
        var conflict = new SessionConflictJournal(conflictFixture.Journal, 1);
        var conflictPort = new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([3]));
        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(conflict, conflictPort, conflictRequest));
        Assert.Equal(["E"], conflictPort.Calls);
    }

    [Fact]
    public async Task QueryContradictoryAndUnknown_AreClosedAndNeverAppendResult()
    {
        var contradictoryFixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var contradictoryRequest = Request(contradictoryFixture); _ = await contradictoryFixture.AppendCommandAsync(contradictoryRequest.Command);
        var contradictoryPort = new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.Contradictory());
        Assert.IsType<GraphRuntimeAdmissionResult.ContradictoryDuplicate>(
            await Admit(contradictoryFixture.Journal, contradictoryPort, contradictoryRequest));
        Assert.Equal(contradictoryFixture.Installation.Position.Sequence + 1,
            Head(contradictoryFixture.Journal, contradictoryFixture.Session));

        var unknownFixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var unknownRequest = Request(unknownFixture); _ = await unknownFixture.AppendCommandAsync(unknownRequest.Command);
        var unknownPort = new RecordingEffectPort().ThenQuery(
            new GraphRuntimeEffectQueryResult.OutcomeUnknown(new BoundedAscii("query-unknown")));
        var unknown = Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(
            await Admit(unknownFixture.Journal, unknownPort, unknownRequest));
        Assert.Equal("query-unknown", unknown.Code.ToString()); Assert.NotNull(unknown.Pending);
        Assert.Equal(unknownFixture.Installation.Position.Sequence + 1,
            Head(unknownFixture.Journal, unknownFixture.Session));
    }

    [Fact]
    public async Task ResultSessionConflict_RereadsPendingAndRetriesWithoutRepeatingEffect()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var journal = new ConflictOnAppendJournal(fixture.Journal, 2);
        var port = new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([8]));

        var result = Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(journal, port, request));

        Assert.Equal(["E"], port.Calls); Assert.Equal(3, journal.AppendCalls);
        Assert.Equal(result.CommandFact.Position.Sequence + 1, result.ResultFact.Position.Sequence);
    }

    [Fact]
    public async Task DifferentRequestWhileAnotherOperationIsPending_ReturnsPendingUncertaintyWithoutEffectOrAppend()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync();
        var existing = Request(fixture); _ = await fixture.AppendCommandAsync(existing.Command);
        var competing = Request(fixture); var port = new RecordingEffectPort();
        var head = Head(fixture.Journal, fixture.Session);

        var result = Assert.IsType<GraphRuntimeAdmissionResult.OutcomeUnknown>(
            await Admit(fixture.Journal, port, competing));

        Assert.Equal("runtime-command-pending", result.Code.ToString()); Assert.NotNull(result.Pending);
        Assert.Empty(port.Calls); Assert.Equal(head, Head(fixture.Journal, fixture.Session));
    }

    [Fact]
    public async Task CancellationBeforeFirstCommandAppend_IsNotAdmittedAndLeavesNoPendingFact()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var port = new RecordingEffectPort(); var before = Head(fixture.Journal, fixture.Session);

        var result = Assert.IsType<GraphRuntimeAdmissionResult.NotAdmitted>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal, port, request,
                GraphRuntimeSnapshotReader.ReadAsync,
                static (_, _, _, _, _) => ValueTask.FromResult<BoundedAscii?>(null), cancellation.Token));

        Assert.Equal("runtime-cancelled-before-command", result.Code.ToString()); Assert.Empty(port.Calls);
        Assert.Equal(before, Head(fixture.Journal, fixture.Session));
    }

    [Fact]
    public async Task ActivationPreflightRunsOnceBeforeC_NotAgainDuringCommandOrResultReconciliation()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var preflightCalls = 0; var port = new RecordingEffectPort().ThenExecute(
            new GraphRuntimeEffectExecutionResult.Completed([10]));

        var result = await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal, port, request,
            GraphRuntimeSnapshotReader.ReadAsync, (_, command, authority, current, _) =>
            {
                preflightCalls++; Assert.Same(request.Command, command); Assert.Same(request.ExpectedAuthority, authority);
                Assert.Equal(fixture.Installation.Position.Sequence, current.SnapshotThrough);
                return ValueTask.FromResult<BoundedAscii?>(null);
            });

        Assert.IsType<GraphRuntimeAdmissionResult.Applied>(result); Assert.Equal(1, preflightCalls);
    }

    [Fact]
    public async Task VerifiedTerminalReads_MapExactRuntimeAndClaimedGraphReplacementEvidence()
    {
        var fixture = await GraphRuntimeReducerTests.Fixture.CreateAsync(); var request = Request(fixture);
        var effects = new RecordingEffectPort(); var runtimeNext = RuntimeGenerationId.Create();
        var runtimeFold = new GraphRuntimeJournalFoldResult.RuntimeReplaced(runtimeNext, 1, 1,
            null, null, null);
        GraphRuntimeAdmissionCoordinator.SnapshotReader runtimeReader = (_, _, _) =>
            ValueTask.FromResult<GraphRuntimeSnapshotReadResult>(
                new GraphRuntimeSnapshotReadResult.Verified(runtimeFold, 1));
        var runtime = Assert.IsType<GraphRuntimeAdmissionResult.RuntimeReplaced>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal, effects, request,
                runtimeReader, static (_, _, _, _, _) => ValueTask.FromResult<BoundedAscii?>(null)));
        Assert.Equal(runtimeNext, runtime.Next); Assert.Equal(1, runtime.TerminatedAt);

        var graphNext = GraphGenerationId.Create(); var graphBytes = new byte[16];
        Assert.True(graphNext.TryWriteBytes(graphBytes)); var stableNext = StableId128.FromBytes(graphBytes);
        var authorityFold = new GraphRuntimeJournalFoldResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph, stableNext, 2, 2, null, null, null);
        GraphRuntimeAdmissionCoordinator.SnapshotReader authorityReader = (_, _, _) =>
            ValueTask.FromResult<GraphRuntimeSnapshotReadResult>(
                new GraphRuntimeSnapshotReadResult.Verified(authorityFold, 2));
        var authority = Assert.IsType<GraphRuntimeAdmissionResult.AuthorityGenerationReplaced>(
            await GraphRuntimeAdmissionCoordinator.AdmitAsync(fixture.Journal, effects, request,
                authorityReader, static (_, _, _, _, _) => ValueTask.FromResult<BoundedAscii?>(null)));
        Assert.Equal(AuthorityAxisId.Graph, authority.Axis); Assert.Equal(stableNext, authority.Next);
        Assert.Empty(effects.Calls);
    }

    [Fact]
    public async Task ClaimedAxisReplacementBetweenExecuteAndFact_PreservesCompletedReceipt()
    {
        var fixture=await GraphRuntimeJournalFoldTests.ClaimedFixture.CreateAsync();var request=Request(fixture);
        var receipt=new byte[]{21,22,23};var port=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed(receipt));
        var next=ActivityGenerationId.Create();port.BeforeExecute=()=>fixture.AppendTransitionAsync(AuthorityAxisId.Activity,
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(fixture.Activity),
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(next)).GetAwaiter().GetResult();

        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.AuthorityGenerationReplaced>(await Admit(fixture.Journal,port,request));

        Assert.Equal(["E"],port.Calls);Assert.Equal(GraphRuntimeEffectHashes.Receipt(fixture.Session,
            request.Command.Kind,request.Command.OperationId,request.Command.EffectRequestHash,receipt),Receipt(terminal.TerminalResultFact!));
        Assert.Equal(terminal.TerminalCommandFact!.Position.Sequence+2,terminal.TerminalResultFact!.Position.Sequence);
    }

    [Fact]
    public async Task ClaimedAxisReplacementBetweenExecuteAndFact_PreservesRefusalResult()
    {
        var fixture=await GraphRuntimeJournalFoldTests.ClaimedFixture.CreateAsync();var request=Request(fixture);
        var port=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Refused(new BoundedAscii("provider-refused-after-race")));
        var next=ActivityGenerationId.Create();port.BeforeExecute=()=>fixture.AppendTransitionAsync(AuthorityAxisId.Activity,
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(fixture.Activity),
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(next)).GetAwaiter().GetResult();

        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.AuthorityGenerationReplaced>(await Admit(fixture.Journal,port,request));

        Assert.Equal(["E"],port.Calls);Assert.Equal("provider-refused-after-race",SafeCode(terminal.TerminalResultFact!));
        Assert.Equal(terminal.TerminalCommandFact!.Position.Sequence+2,terminal.TerminalResultFact!.Position.Sequence);
    }

    [Fact]
    public async Task ClaimedAxisReplacementBetweenCommandAndEffect_QueryCompletedPreservesReceiptWithoutExecute()
    {
        var fixture=await GraphRuntimeJournalFoldTests.ClaimedFixture.CreateAsync();var request=Request(fixture);
        var next=ActivityGenerationId.Create();var journal=new AfterFirstAppendJournal(fixture.Journal,()=>fixture.AppendTransitionAsync(
            AuthorityAxisId.Activity,GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(fixture.Activity),
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(next)));
        var receipt=new byte[]{31,32};var port=new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.Completed(receipt));

        var result=await Admit(journal,port,request);
        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.AuthorityGenerationReplaced>(result);

        Assert.Equal(["Q"],port.Calls);Assert.Empty(port.Executions);
        Assert.Equal(GraphRuntimeEffectHashes.Receipt(fixture.Session,request.Command.Kind,
            request.Command.OperationId,request.Command.EffectRequestHash,receipt),Receipt(terminal.TerminalResultFact!));
    }

    [Fact]
    public async Task ClaimedAxisReplacementBetweenCommandAndEffect_QueryNotObservedRendersGenerationReplacedWithoutExecute()
    {
        var fixture=await GraphRuntimeJournalFoldTests.ClaimedFixture.CreateAsync();var request=Request(fixture);
        var next=ActivityGenerationId.Create();var journal=new AfterFirstAppendJournal(fixture.Journal,()=>fixture.AppendTransitionAsync(
            AuthorityAxisId.Activity,GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(fixture.Activity),
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(next)));
        var port=new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.NotObserved());

        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.AuthorityGenerationReplaced>(await Admit(journal,port,request));

        Assert.Equal(["Q"],port.Calls);Assert.Empty(port.Executions);Assert.Equal(AuthorityAxisId.Activity,terminal.Axis);
        Assert.NotNull(terminal.TerminalCommandFact);Assert.NotNull(terminal.TerminalResultFact);
        Assert.True(GraphRuntimeCodecs.TryDecodeOuter(terminal.TerminalResultFact!.PayloadMemory,out var outer));
        Assert.True(GraphRuntimeCodecs.TryDecodeFact(outer!.Body,out var fact));
        Assert.Equal(GraphRuntimeOutcome.GenerationReplaced,fact!.Outcome);Assert.Equal("generation-replaced",fact.SafeCode?.ToString());
    }

    [Fact]
    public async Task RetireHappyPath_AdmitsRetiredSnapshotAndExecutesExactlyOnce()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var activate=Request(fixture);
        var activation=Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fixture.Journal,
            new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([41])),activate));
        var operation=OperationId.Create();var command=new GraphRuntimeCommand.Retire(operation,
            activation.ResultFact.Position,activation.ResultFact.Position,
            GraphRuntimeEffectHashes.Retire(fixture.Session,operation,activation.ResultFact.Position));
        var request=new GraphRuntimeAdmissionRequest(command,fixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(51));
        var effects=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([42]));

        var retired=Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fixture.Journal,effects,request));

        Assert.Equal(GraphRuntimePhase.Retired,retired.Snapshot.Phase);Assert.Equal(operation,retired.Snapshot.Retirement!.OperationId);
        Assert.Equal(["E"],effects.Calls);Assert.Equal(retired.CommandFact.Position.Sequence+1,retired.ResultFact.Position.Sequence);
    }

    [Fact]
    public async Task DeterministicRejectedAndConflict_AppendTerminalFactsWithoutCallingEffectPort()
    {
        var rejectedFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var rejectOperation=OperationId.Create();
        var rejectCommand=new GraphRuntimeCommand.Retire(rejectOperation,rejectedFixture.Installation.Position,
            rejectedFixture.Installation.Position,GraphRuntimeEffectHashes.Retire(rejectedFixture.Session,
                rejectOperation,rejectedFixture.Installation.Position));
        var rejectRequest=new GraphRuntimeAdmissionRequest(rejectCommand,rejectedFixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create(),operationId:rejectOperation),new UtcInstant(52));
        var rejectedEffects=new RecordingEffectPort();
        var rejected=Assert.IsType<GraphRuntimeAdmissionResult.Rejected>(
            await Admit(rejectedFixture.Journal,rejectedEffects,rejectRequest));
        Assert.Equal("runtime-not-active",rejected.SafeCode.ToString());Assert.Empty(rejectedEffects.Calls);

        var conflictFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var conflictOperation=OperationId.Create();
        var stale=new JournalPositionV1(conflictFixture.Session,conflictFixture.Installation.Position.Sequence-1);
        var conflictCommand=new GraphRuntimeCommand.Activate(conflictOperation,stale,conflictFixture.Installation.Position,
            conflictFixture.Plan.Fingerprint,conflictFixture.GraphGeneration,conflictFixture.Grant.CurrentFact,
            GraphRuntimeEffectHashes.Activate(conflictFixture.Session,conflictOperation,
                conflictFixture.Installation.Position,conflictFixture.Plan.Fingerprint,
                conflictFixture.GraphGeneration,conflictFixture.Grant.CurrentFact));
        var conflictRequest=new GraphRuntimeAdmissionRequest(conflictCommand,conflictFixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create(),operationId:conflictOperation),new UtcInstant(53));
        var conflictEffects=new RecordingEffectPort();
        var conflict=Assert.IsType<GraphRuntimeAdmissionResult.Conflict>(
            await Admit(conflictFixture.Journal,conflictEffects,conflictRequest));
        Assert.Equal(conflictFixture.Installation.Position,conflict.ActualPredecessor);Assert.Empty(conflictEffects.Calls);
    }

    [Fact]
    public async Task RuntimeReplacementBetweenCommandAndEffect_StopsWithoutOldSessionEffectOrResultFact()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var effects=new RecordingEffectPort();var reads=0;PendingGraphRuntimeCommand? pending=null;
        GraphRuntimeAdmissionCoordinator.SnapshotReader reader=async(j,s,ct)=>
        {
            var actual=await GraphRuntimeSnapshotReader.ReadAsync(j,s,ct);reads++;
            if(reads!=2)return actual;
            var verified=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(actual);pending=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(verified.Fold).Pending;
            var through=verified.SnapshotThrough+1;return new GraphRuntimeSnapshotReadResult.Verified(
                new GraphRuntimeJournalFoldResult.RuntimeReplaced(RuntimeGenerationId.Create(),through,through,
                    null,pending,null),through);
        };

        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.RuntimeReplaced>(await GraphRuntimeAdmissionCoordinator.AdmitAsync(
            fixture.Journal,effects,request,reader,static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null)));

        Assert.NotNull(terminal.Pending);Assert.Null(terminal.TerminalResultFact);Assert.Empty(effects.Calls);
        Assert.Equal(fixture.Installation.Position.Sequence+1,Head(fixture.Journal,fixture.Session));
    }

    [Fact]
    public async Task RuntimeReplacementBetweenEffectAndFact_DoesNotAppendOldSessionResult()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var journal=new ConflictOnAppendJournal(fixture.Journal,2);
        var effects=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([43]));
        var reads=0;PendingGraphRuntimeCommand? pending=null;
        GraphRuntimeAdmissionCoordinator.SnapshotReader reader=async(j,s,ct)=>
        {
            var actual=await GraphRuntimeSnapshotReader.ReadAsync(j,s,ct);reads++;
            if(reads==2){var verified=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(actual);pending=Assert.IsType<GraphRuntimeJournalFoldResult.Current>(verified.Fold).Pending;return actual;}
            if(reads!=3)return actual;
            var current=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(actual);var through=current.SnapshotThrough+1;
            return new GraphRuntimeSnapshotReadResult.Verified(new GraphRuntimeJournalFoldResult.RuntimeReplaced(
                RuntimeGenerationId.Create(),through,through,null,pending,null),through);
        };

        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.RuntimeReplaced>(await GraphRuntimeAdmissionCoordinator.AdmitAsync(
            journal,effects,request,reader,static(_,_,_,_,_)=>ValueTask.FromResult<BoundedAscii?>(null)));

        Assert.Equal(["E"],effects.Calls);Assert.Null(terminal.TerminalResultFact);
        Assert.Equal(fixture.Installation.Position.Sequence+1,Head(fixture.Journal,fixture.Session));
    }

    [Fact]
    public async Task StaleAuthorityAndFullOperationLedger_AreNotAdmittedWithoutAppendOrEffect()
    {
        var staleFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var baseRequest=Request(staleFixture);
        var staleAuthority=ExpectedAuthorityVectorV1.Create(staleFixture.Session,
            [new AuthorityAxisValueV1.Graph(staleFixture.GraphGeneration),
             new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create())]);
        var staleRequest=new GraphRuntimeAdmissionRequest(baseRequest.Command,staleAuthority,
            baseRequest.Correlation,baseRequest.ObservedAt);var staleEffects=new RecordingEffectPort();
        var staleHead=Head(staleFixture.Journal,staleFixture.Session);
        var stale=Assert.IsType<GraphRuntimeAdmissionResult.NotAdmitted>(
            await Admit(staleFixture.Journal,staleEffects,staleRequest));
        Assert.Equal("authority-vector-stale",stale.Code.ToString());Assert.Empty(staleEffects.Calls);
        Assert.Equal(staleHead,Head(staleFixture.Journal,staleFixture.Session));

        var fullFixture=await GraphRuntimeReducerTests.Fixture.CreateAsync(1024);var activation=Request(fullFixture);
        var active=Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fullFixture.Journal,
            new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([44])),activation));
        for(var index=1;index<GraphRuntimeJournalFold.MaximumOperations;index++)
        {
            var operation=OperationId.Create();var command=new GraphRuntimeCommand.Retire(operation,
                fullFixture.Installation.Position,fullFixture.Installation.Position,
                GraphRuntimeEffectHashes.Retire(fullFixture.Session,operation,fullFixture.Installation.Position));
            var request=new GraphRuntimeAdmissionRequest(command,fullFixture.Authority,
                new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(55+index));
            Assert.IsType<GraphRuntimeAdmissionResult.Conflict>(await Admit(fullFixture.Journal,new RecordingEffectPort(),request));
        }
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(await GraphRuntimeSnapshotReader.ReadAsync(
            fullFixture.Journal,fullFixture.Session,CancellationToken.None));
        Assert.Equal(GraphRuntimeJournalFold.MaximumOperations,
            Assert.IsType<GraphRuntimeJournalFoldResult.Current>(verified.Fold).Operations.Select(x=>x.OperationId).Distinct().Count());
        var retireOperation=OperationId.Create();var retireCommand=new GraphRuntimeCommand.Retire(retireOperation,
            active.ResultFact.Position,active.ResultFact.Position,GraphRuntimeEffectHashes.Retire(fullFixture.Session,
                retireOperation,active.ResultFact.Position));
        var retireRequest=new GraphRuntimeAdmissionRequest(retireCommand,fullFixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create(),operationId:retireOperation),new UtcInstant(54));
        var fullEffects=new RecordingEffectPort();var fullHead=Head(fullFixture.Journal,fullFixture.Session);
        var full=Assert.IsType<GraphRuntimeAdmissionResult.NotAdmitted>(await Admit(
            fullFixture.Journal,fullEffects,retireRequest));
        Assert.Equal("runtime-operation-bound",full.Code.ToString());Assert.Empty(fullEffects.Calls);
        Assert.Equal(fullHead,Head(fullFixture.Journal,fullFixture.Session));
    }

    [Fact]
    public async Task UnclaimedTurnTransitionDuringEffect_DoesNotFenceRuntimeAndResultStillApplies()
    {
        var fixture=await GraphRuntimeJournalFoldTests.ClaimedFixture.CreateAsync();var request=Request(fixture);
        var effects=new RecordingEffectPort().ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([45]));
        var next=TurnGenerationId.Create();effects.BeforeExecute=()=>fixture.AppendTransitionAsync(AuthorityAxisId.Turn,
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(fixture.Turn),
            GraphRuntimeJournalFoldTests.ClaimedFixture.StableValue(next)).GetAwaiter().GetResult();

        var applied=Assert.IsType<GraphRuntimeAdmissionResult.Applied>(await Admit(fixture.Journal,effects,request));
        var verified=Assert.IsType<GraphRuntimeSnapshotReadResult.Verified>(await GraphRuntimeSnapshotReader.ReadAsync(
            fixture.Journal,fixture.Session,CancellationToken.None));

        Assert.IsType<GraphRuntimeJournalFoldResult.Current>(verified.Fold);Assert.Equal(["E"],effects.Calls);
        Assert.Equal(applied.CommandFact.Position.Sequence+2,applied.ResultFact.Position.Sequence);
    }

    [Fact]
    public async Task CommandCommitThenCancellationException_IsRecoveredByExactQueryWithoutReexecution()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var journal=new CommitThenCancelJournal(fixture.Journal);var effects=new RecordingEffectPort()
            .ThenQuery(new GraphRuntimeEffectQueryResult.Completed([46]));

        var result=await Admit(journal,effects,request);

        Assert.True(result is GraphRuntimeAdmissionResult.Applied or GraphRuntimeAdmissionResult.AlreadyAdmitted,result.ToString());
        Assert.Equal(["Q"],effects.Calls);Assert.Empty(effects.Executions);Assert.Equal(2,journal.AppendCalls);
    }

    [Fact]
    public async Task ResultCommitThenCancellationException_IsReconciledWithoutReexecution()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var request=Request(fixture);
        var journal=new CommitThenCancelOnAppendJournal(fixture.Journal,2);var effects=new RecordingEffectPort()
            .ThenExecute(new GraphRuntimeEffectExecutionResult.Completed([47]));

        var result=await Admit(journal,effects,request);

        Assert.True(result is GraphRuntimeAdmissionResult.Applied or GraphRuntimeAdmissionResult.AlreadyAdmitted,result.ToString());
        Assert.Equal(["E"],effects.Calls);Assert.Single(effects.Executions);Assert.Equal(2,journal.AppendCalls);
    }

    [Fact]
    public async Task RealGraphCommitBetweenCommandAndEffect_FencesRuntimeWithoutEffectOrWrongResult()
    {
        var fixture=await GraphReplacementAdmissionCoordinatorTests.Fixture.CreateAsync();var request=Request(fixture);
        var operation=OperationId.Create();var prepare=new GraphReplacementJournalCommand.Prepare(operation,
            fixture.Installation,fixture.Source.Fingerprint,fixture.Target,fixture.TargetGrant.CurrentFact,
            fixture.Authority,fixture.Observed,fixture.Deadline);
        var journal=new AfterFirstAppendJournal(fixture.Journal,async()=>
        {
            var prepared=Assert.IsType<GraphReplacementAdmissionResult.Admitted>(await GraphReplacementAdmissionCoordinator.AdmitAsync(
                fixture.Journal,fixture.Request(prepare,fixture.Authority)));
            var commit=new GraphReplacementJournalCommand.Commit(operation,prepared.Result.Position);
            var committed=Assert.IsType<GraphReplacementAdmissionResult.Admitted>(await GraphReplacementAdmissionCoordinator.AdmitAsync(
                fixture.Journal,fixture.Request(commit,fixture.Authority)));
            Assert.NotNull(committed.GraphTransition);
        });
        var effects=new RecordingEffectPort().ThenQuery(new GraphRuntimeEffectQueryResult.NotObserved());

        var terminal=Assert.IsType<GraphRuntimeAdmissionResult.AuthorityGenerationReplaced>(await Admit(journal,effects,request));

        Assert.Equal(AuthorityAxisId.Graph,terminal.Axis);Assert.Equal(["Q"],effects.Calls);Assert.Empty(effects.Executions);
        Assert.NotNull(terminal.TerminalResultFact);Assert.Equal(GraphRuntimeOutcome.GenerationReplaced,
            TerminalFact(terminal.TerminalResultFact!).Outcome);
    }

    private static GraphRuntimeAdmissionRequest Request(GraphRuntimeReducerTests.Fixture fixture)
    {
        var operation = OperationId.Create(); var installation = fixture.Installation.Position;
        var command = new GraphRuntimeCommand.Activate(operation, installation, installation,
            fixture.Plan.Fingerprint, fixture.GraphGeneration, fixture.Grant.CurrentFact,
            GraphRuntimeEffectHashes.Activate(fixture.Session, operation, installation,
                fixture.Plan.Fingerprint, fixture.GraphGeneration, fixture.Grant.CurrentFact));
        return new(command, fixture.Authority, new CorrelationEnvelopeV1(TenantId.Create(), operationId: operation),
            new UtcInstant(50));
    }

    private static GraphRuntimeAdmissionRequest Request(GraphRuntimeJournalFoldTests.ClaimedFixture fixture)
    {
        var operation=OperationId.Create();var installation=fixture.Installation.Position;
        var command=new GraphRuntimeCommand.Activate(operation,installation,installation,fixture.Plan.Fingerprint,
            fixture.Graph,fixture.Grant.CurrentFact,GraphRuntimeEffectHashes.Activate(fixture.Session,operation,
                installation,fixture.Plan.Fingerprint,fixture.Graph,fixture.Grant.CurrentFact));
        return new(command,fixture.Authority,new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(50));
    }

    private static GraphRuntimeAdmissionRequest Request(GraphReplacementAdmissionCoordinatorTests.Fixture fixture)
    {
        var operation=OperationId.Create();var command=new GraphRuntimeCommand.Activate(operation,fixture.Installation,
            fixture.Installation,fixture.Source.Fingerprint,fixture.GraphGeneration,fixture.SourceGrantFact,
            GraphRuntimeEffectHashes.Activate(fixture.Session,operation,fixture.Installation,fixture.Source.Fingerprint,
                fixture.GraphGeneration,fixture.SourceGrantFact));
        return new(command,fixture.Authority,new CorrelationEnvelopeV1(TenantId.Create(),operationId:operation),new UtcInstant(50));
    }

    private static ValueTask<GraphRuntimeAdmissionResult> Admit(IAuthorityJournalV1 journal,
        IGraphRuntimeEffectPort port, GraphRuntimeAdmissionRequest request) =>
        GraphRuntimeAdmissionCoordinator.AdmitAsync(journal, port, request,
            GraphRuntimeSnapshotReader.ReadAsync,
            static (_, _, _, _, _) => ValueTask.FromResult<BoundedAscii?>(null));

    private static long Head(IAuthorityJournalV1 journal, SessionAuthorityStampV1 session)
    {
        var batch = Assert.IsType<ReadAuthorityRangeResultV1.Batch>(journal.ReadAsync(
            new ReadAuthorityRangeV1(session, 0, long.MaxValue, 256, 1_048_576)).AsTask().GetAwaiter().GetResult());
        return batch.SnapshotThrough;
    }

    private static GraphRuntimeFact TerminalFact(AuthorityFactEnvelopeV1 envelope)
    { Assert.True(GraphRuntimeCodecs.TryDecodeOuter(envelope.PayloadMemory,out var outer));Assert.True(GraphRuntimeCodecs.TryDecodeFact(outer!.Body,out var fact));return fact!; }
    private static Hash256? Receipt(AuthorityFactEnvelopeV1 envelope)=>TerminalFact(envelope).EffectReceiptHash;
    private static string? SafeCode(AuthorityFactEnvelopeV1 envelope)=>TerminalFact(envelope).SafeCode?.ToString();
    private static void AssertExactIdentity(GraphRuntimeAdmissionResult.OutcomeUnknown result,
        GraphRuntimeAdmissionRequest request)
    {Assert.Equal(request.Command.OperationId,result.OperationId);Assert.Equal(request.Command.Kind,result.Kind);
     Assert.Equal(request.Command.EffectRequestHash,result.RequestHash);}

    private sealed class RecordingEffectPort : IGraphRuntimeEffectPort
    {
        private readonly Queue<object> _script = [];

        internal List<string> Calls { get; } = [];
        internal List<GraphRuntimeEffectRequest> Executions { get; } = [];
        internal List<GraphRuntimeEffectQuery> Queries { get; } = [];
        internal Action? BeforeExecute { get; set; }
        internal Action? BeforeQuery { get; set; }

        internal RecordingEffectPort ThenExecute(GraphRuntimeEffectExecutionResult result)
        { _script.Enqueue(result); return this; }

        internal RecordingEffectPort ThenQuery(GraphRuntimeEffectQueryResult result)
        { _script.Enqueue(result); return this; }

        internal RecordingEffectPort ThenThrow(Exception exception)
        { _script.Enqueue(exception); return this; }

        public ValueTask<GraphRuntimeEffectExecutionResult> ExecuteAsync(
            GraphRuntimeEffectRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("E"); Executions.Add(request); BeforeExecute?.Invoke();
            return ValueTask.FromResult(Next<GraphRuntimeEffectExecutionResult>());
        }

        public ValueTask<GraphRuntimeEffectQueryResult> QueryAsync(
            GraphRuntimeEffectQuery query, CancellationToken cancellationToken = default)
        {
            Calls.Add("Q"); Queries.Add(query); BeforeQuery?.Invoke();
            return ValueTask.FromResult(Next<GraphRuntimeEffectQueryResult>());
        }

        private T Next<T>()
        {
            Assert.NotEmpty(_script);
            var value = _script.Dequeue();
            if (value is Exception exception) throw exception;
            return Assert.IsAssignableFrom<T>(value);
        }
    }

    private abstract class ForwardingJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        protected IAuthorityJournalV1 Inner { get; } = inner;
        internal int AppendCalls { get; private set; }
        internal int ReadCalls { get; private set; }
        internal List<string> Calls { get; } = [];

        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++; Calls.Add($"A{AppendCalls}");
            return await AppendCoreAsync(request, cancellationToken);
        }

        public async ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++; Calls.Add($"R{ReadCalls}");
            return await Inner.ReadAsync(request, cancellationToken);
        }

        protected abstract ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken);
    }

    private sealed class CommitThenThrowJournal(IAuthorityJournalV1 inner, int appendToThrow) :
        ForwardingJournal(inner)
    {
        protected override async ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken)
        {
            var result = await Inner.AppendAsync(request, cancellationToken);
            if (AppendCalls == appendToThrow && result is AppendAuthorityResultV1.Committed)
                throw new InvalidOperationException("lost-append-ack");
            return result;
        }
    }

    private sealed class CommitThenCancelJournal(IAuthorityJournalV1 inner):ForwardingJournal(inner)
    {
        protected override async ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request,CancellationToken cancellationToken)
        {
            var result=await Inner.AppendAsync(request,CancellationToken.None);
            if(AppendCalls==1&&result is AppendAuthorityResultV1.Committed)
                throw new OperationCanceledException("lost-command-ack-after-commit");
            return result;
        }
    }
    private sealed class CommitThenCancelOnAppendJournal(IAuthorityJournalV1 inner,int targetAppend):ForwardingJournal(inner)
    {
        protected override async ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request,CancellationToken cancellationToken)
        {
            var result=await Inner.AppendAsync(request,CancellationToken.None);
            if(AppendCalls==targetAppend&&result is AppendAuthorityResultV1.Committed)
                throw new OperationCanceledException("lost-result-ack-after-commit");
            return result;
        }
    }

    private sealed class ThrowBeforeCommitJournal(IAuthorityJournalV1 inner, int appendToThrow) :
        ForwardingJournal(inner)
    {
        protected override ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken) =>
            AppendCalls == appendToThrow
                ? throw new InvalidOperationException("append-before-commit")
                : Inner.AppendAsync(request, cancellationToken);
    }

    private sealed class SessionConflictJournal(IAuthorityJournalV1 inner, int conflictCount) :
        ForwardingJournal(inner)
    {
        protected override ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken) =>
            AppendCalls <= conflictCount
                ? ValueTask.FromResult<AppendAuthorityResultV1>(new AppendAuthorityResultV1.SessionConflict(
                    request.ExpectedSessionHead, checked(request.ExpectedSessionHead + 1)))
                : Inner.AppendAsync(request, cancellationToken);
    }

    private sealed class ConflictOnAppendJournal(IAuthorityJournalV1 inner, int targetAppend) :
        ForwardingJournal(inner)
    {
        protected override ValueTask<AppendAuthorityResultV1> AppendCoreAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken) =>
            AppendCalls == targetAppend
                ? ValueTask.FromResult<AppendAuthorityResultV1>(new AppendAuthorityResultV1.SessionConflict(
                    request.ExpectedSessionHead, checked(request.ExpectedSessionHead + 1)))
                : Inner.AppendAsync(request, cancellationToken);
    }

    private sealed class ConflictRangeJournal(IAuthorityJournalV1 inner,int first,int last):ForwardingJournal(inner)
    {
        protected override ValueTask<AppendAuthorityResultV1> AppendCoreAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken)=>
            AppendCalls>=first&&AppendCalls<=last
                ?ValueTask.FromResult<AppendAuthorityResultV1>(new AppendAuthorityResultV1.SessionConflict(request.ExpectedSessionHead,request.ExpectedSessionHead+1))
                :Inner.AppendAsync(request,cancellationToken);
    }
    private sealed class AfterFirstAppendJournal(IAuthorityJournalV1 inner,Func<Task> after):ForwardingJournal(inner)
    {
        protected override async ValueTask<AppendAuthorityResultV1> AppendCoreAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken)
        {
            var result=await Inner.AppendAsync(request,cancellationToken);
            if(AppendCalls==1&&result is AppendAuthorityResultV1.Committed)await after();
            return result;
        }
    }
    private sealed class ContradictoryOnceJournal(IAuthorityJournalV1 inner):ForwardingJournal(inner)
    {
        protected override ValueTask<AppendAuthorityResultV1> AppendCoreAsync(AppendAuthorityBatchV1 request,CancellationToken cancellationToken)=>
            AppendCalls==1?ValueTask.FromResult<AppendAuthorityResultV1>(new AppendAuthorityResultV1.ContradictoryDuplicate(
                request.Facts[0].FactId,request.Facts[0].PayloadHash,request.Facts[0].PayloadHash)):Inner.AppendAsync(request,cancellationToken);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;private readonly List<Timer> _timers=[];
        internal Action? BeforeTimers{get;set;}
        public override long GetTimestamp()=>_ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        {var timer=new Timer(this,callback,state,_ticks+dueTime.Ticks,period);_timers.Add(timer);return timer;}
        internal void Advance(TimeSpan duration)
        { _ticks+=duration.Ticks;BeforeTimers?.Invoke();BeforeTimers=null;foreach(var timer in _timers.ToArray())timer.Fire(_ticks); }
        private sealed class Timer(ManualTimeProvider owner,TimerCallback callback,object? state,long due,TimeSpan period):ITimer
        {
            private bool _disposed;private long _due=due;
            public bool Change(TimeSpan dueTime,TimeSpan nextPeriod){_due=owner._ticks+dueTime.Ticks;return !_disposed;}
            public void Dispose()=>_disposed=true;public ValueTask DisposeAsync(){Dispose();return default;}
            internal void Fire(long now){if(_disposed||now<_due)return;callback(state);if(period==Timeout.InfiniteTimeSpan)Dispose();else _due=now+period.Ticks;}
        }
    }
}
