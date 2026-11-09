using HPD.Agent.Authority;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.Tests;

public sealed class S6AuthoritativeAudioOutputSinkTests
{
    [Fact]
    public async Task Scoped_write_and_playback_receipts_advance_distinct_axes()
    {
        var controller=Controller();var inner=new Sink();var sink=new S6AuthoritativeAudioOutputSink(inner,controller,OutputSynthesisFamily.PushPcm);
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
        var controller=Controller();var sink=new S6AuthoritativeAudioOutputSink(new Sink{FailWrite=true},controller,OutputSynthesisFamily.SegmentedPcm);
        await Assert.ThrowsAsync<IOException>(()=>sink.WriteAsync(Chunk([1,2,3])).AsTask());
        var status=controller.Read();
        Assert.Equal((3L,0L),(status.GeneratedUntil,status.SentUntil));
    }

    [Fact]
    public async Task Decoded_pcm_evidence_may_be_larger_than_its_source_text()
    {
        var controller=Controller(maximumUnits:64L*1024*1024);var sink=new S6AuthoritativeAudioOutputSink(new Sink(),controller,OutputSynthesisFamily.SegmentedPcm);
        var pcm=new byte[32_000];

        await sink.WriteAsync(Chunk(pcm));

        var status=controller.Read();
        Assert.Equal((32_000L,32_000L),(status.GeneratedUntil,status.SentUntil));
    }

    private static InMemoryOutputController Controller(long maximumUnits=100)
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var output=OutputGenerationId.Create();
        var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Output(output)]);
        return new InMemoryOutputController(new OutputPlan(OperationId.Create(),output,authority,maximumUnits),32);
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
