using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ToolLifecycleAuthorityRecordCodecsTests
{
    [Fact]
    public void All_thirteen_tool_lifecycle_records_round_trip_and_hash_independently()
    {
        var o=OperationId.Create();var p=new JournalPositionV1(Session(),8);var a=Authority(p.Session);
        var control=new ToolControlRecorded(o,p,a,1);var arguments=new ToolArgumentsFinalized(o,p,a,2);
        var approval=new ToolApprovalDecided(o,p,a,3);var disposition=new ToolDispositionChosen(o,p,a,4);
        var owner=new ToolOwnerClaimed(o,p,a,5);var dispatch=new ToolDispatchAuthorized(o,p,a,6);
        var entry=new ToolEntryIntentRecorded(o,p,a,7);var boundary=new ToolExternalBoundaryEntered(o,p,a,8);
        var evidence=new ToolEffectEvidenceAdmitted(o,p,a,9);var final=new ToolResultFinalized(o,p,a,10);
        var projected=new ToolResultProjected(o,p,a,11);var continuation=new ToolContinuationAuthorized(o,p,a,12);
        var terminal=new ToolOrchestrationTerminalized(o,p,a,13);
        RoundTrip(control,ToolLifecycleAuthorityRecordCodecs.TryDecodeControl);RoundTrip(arguments,ToolLifecycleAuthorityRecordCodecs.TryDecodeArguments);
        RoundTrip(approval,ToolLifecycleAuthorityRecordCodecs.TryDecodeApproval);RoundTrip(disposition,ToolLifecycleAuthorityRecordCodecs.TryDecodeDisposition);
        RoundTrip(owner,ToolLifecycleAuthorityRecordCodecs.TryDecodeOwner);RoundTrip(dispatch,ToolLifecycleAuthorityRecordCodecs.TryDecodeDispatch);
        RoundTrip(entry,ToolLifecycleAuthorityRecordCodecs.TryDecodeEntry);RoundTrip(boundary,ToolLifecycleAuthorityRecordCodecs.TryDecodeBoundary);
        RoundTrip(evidence,ToolLifecycleAuthorityRecordCodecs.TryDecodeEvidence);RoundTrip(final,ToolLifecycleAuthorityRecordCodecs.TryDecodeFinal);
        RoundTrip(projected,ToolLifecycleAuthorityRecordCodecs.TryDecodeProjected);RoundTrip(continuation,ToolLifecycleAuthorityRecordCodecs.TryDecodeContinuation);
        RoundTrip(terminal,ToolLifecycleAuthorityRecordCodecs.TryDecodeTerminal);
        Hash256[] hashes=[ToolLifecycleAuthorityRecordCodecs.ComputeHash(control),ToolLifecycleAuthorityRecordCodecs.ComputeHash(arguments),ToolLifecycleAuthorityRecordCodecs.ComputeHash(approval),ToolLifecycleAuthorityRecordCodecs.ComputeHash(disposition),ToolLifecycleAuthorityRecordCodecs.ComputeHash(owner),ToolLifecycleAuthorityRecordCodecs.ComputeHash(dispatch),ToolLifecycleAuthorityRecordCodecs.ComputeHash(entry),ToolLifecycleAuthorityRecordCodecs.ComputeHash(boundary),ToolLifecycleAuthorityRecordCodecs.ComputeHash(evidence),ToolLifecycleAuthorityRecordCodecs.ComputeHash(final),ToolLifecycleAuthorityRecordCodecs.ComputeHash(projected),ToolLifecycleAuthorityRecordCodecs.ComputeHash(continuation),ToolLifecycleAuthorityRecordCodecs.ComputeHash(terminal)];
        Assert.Equal(13,hashes.Distinct().Count());
    }

    [Fact]
    public void Tool_lifecycle_contract_and_decoder_fail_closed()
    {
        var p=new JournalPositionV1(Session(),1);var a=Authority(p.Session);
        Assert.Throws<ArgumentException>(()=>new ToolControlRecorded(default,p,a,1));
        Assert.Throws<ArgumentException>(()=>new ToolControlRecorded(OperationId.Create(),p,a,0));
        var bytes=ToolLifecycleAuthorityRecordCodecs.Encode(new ToolControlRecorded(OperationId.Create(),p,a,1));
        Assert.False(ToolLifecycleAuthorityRecordCodecs.TryDecodeControl(bytes.Concat(new byte[]{0}).ToArray(),out _));
        Assert.False(ToolLifecycleAuthorityRecordCodecs.TryDecodeControl(new byte[]{0xff},out _));
    }

    private static void RoundTrip<T>(T value,Decoder<T> decode)where T:ToolLifecycleRecord{var bytes=ToolLifecycleAuthorityRecordCodecs.Encode(value);Assert.True(decode(bytes,out var result));Assert.Equal(value,result);}
    private delegate bool Decoder<T>(ReadOnlyMemory<byte> bytes,out T? value)where T:ToolLifecycleRecord;
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
    private static ExpectedAuthorityVectorV1 Authority(SessionAuthorityStampV1 session)=>ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Tool(ToolGenerationId.Create())]);
}
