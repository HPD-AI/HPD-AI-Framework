using HPD.Agent.Authority;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class S6AuthoritativeAudioOutputSinkV2Tests
{
    [Fact]
    public async Task Scoped_write_and_playback_receipts_advance_distinct_axes()
    {
        var controller=Controller();var inner=new Sink();var sink=new S6AuthoritativeAudioOutputSinkV2(inner,controller,OutputSynthesisFamilyV2.PushPcm);
        var chunk=Chunk([1,2,3,4,5]);
        await sink.WriteAsync(chunk);
        inner.Events.Add(new OutputPlaybackCompletedEvent
        {
            OutputFlowId=chunk.OutputFlowId,ResponseId=chunk.ResponseId,SegmentId=chunk.SegmentId,SegmentIndex=0,
            Cursor=new OutputPlaybackCursor{OutputFlowId=chunk.OutputFlowId,ResponseId=chunk.ResponseId,SegmentId=chunk.SegmentId,SegmentIndex=0,PlayedTextLength=3,PlayedDuration=TimeSpan.FromMilliseconds(1),Precision=OutputAlignmentPrecision.Exact},
            ObservedAt=DateTimeOffset.UtcNow
        });
        await foreach(var _ in sink.ReadPlaybackEventsAsync(chunk.OutputFlowId)) { }
        var status=controller.Read();
        Assert.Equal((5L,5L,3L,3L),(status.GeneratedUntil,status.SentUntil,status.PlayedUntil,status.HeardUntil));
    }

    [Fact]
    public async Task Failed_external_write_never_promotes_generated_evidence_to_sent()
    {
        var controller=Controller();var sink=new S6AuthoritativeAudioOutputSinkV2(new Sink{FailWrite=true},controller,OutputSynthesisFamilyV2.SegmentedPcm);
        await Assert.ThrowsAsync<IOException>(()=>sink.WriteAsync(Chunk([1,2,3])).AsTask());
        var status=controller.Read();
        Assert.Equal((3L,0L),(status.GeneratedUntil,status.SentUntil));
    }

    private static InMemoryOutputControllerV2 Controller()
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var output=OutputGenerationId.Create();
        var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Output(output)]);
        return new InMemoryOutputControllerV2(new OutputPlanV2(OperationId.Create(),output,authority,100),32);
    }

    private static OutputAudioChunk Chunk(byte[] data)=>new()
    {
        OutputFlowId=new OutputFlowId("flow"),ResponseId=new ResponseId("response"),SegmentId=new OutputSegmentId("segment"),SegmentIndex=0,Sequence=0,
        Payload=new EncodedOutputAudioData{ContentType="audio/test",Data=data},ObservedAt=DateTimeOffset.UtcNow
    };

    private sealed class Sink : IAudioOutputSink
    {
        internal bool FailWrite{get;init;} internal List<OutputPlaybackEvent> Events{get;}=[];
        public ValueTask<OutputSinkStartResult> StartAsync(OutputAudioStream stream,CancellationToken cancellationToken=default)=>ValueTask.FromResult(new OutputSinkStartResult{OutputFlowId=stream.OutputFlowId,ResponseId=stream.ResponseId,SegmentId=stream.SegmentId,SegmentIndex=stream.SegmentIndex,Disposition=OutputSinkStartDisposition.Accepted});
        public ValueTask WriteAsync(OutputAudioChunk chunk,CancellationToken cancellationToken=default)=>FailWrite?ValueTask.FromException(new IOException("write failed")):ValueTask.CompletedTask;
        public ValueTask CompleteAsync(OutputAudioStreamCompletion completion,CancellationToken cancellationToken=default)=>ValueTask.CompletedTask;
        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(OutputFlowId outputFlowId,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken cancellationToken=default){foreach(var value in Events){yield return value;await Task.Yield();}}
        public ValueTask<OutputPlaybackBoundary> InterruptAsync(OutputFlowId outputFlowId,CancellationToken cancellationToken=default)=>ValueTask.FromResult(new OutputPlaybackBoundary{OutputFlowId=outputFlowId,ResponseId=new ResponseId("response"),SegmentId=new OutputSegmentId("segment"),SegmentIndex=0,PlayedTextLength=0});
        public ValueTask FlushAsync(OutputFlowId outputFlowId,CancellationToken cancellationToken=default)=>ValueTask.CompletedTask;
    }
}
