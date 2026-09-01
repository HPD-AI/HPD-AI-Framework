using HPD.Agent.Authority;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Runtime.Output;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal sealed class S6AuthoritativeAudioOutputSinkV2 : IAudioOutputSink
{
    private readonly IAudioOutputSink _inner;
    private readonly InMemoryOutputControllerV2 _controller;
    private readonly OutputSynthesisFamilyV2 _family;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _generatedUnits;

    internal S6AuthoritativeAudioOutputSinkV2(
        IAudioOutputSink inner,
        InMemoryOutputControllerV2 controller,
        OutputSynthesisFamilyV2 family)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _family = family;
    }

    public ValueTask<OutputSinkStartResult> StartAsync(OutputAudioStream stream,CancellationToken cancellationToken=default) =>
        _inner.StartAsync(stream,cancellationToken);

    public async ValueTask WriteAsync(OutputAudioChunk chunk,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = checked(_generatedUnits + chunk.SizeBytes);
            var generated = _controller.Generate(new OutputSynthesisEvidenceV2(
                OperationId.Create(),_family,next,Fingerprint(chunk.Payload)));
            if (generated is not OutputPipelineResultV2.Applied)
                throw new InvalidOperationException("S6 rejected generated output evidence.");
            _generatedUnits = next;
            await _inner.WriteAsync(chunk,cancellationToken).ConfigureAwait(false);
            var sent = await _controller.SendAsync(
                new OutputSinkEffectV2.Send(OperationId.Create(),next),
                new ManualOutputSinkEffectPortV2(),cancellationToken).ConfigureAwait(false);
            if (sent is not OutputPipelineResultV2.Applied)
                throw new InvalidOperationException("S6 rejected the scoped sink receipt.");
        }
        finally { _gate.Release(); }
    }

    public ValueTask CompleteAsync(OutputAudioStreamCompletion completion,CancellationToken cancellationToken=default) =>
        _inner.CompleteAsync(completion,cancellationToken);

    public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
        OutputFlowId outputFlowId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken=default)
    {
        await foreach(var playbackEvent in _inner.ReadPlaybackEventsAsync(outputFlowId,cancellationToken).ConfigureAwait(false))
        {
            await SettlePlaybackAsync(playbackEvent,cancellationToken).ConfigureAwait(false);
            yield return playbackEvent;
        }
    }

    public ValueTask<OutputPlaybackBoundary> InterruptAsync(OutputFlowId outputFlowId,CancellationToken cancellationToken=default) =>
        _inner.InterruptAsync(outputFlowId,cancellationToken);

    public ValueTask FlushAsync(OutputFlowId outputFlowId,CancellationToken cancellationToken=default) =>
        _inner.FlushAsync(outputFlowId,cancellationToken);

    private async ValueTask SettlePlaybackAsync(OutputPlaybackEvent playbackEvent,CancellationToken cancellationToken)
    {
        var cursor = playbackEvent switch
        {
            OutputPlaybackProgressEvent progress => progress.Cursor,
            OutputPlaybackCompletedEvent completed => completed.Cursor,
            OutputPlaybackInterruptedEvent interrupted => new OutputPlaybackCursor
            {
                OutputFlowId=interrupted.Boundary.OutputFlowId,ResponseId=interrupted.Boundary.ResponseId,
                SegmentId=interrupted.Boundary.SegmentId,SegmentIndex=interrupted.Boundary.SegmentIndex,
                PlayedDuration=interrupted.Boundary.PlayedDuration,PlayedTextLength=interrupted.Boundary.PlayedTextLength,
                Precision=interrupted.Boundary.Precision
            },
            _ => null
        };
        if(cursor is null)return;
        var status=_controller.Read();
        var until=Math.Min(status.SentUntil,Math.Max(status.PlayedUntil,cursor.PlayedTextLength));
        if(until>status.PlayedUntil)
        {
            var played=await _controller.PlayAsync(new OutputSinkEffectV2.Play(OperationId.Create(),until),new ManualOutputSinkEffectPortV2(),cancellationToken).ConfigureAwait(false);
            if(played is not OutputPipelineResultV2.Applied)throw new InvalidOperationException("S6 rejected playback evidence.");
        }
        if(playbackEvent is OutputPlaybackCompletedEvent && until>_controller.Read().HeardUntil)
        {
            var heard=await _controller.HearAsync(new OutputSinkEffectV2.Hear(OperationId.Create(),until),new ManualOutputSinkEffectPortV2(),cancellationToken).ConfigureAwait(false);
            if(heard is not OutputPipelineResultV2.Applied)throw new InvalidOperationException("S6 rejected heard evidence.");
        }
    }

    private static Hash256 Fingerprint(OutputAudioPayload payload) => payload switch
    {
        EncodedOutputAudioData encoded => Hash256.Compute(encoded.Data.Span),
        DecodedOutputAudioFrame decoded => Hash256.Compute(decoded.Frame.Data.Span),
        _ => throw new InvalidOperationException("Unsupported output payload.")
    };
}
