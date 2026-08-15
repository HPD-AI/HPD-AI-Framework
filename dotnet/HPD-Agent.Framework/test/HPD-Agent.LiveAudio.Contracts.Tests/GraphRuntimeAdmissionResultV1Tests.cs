using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;
using System.Reflection;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeAdmissionResultV1Tests
{
    [Fact]
    public async Task ExactSuccessfulTuple_ConstructsAppliedAndAlreadyAdmitted()
    {
        var (fixture,command,commandEnvelope,resultEnvelope,fact)=await SuccessfulPair();
        var request=new GraphRuntimeAdmissionRequestV1(command,fixture.Authority,
            new CorrelationEnvelopeV1(TenantId.Create()),new UtcInstant(3));
        Assert.Same(command,request.Command);
        var applied=new GraphRuntimeAdmissionResultV1.Applied(fact.ResultingSnapshot!,commandEnvelope,resultEnvelope,fact.EffectReceiptHash!.Value);
        Assert.Equal(resultEnvelope,applied.ResultFact);
        Assert.Equal(commandEnvelope,new GraphRuntimeAdmissionResultV1.AlreadyAdmitted(commandEnvelope,resultEnvelope).CommandFact);
    }

    [Fact]
    public async Task EnvelopeBearingArms_RejectWrongOrMismatchedTuples()
    {
        var (_,_,command,result,fact)=await SuccessfulPair();
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.Applied(fact.ResultingSnapshot!,result,command,fact.EffectReceiptHash!.Value));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.Applied(fact.ResultingSnapshot!,command,result,Hash(19)));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.Rejected(null,new BoundedAscii("refused"),command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.Conflict(null,fact.ActualPredecessor,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AlreadyAdmitted(result,result));
    }

    [Fact]
    public async Task Request_RecomputesBothEffectIdentitiesAndRequiresExactGraph()
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var operation=OperationId.Create();
        var badActivate=new GraphRuntimeCommandV1.Activate(operation,f.Installation.Position,f.Installation.Position,
            f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,Hash(99));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionRequestV1(badActivate,f.Authority,new(TenantId.Create()),new(1)));
        var retireOperation=OperationId.Create();var retire=new GraphRuntimeCommandV1.Retire(retireOperation,f.Installation.Position,
            f.Installation.Position,GraphRuntimeEffectHashesV1.Retire(f.Session,retireOperation,f.Installation.Position));
        Assert.Equal(retire,new GraphRuntimeAdmissionRequestV1(retire,f.Authority,new(TenantId.Create()),new(1)).Command);
        var badRetire=new GraphRuntimeCommandV1.Retire(retireOperation,f.Installation.Position,f.Installation.Position,Hash(98));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionRequestV1(badRetire,f.Authority,new(TenantId.Create()),new(1)));
    }

    [Fact]
    public async Task TerminalAndScalarPositiveArms_PreserveExactEvidence()
    {
        var (_,_,command,result,fact)=await SuccessfulPair();
        var terminal=new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(AuthorityAxisId.Graph,
            StableId128.CreateRandom(),command.Position.Sequence,result.Position.Sequence,null,null,null,null);
        Assert.Null(terminal.TerminalCommandFact);Assert.Null(terminal.TerminalResultFact);
        Assert.Equal(4,new GraphRuntimeAdmissionResultV1.NotAdmitted(new("stale-authority"),4).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResultV1.OutcomeUnknown(new("unknown"),4,null).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResultV1.ContradictoryDuplicate(new("changed-identity"),4).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResultV1.InvalidHistory(new("invalid-history"),4).LastVerified);
        Assert.Equal(4,new GraphRuntimeAdmissionResultV1.RetryRequired(4).LastVerified);
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.RuntimeReplaced(RuntimeGenerationId.Create(),
            command.Position.Sequence,result.Position.Sequence,fact.ResultingSnapshot,null,command,result));
    }

    [Fact]
    public async Task TerminalOrderingAndClaimedAxis_AreExact()
    {
        var (_,_,command,result,fact)=await SuccessfulPair();var next=StableId128.CreateRandom();
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph,next,command.Position.Sequence,result.Position.Sequence,fact.ResultingSnapshot,null,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph,next,command.Position.Sequence-1,result.Position.Sequence,fact.ResultingSnapshot,null,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(
            AuthorityAxisId.Graph,next,command.Position.Sequence-1,result.Position.Sequence,null,null,command,result));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(
            AuthorityAxisId.Turn,next,1,1,null,null,null,null));
        var graph=new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(AuthorityAxisId.Graph,next,1,1,null,null,null,null);
        Assert.Equal(AuthorityAxisId.Graph,graph.Axis);
    }

    [Fact]
    public async Task RejectedAndConflictPositiveArms_PreserveCanonicalFacts()
    {
        var rejected=await FailurePair(GraphRuntimeOutcomeV1.Rejected,new BoundedAscii("effect-refused"));
        Assert.Equal(rejected.Result,new GraphRuntimeAdmissionResultV1.Rejected(null,new("effect-refused"),
            rejected.Command,rejected.Result).ResultFact);
        var conflict=await FailurePair(GraphRuntimeOutcomeV1.Conflict,new BoundedAscii("runtime-predecessor-conflict"));
        Assert.Equal(conflict.Actual,new GraphRuntimeAdmissionResultV1.Conflict(null,conflict.Actual,
            conflict.Command,conflict.Result).ActualPredecessor);
    }

    [Fact]
    public void ScalarAndTerminalArms_RejectIncompleteEvidence()
    {
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.NotAdmitted(default,0));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.OutcomeUnknown(new BoundedAscii("unknown"),-1,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.ContradictoryDuplicate(default,0));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.InvalidHistory(new BoundedAscii("invalid"),-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new GraphRuntimeAdmissionResultV1.RetryRequired(-1));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.RuntimeReplaced(default,0,0,null,null,null,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(
            AuthorityAxisId.Runtime,StableId128.CreateRandom(),0,0,null,null,null,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.AuthorityGenerationReplaced(
            AuthorityAxisId.Activity,default,0,0,null,null,null,null));
        Assert.Throws<ArgumentException>(()=>new GraphRuntimeAdmissionResultV1.RuntimeReplaced(
            RuntimeGenerationId.Create(),2,1,null,null,null,null));
    }

    [Fact]
    public void Union_IsClosedAndExact()
    {
        var arms=typeof(GraphRuntimeAdmissionResultV1).GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic)
            .Where(x=>x.IsSubclassOf(typeof(GraphRuntimeAdmissionResultV1))).Select(x=>x.Name).Order().ToArray();
        Assert.Equal(new[]{"AlreadyAdmitted","Applied","AuthorityGenerationReplaced","Conflict","ContradictoryDuplicate",
            "InvalidHistory","NotAdmitted","OutcomeUnknown","Rejected","RetryRequired","RuntimeReplaced"},arms);
    }

    private static async Task<(GraphRuntimeReducerV1Tests.Fixture Fixture,GraphRuntimeCommandV1 Command,
        AuthorityFactEnvelopeV1 CommandEnvelope,AuthorityFactEnvelopeV1 ResultEnvelope,GraphRuntimeFactV1 Fact)> SuccessfulPair()
    {
        var fixture=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var operation=OperationId.Create();
        var hash=GraphRuntimeEffectHashesV1.Activate(fixture.Session,operation,fixture.Installation.Position,
            fixture.Plan.Fingerprint,fixture.GraphGeneration,fixture.Grant.CurrentFact);
        var command=new GraphRuntimeCommandV1.Activate(operation,fixture.Installation.Position,fixture.Installation.Position,
            fixture.Plan.Fingerprint,fixture.GraphGeneration,fixture.Grant.CurrentFact,hash);
        var admitted=await fixture.AppendCommandAsync(command);
        var graph=Assert.IsType<GraphReplacementSnapshotReadResultV1.Verified>(
            await GraphReplacementSnapshotReaderV1.ReadAsync(fixture.Journal,fixture.Session));
        var required=Assert.IsType<GraphRuntimeReducerV1.EffectRequired>(GraphRuntimeReducerV1.Evaluate(null,command,
            admitted.Position,fixture.Authority,GraphRuntimeCurrentGraphEvidenceV1.From(graph),fixture.Grant));
        var receipt=Hash(9);var resolved=Assert.IsType<GraphRuntimeResolutionV1.Applied>(GraphRuntimeReducerV1.Resolve(required,
            new GraphRuntimeEffectResolutionV1.Completed(receipt),new JournalPositionV1(fixture.Session,admitted.Position.Sequence+1)));
        var result=await fixture.AppendFactAsync(new GraphRuntimeFactV1(admitted.Position,command.ExpectedPredecessor,
            fixture.Installation.Position,GraphRuntimeOutcomeV1.Activated,resolved.Snapshot,receipt,null),admitted.Position.Sequence);
        Assert.True(GraphRuntimeCodecsV1.TryDecodeOuter(result.PayloadMemory,out var outer));
        Assert.True(GraphRuntimeCodecsV1.TryDecodeFact(outer!.Body,out var fact));
        return(fixture,command,admitted,result,fact!);
    }

    private static async Task<(AuthorityFactEnvelopeV1 Command,AuthorityFactEnvelopeV1 Result,JournalPositionV1 Actual)> FailurePair(
        GraphRuntimeOutcomeV1 outcome,BoundedAscii code)
    {
        var f=await GraphRuntimeReducerV1Tests.Fixture.CreateAsync();var operation=OperationId.Create();
        var hash=GraphRuntimeEffectHashesV1.Activate(f.Session,operation,f.Installation.Position,f.Plan.Fingerprint,
            f.GraphGeneration,f.Grant.CurrentFact);
        var command=new GraphRuntimeCommandV1.Activate(operation,f.Installation.Position,f.Installation.Position,
            f.Plan.Fingerprint,f.GraphGeneration,f.Grant.CurrentFact,hash);var admitted=await f.AppendCommandAsync(command);
        var fact=new GraphRuntimeFactV1(admitted.Position,command.ExpectedPredecessor,f.Installation.Position,outcome,null,null,code);
        var result=await f.AppendFactAsync(fact,admitted.Position.Sequence);return(admitted,result,f.Installation.Position);
    }

    private static Hash256 Hash(byte value){Hash256.TryCreate(Enumerable.Repeat(value,32).ToArray(),out var hash);return hash;}
}
