using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;
using System.Reflection;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeAdmissionResultTests
{
    [Fact]
    public async Task ExactSuccessfulTuple_ConstructsAppliedAndAlreadyAdmitted()
    {
        var (fixture,command,commandEnvelope,resultEnvelope,fact)=await SuccessfulPair();
        var request=new GraphRuntimeAdmissionRequest(command,fixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(3));
        Assert.Same(command,request.Command);
        var applied=new GraphRuntimeAdmissionResult.Applied(fact.ResultingSnapshot!,commandEnvelope,resultEnvelope,fact.EffectReceiptHash!.Value);
        Assert.Equal(resultEnvelope,applied.ResultFact);
        Assert.Equal(commandEnvelope,new GraphRuntimeAdmissionResult.AlreadyAdmitted(commandEnvelope,resultEnvelope).CommandFact);
    }

    [Fact]
    public async Task EnvelopeBearingArms_RejectWrongOrMismatchedTuples()
    {
        var (_,_,command,result,fact)=await SuccessfulPair();
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.Applied(fact.ResultingSnapshot!,result,command,fact.EffectReceiptHash!.Value));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.Applied(fact.ResultingSnapshot!,command,result,Hash(19)));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.Rejected(null,new BoundedAscii("refused"),command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.Conflict(null,fact.ActualPredecessor,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AlreadyAdmitted(result,result));
    }

    [Fact]
    public async Task Request_RecomputesBothEffectIdentitiesAndRequiresExactGraph()
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();
        var badActivate=new GraphRuntimeCommand.Activate(operation,f.Installation.Position,f.Installation.Position,
            f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,Hash(99));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionRequest(badActivate,f.Authority,new(TenantId.Create()),new(1)));
        var retireOperation=OperationId.Create();var retire=new GraphRuntimeCommand.Retire(retireOperation,f.Installation.Position,
            f.Installation.Position,GraphRuntimeEffectHashes.Retire(f.Session,retireOperation,f.Installation.Position));
        Assert.Equal(retire,new GraphRuntimeAdmissionRequest(retire,f.Authority,new(TenantId.Create()),new(1)).Command);
        var badRetire=new GraphRuntimeCommand.Retire(retireOperation,f.Installation.Position,f.Installation.Position,Hash(98));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionRequest(badRetire,f.Authority,new(TenantId.Create()),new(1)));
    }

    [Fact]
    public async Task TerminalAndScalarPositiveArms_PreserveExactEvidence()
    {
        var (_,_,command,result,fact)=await SuccessfulPair();
        var terminal=new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(AuthorityAxisId.Graph,
            StableId128.CreateRandom(),command.Position.Sequence,result.Position.Sequence,null,null,null,null);
        Assert.Null(terminal.TerminalCommandFact);Assert.Null(terminal.TerminalResultFact);
        Assert.Equal(4,new GraphRuntimeAdmissionResult.NotAdmitted(new("stale-authority"),4).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResult.OutcomeUnknown(new("unknown"),4,null).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResult.ContradictoryDuplicate(new("changed-identity"),4).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResult.InvalidHistory(new("invalid-history"),4).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResult.RetryRequired(4).LastVerified);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.RuntimeReplaced(RuntimeGenerationId.Create(),
            command.Position.Sequence,result.Position.Sequence,fact.ResultingSnapshot,null,command,result));
    }

    [Fact]
    public async Task TerminalOrderingAndClaimedAxis_AreExact()
    {
        var (_,_,command,result,fact)=await SuccessfulPair();var next=StableId128.CreateRandom();
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph,next,command.Position.Sequence,result.Position.Sequence,fact.ResultingSnapshot,null,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph,next,command.Position.Sequence-1,result.Position.Sequence,fact.ResultingSnapshot,null,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph,next,command.Position.Sequence-1,result.Position.Sequence,null,null,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Turn,next,1,1,null,null,null,null));
        var graph=new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(AuthorityAxisId.Graph,next,1,1,null,null,null,null);
        Assert.Equal(AuthorityAxisId.Graph,graph.Axis);
    }

    [Fact]
    public async Task RejectedAndConflictPositiveArms_PreserveCanonicalFacts()
    {
        var rejected=await FailurePair(GraphRuntimeOutcome.Rejected,new BoundedAscii("effect-refused"));
        Assert.Equal(rejected.Result,new GraphRuntimeAdmissionResult.Rejected(null,new("effect-refused"),
            rejected.Command,rejected.Result).ResultFact);
        var conflict=await FailurePair(GraphRuntimeOutcome.Conflict,new BoundedAscii("runtime-predecessor-conflict"));
        Assert.Equal(conflict.Actual,new GraphRuntimeAdmissionResult.Conflict(null,conflict.Actual,
            conflict.Command,conflict.Result).ActualPredecessor);
    }

    [Fact]
    public void ScalarAndTerminalArms_RejectIncompleteEvidence()
    {
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.NotAdmitted(default,0));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.OutcomeUnknown(new BoundedAscii("unknown"),-1,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.ContradictoryDuplicate(default,0));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.InvalidHistory(new BoundedAscii("invalid"),-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new GraphRuntimeAdmissionResult.RetryRequired(-1));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.RuntimeReplaced(default,0,0,null,null,null,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Runtime,StableId128.CreateRandom(),0,0,null,null,null,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.AuthorityGenerationReplaced(
            AuthorityAxisId.Activity,default,0,0,null,null,null,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResult.RuntimeReplaced(
            RuntimeGenerationId.Create(),2,1,null,null,null,null));
    }

    [Fact]
    public void Union_IsClosedAndExact()
    {
        var arms=typeof(GraphRuntimeAdmissionResult).GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic)
            .Where(x=>x.IsSubclassOf(typeof(GraphRuntimeAdmissionResult))).Select(x=>x.Name).Order().ToArray();
        Assert.Equal(new[]{"AlreadyAdmitted","Applied","AuthorityGenerationReplaced","Conflict","ContradictoryDuplicate",
            "InvalidHistory","NotAdmitted","OutcomeUnknown","Rejected","RetryRequired","RuntimeReplaced"},arms);
    }

    private static async Task<(GraphRuntimeReducerTests.Fixture Fixture,GraphRuntimeCommand Command,
        AuthorityFactEnvelopeV1 CommandEnvelope,AuthorityFactEnvelopeV1 ResultEnvelope,GraphRuntimeFact Fact)> SuccessfulPair()
    {
        var fixture=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();
        var hash=GraphRuntimeEffectHashes.Activate(fixture.Session,operation,fixture.Installation.Position,
            fixture.Plan.Fingerprint,fixture.GraphGeneration,fixture.Grant.CurrentFact);
        var command=new GraphRuntimeCommand.Activate(operation,fixture.Installation.Position,fixture.Installation.Position,
            fixture.Plan.Fingerprint,fixture.GraphGeneration,fixture.Grant.CurrentFact,hash);
        var admitted=await fixture.AppendCommandAsync(command);
        var graph=Assert.IsType<GraphReplacementSnapshotReadResult.Verified>(
            await GraphReplacementSnapshotReader.ReadAsync(fixture.Journal,fixture.Session));
        var required=Assert.IsType<GraphRuntimeReducer.EffectRequired>(GraphRuntimeReducer.Evaluate(null,command,
            admitted.Position,fixture.Authority,GraphRuntimeCurrentGraphEvidence.From(graph),fixture.Grant));
        var receipt=Hash(9);var resolved=Assert.IsType<GraphRuntimeResolution.Applied>(GraphRuntimeReducer.Resolve(required,
            new GraphRuntimeEffectResolution.Completed(receipt),new JournalPositionV1(fixture.Session,admitted.Position.Sequence+1)));
        var result=await fixture.AppendFactAsync(new GraphRuntimeFact(admitted.Position,command.ExpectedPredecessor,
            fixture.Installation.Position,GraphRuntimeOutcome.Activated,resolved.Snapshot,receipt,null),admitted.Position.Sequence);
        Assert.True(GraphRuntimeCodecs.TryDecodeOuter(result.PayloadMemory,out var outer));
        Assert.True(GraphRuntimeCodecs.TryDecodeFact(outer!.Body,out var fact));
        return(fixture,command,admitted,result,fact!);
    }

    private static async Task<(AuthorityFactEnvelopeV1 Command,AuthorityFactEnvelopeV1 Result,JournalPositionV1 Actual)> FailurePair(
        GraphRuntimeOutcome outcome,BoundedAscii code)
    {
        var f=await GraphRuntimeReducerTests.Fixture.CreateAsync();var operation=OperationId.Create();
        var hash=GraphRuntimeEffectHashes.Activate(f.Session,operation,f.Installation.Position,f.Plan.Fingerprint,
            f.GraphGeneration,f.Grant.CurrentFact);
        var command=new GraphRuntimeCommand.Activate(operation,f.Installation.Position,f.Installation.Position,
            f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,hash);var admitted=await f.AppendCommandAsync(command);
        var fact=new GraphRuntimeFact(admitted.Position,command.ExpectedPredecessor,f.Installation.Position,outcome,null,null,code);
        var result=await f.AppendFactAsync(fact,admitted.Position.Sequence);return(admitted,result,f.Installation.Position);
    }

    private static Hash256 Hash(byte value){Hash256.TryCreate(Enumerable.Repeat(value,32).ToArray(),out var hash);return hash;}
}
