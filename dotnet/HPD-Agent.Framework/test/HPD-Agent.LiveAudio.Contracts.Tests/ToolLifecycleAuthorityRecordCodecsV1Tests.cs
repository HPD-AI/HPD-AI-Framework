using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ToolLifecycleAuthorityRecordCodecsV1Tests
{
    [Fact]
    public void All_thirteen_tool_lifecycle_records_round_trip_and_hash_independently()
    {
        var o=OperationId.Create();var p=new JournalPositionV1(Session(),8);var a=Authority(p.Session);
        var control=new ToolControlRecordedV1(o,p,a,1);var arguments=new ToolArgumentsFinalizedV1(o,p,a,2);
        var approval=new ToolApprovalDecidedV1(o,p,a,3);var disposition=new ToolDispositionChosenV1(o,p,a,4);
        var owner=new ToolOwnerClaimedV1(o,p,a,5);var dispatch=new ToolDispatchAuthorizedV1(o,p,a,6);
        var entry=new ToolEntryIntentRecordedV1(o,p,a,7);var boundary=new ToolExternalBoundaryEnteredV1(o,p,a,8);
        var evidence=new ToolEffectEvidenceAdmittedV1(o,p,a,9);var final=new ToolResultFinalizedV1(o,p,a,10);
        var projected=new ToolResultProjectedV1(o,p,a,11);var continuation=new ToolContinuationAuthorizedV1(o,p,a,12);
        var terminal=new ToolOrchestrationTerminalizedV1(o,p,a,13);
        RoundTrip(control,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeControl);RoundTrip(arguments,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeArguments);
        RoundTrip(approval,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeApproval);RoundTrip(disposition,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeDisposition);
        RoundTrip(owner,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeOwner);RoundTrip(dispatch,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeDispatch);
        RoundTrip(entry,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeEntry);RoundTrip(boundary,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeBoundary);
        RoundTrip(evidence,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeEvidence);RoundTrip(final,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeFinal);
        RoundTrip(projected,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeProjected);RoundTrip(continuation,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeContinuation);
        RoundTrip(terminal,ToolLifecycleAuthorityRecordCodecsV1.TryDecodeTerminal);
        Hash256[] hashes=[ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(control),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(arguments),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(approval),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(disposition),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(owner),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(dispatch),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(entry),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(boundary),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(evidence),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(final),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(projected),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(continuation),ToolLifecycleAuthorityRecordCodecsV1.ComputeHash(terminal)];
        Assert.Equal(13,hashes.Distinct().Count());
    }

    [Fact]
    public void Tool_lifecycle_contract_and_decoder_fail_closed()
    {
        var p=new JournalPositionV1(Session(),1);var a=Authority(p.Session);
        Assert.Throws<ArgumentException>(()=>new ToolControlRecordedV1(default,p,a,1));
        Assert.Throws<ArgumentException>(()=>new ToolControlRecordedV1(OperationId.Create(),p,a,0));
        var bytes=ToolLifecycleAuthorityRecordCodecsV1.Encode(new ToolControlRecordedV1(OperationId.Create(),p,a,1));
        Assert.False(ToolLifecycleAuthorityRecordCodecsV1.TryDecodeControl(bytes.Concat(new byte[]{0}).ToArray(),out _));
        Assert.False(ToolLifecycleAuthorityRecordCodecsV1.TryDecodeControl(new byte[]{0xff},out _));
    }

    private static void RoundTrip<T>(T value,Decoder<T> decode)where T:ToolLifecycleRecordV1{var bytes=ToolLifecycleAuthorityRecordCodecsV1.Encode(value);Assert.True(decode(bytes,out var result));Assert.Equal(value,result);}
    private delegate bool Decoder<T>(ReadOnlyMemory<byte> bytes,out T? value)where T:ToolLifecycleRecordV1;
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
    private static ExpectedAuthorityVectorV1 Authority(SessionAuthorityStampV1 session)=>ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Tool(ToolGenerationId.Create())]);
}
