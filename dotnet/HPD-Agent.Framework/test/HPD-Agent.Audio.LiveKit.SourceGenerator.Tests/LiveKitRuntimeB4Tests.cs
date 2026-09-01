using HPD.Agent.Audio.LiveKit.Generated;
using HPD.Audio.Primitives;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Audio.LiveKit.SourceGenerator.Tests;

[Collection("LiveKit process host")]
public sealed class LiveKitRuntimeB4Tests
{
    [Fact]
    public void Qualified_native_asset_is_copied_and_hash_verified_for_runtime_admission()
    {
        var artifact = LiveKitRuntimeSupport.VerifyCurrentArtifact();

        Assert.Equal(LiveKitNativeArtifactDisposition.Verified, artifact.Disposition);
        Assert.NotNull(artifact.Path);
        Assert.EndsWith("liblivekit_ffi.dylib", artifact.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_before_registration_is_retained_and_response_is_released_once()
    {
        var native = new ScriptedNative();
        await using var host = LiveKitFfiHost.CreateIsolated(native, new ByteWireDecoder());
        native.BeforeResponse = () => native.Emit(Event(LiveKitFfiEventCase.Connect, 7));

        var completion = await host.IssueAsync(
            LiveKitFfiOperation.Connect,
            new byte[] { 1 },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(LiveKitFfiEventCase.Connect, completion!.Value.EventCase);
        Assert.Equal([101UL], native.Dropped);
        Assert.False(host.IsQuarantined);
    }

    [Fact]
    public async Task Cancel_after_issue_detaches_and_late_completion_reconciles()
    {
        var native = new ScriptedNative();
        await using var host = LiveKitFfiHost.CreateIsolated(native, new ByteWireDecoder());
        using var caller = new CancellationTokenSource();
        var pending = host.IssueAsync(
            LiveKitFfiOperation.Connect,
            new byte[] { 1 },
            TimeSpan.FromSeconds(5),
            caller.Token).AsTask();
        await native.Issued.Task.WaitAsync(TimeSpan.FromSeconds(1));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        native.Emit(Event(LiveKitFfiEventCase.Connect, 7));
        await WaitUntilAsync(() => host.Reconcile(new(LiveKitFfiOperation.Connect, 7)).Disposition == LiveKitIssuedOperationDisposition.DetachedCompleted);
        var snapshot = host.Reconcile(new(LiveKitFfiOperation.Connect, 7));
        Assert.Equal(LiveKitIssuedOperationDisposition.DetachedCompleted, snapshot.Disposition);
        Assert.Equal(1, snapshot.ObservedCompletionCount);
    }

    [Fact]
    public async Task Deadline_is_outcome_unknown_and_late_completion_remains_owned()
    {
        var native = new ScriptedNative();
        await using var host = LiveKitFfiHost.CreateIsolated(native, new ByteWireDecoder());
        var completion = await host.IssueAsync(
            LiveKitFfiOperation.Connect,
            new byte[] { 1 },
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);
        Assert.Null(completion);
        Assert.Equal(
            LiveKitIssuedOperationDisposition.OutcomeUnknown,
            host.Reconcile(new(LiveKitFfiOperation.Connect, 7)).Disposition);

        native.Emit(Event(LiveKitFfiEventCase.Connect, 7));
        await WaitUntilAsync(() => host.Reconcile(new(LiveKitFfiOperation.Connect, 7)).Disposition == LiveKitIssuedOperationDisposition.Completed);
        Assert.Equal(
            LiveKitIssuedOperationDisposition.Completed,
            host.Reconcile(new(LiveKitFfiOperation.Connect, 7)).Disposition);
    }

    [Fact]
    public async Task Invalid_release_and_panic_poison_the_process_host()
    {
        var native = new ScriptedNative { ReleaseSucceeds = false };
        await using var host = LiveKitFfiHost.CreateIsolated(native, new ByteWireDecoder());
        var owner = host.Own(LiveKitFfiHandleKind.Room, 55);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await owner.DisposeAsync());
        Assert.Equal("ffi-room-release-failed", host.QuarantineCode);
        Assert.Equal(1, host.OutstandingOwnedHandles);

        var panicNative = new ScriptedNative();
        await using var panicHost = LiveKitFfiHost.CreateIsolated(panicNative, new ByteWireDecoder());
        panicNative.Emit(Event(LiveKitFfiEventCase.Panic, 0));
        await WaitUntilAsync(() => panicHost.IsQuarantined);
        Assert.Equal("native-panic", panicHost.QuarantineCode);
    }

    [Fact]
    public async Task Inbound_pcm_is_owned_bounded_and_never_silently_dropped()
    {
        var format = Format();
        await using var source = new LiveKitInboundAudioSource(format, 1);
        source.AdmitCopiedPcm([1, 2, 3, 4], 2, 1);
        Assert.Throws<AudioSourceException>(() => source.AdmitCopiedPcm([5, 6, 7, 8], 2, 2));
        Assert.Equal(AudioSourceState.Failed, source.State);
        Assert.True(source.TryRead(out var retained));
        Assert.Equal([1, 2, 3, 4], retained.Data.ToArray());
    }

    [Fact]
    public async Task Outbound_pcm_is_serial_backpressured_and_clear_is_local_only()
    {
        var capture = new ScriptedCapture();
        await using var sink = new LiveKitOutboundAudioSink(Format(), capture);
        var frame = Frame([1, 2, 3, 4]);
        await Task.WhenAll(
            sink.WriteAsync(frame).AsTask(),
            sink.WriteAsync(frame).AsTask());
        await sink.FlushAsync();
        Assert.Equal(2, capture.Captures);
        Assert.Equal(1, capture.Clears);
        Assert.Equal(1, capture.MaximumConcurrent);
    }

    [Fact]
    public async Task Outbound_clear_preempts_an_inflight_capture_before_clearing_playout()
    {
        var capture = new BlockingCapture();
        await using var sink = new LiveKitOutboundAudioSink(Format(), capture);
        var write = sink.WriteAsync(Frame(new byte[96_000])).AsTask();
        await capture.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await sink.FlushAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        await write.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(capture.CaptureCancelled);
        Assert.Equal(1, capture.Clears);
        await sink.WriteAsync(Frame([1,2,3,4]));
        Assert.Equal(1, capture.CompletedCaptures);
    }

    [Fact]
    public void Replacement_fences_the_previous_session_generation()
    {
        var fence = new LiveKitSessionGenerationFence();
        string? quarantined = null;
        var first = fence.Replace(code => quarantined = code);
        var second = fence.Replace(_ => { });
        Assert.Equal("stale-session-generation", quarantined);
        Assert.False(fence.IsCurrent(first));
        Assert.True(fence.IsCurrent(second));
    }

    [Fact]
    public async Task Stop_ambiguity_retains_all_handles_until_reconciliation()
    {
        var native = new ScriptedNative();
        await using var host = LiveKitFfiHost.CreateIsolated(native, new ByteWireDecoder());
        var owners = new LiveKitRoomMediaOwner(
            host.Own(LiveKitFfiHandleKind.Room, 1),
            host.Own(LiveKitFfiHandleKind.Participant, 2),
            host.Own(LiveKitFfiHandleKind.AudioStream, 3),
            host.Own(LiveKitFfiHandleKind.AudioSource, 4),
            host.Own(LiveKitFfiHandleKind.Track, 5),
            host.Own(LiveKitFfiHandleKind.TrackPublication, 6));
        var control = new ScriptedSessionControl { DisconnectFails = true };
        var session = new LiveKitTransportSessionRuntime("session", Format(), control, owners, new());

        var unknown = await session.StopAsync(CancellationToken.None);
        Assert.Equal(LiveKitSessionStopDisposition.OutcomeUnknown, unknown.Disposition);
        Assert.Empty(native.Dropped);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());

        control.Reconciled = true;
        var stopped = await session.ReconcileStopAsync(unknown.OperationId!, CancellationToken.None);
        Assert.Equal(LiveKitSessionStopDisposition.Stopped, stopped.Disposition);
        Assert.Equal([6UL, 5UL, 4UL, 3UL, 2UL, 1UL], native.Dropped);
        Assert.Equal(0, host.OutstandingOwnedHandles);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Callback_pressure_overflow_quarantines_the_actual_host_path()
    {
        var native = new ScriptedNative();
        var decoder = new BlockingDecoder();
        await using var host = LiveKitFfiHost.CreateIsolated(native, decoder);
        host.ReceiveCopiedCallback(Event(LiveKitFfiEventCase.RoomEvent, 0));
        await decoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        for (var i = 0; i <= LiveKitFfiHost.MaximumCopiedCallbacks; i++)
            host.ReceiveCopiedCallback(Event(LiveKitFfiEventCase.RoomEvent, 0));
        Assert.Equal("ffi-callback-overflow", host.QuarantineCode);
        decoder.Release.Set();
    }

    [Fact]
    public async Task Existing_audio_output_sink_adapter_reports_only_local_queue_and_clear_truth()
    {
        var capture = new ScriptedCapture();
        await using var endpoint = new LiveKitOutboundAudioSink(Format(), capture);
        IAudioOutputSink adapter = new LiveKitAudioOutputSinkAdapter(endpoint);
        var flowId = new HPD.Agent.Audio.OutputFlowId("flow");
        var responseId = new HPD.Agent.Audio.ResponseId("response");
        var segmentId = new HPD.Agent.Audio.OutputSegmentId("segment");
        var start = await adapter.StartAsync(new OutputAudioStream
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            MediaType = "audio/pcm",
            PayloadKind = OutputAudioPayloadKind.DecodedPcmFrame
        });
        Assert.Equal(OutputSinkStartDisposition.Accepted, start.Disposition);
        await adapter.WriteAsync(new OutputAudioChunk
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Sequence = 0,
            Payload = new DecodedOutputAudioFrame { Frame = Frame([1, 2, 3, 4]) }
        });
        var boundary = await adapter.InterruptAsync(flowId);
        Assert.Equal(OutputAlignmentPrecision.Unknown, boundary.Precision);
        Assert.Equal(TimeSpan.Zero, boundary.PlayedDuration);
        Assert.Equal(1, capture.Captures);
        Assert.Equal(1, capture.Clears);
    }

    [Fact]
    public async Task Completed_final_output_stream_closes_playback_observation_with_approximate_boundary()
    {
        var capture = new ScriptedCapture();
        await using var endpoint = new LiveKitOutboundAudioSink(Format(), capture);
        IAudioOutputSink adapter = new LiveKitAudioOutputSinkAdapter(endpoint);
        var flowId = new HPD.Agent.Audio.OutputFlowId("flow-completed");
        var responseId = new HPD.Agent.Audio.ResponseId("response-completed");
        var segmentId = new HPD.Agent.Audio.OutputSegmentId("segment-completed");
        await adapter.StartAsync(new OutputAudioStream
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 0,
            SourceTextLength = 12,
            MediaType = "audio/pcm",
            PayloadKind = OutputAudioPayloadKind.DecodedPcmFrame
        });

        await adapter.CompleteAsync(new OutputAudioStreamCompletion
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Disposition = OutputAudioStreamDisposition.Completed,
            ChunkCount = 1,
            SizeBytes = 4,
            Duration = TimeSpan.FromMilliseconds(20),
            CompletedAt = DateTimeOffset.UtcNow
        });

        var events = new List<OutputPlaybackEvent>();
        await foreach (var playbackEvent in adapter.ReadPlaybackEventsAsync(flowId))
            events.Add(playbackEvent);

        Assert.Collection(events,
            queued => Assert.IsType<OutputPlaybackQueuedEvent>(queued),
            completed =>
            {
                var value = Assert.IsType<OutputPlaybackCompletedEvent>(completed);
                Assert.Equal(12, value.Cursor.PlayedTextLength);
                Assert.Equal(TimeSpan.FromMilliseconds(20), value.Cursor.PlayedDuration);
                Assert.Equal(OutputAlignmentPrecision.Approximate, value.Cursor.Precision);
            });
    }

    [Fact]
    public async Task Interrupted_output_stream_terminates_observation_and_retained_sink_accepts_next_flow()
    {
        var capture = new ScriptedCapture();
        await using var endpoint = new LiveKitOutboundAudioSink(Format(), capture);
        IAudioOutputSink adapter = new LiveKitAudioOutputSinkAdapter(endpoint);
        var firstFlow = new HPD.Agent.Audio.OutputFlowId("flow-interrupted");
        var responseId = new HPD.Agent.Audio.ResponseId("response-interrupted");
        var segmentId = new HPD.Agent.Audio.OutputSegmentId("segment-interrupted");
        await adapter.StartAsync(new OutputAudioStream
        {
            OutputFlowId = firstFlow,
            ResponseId = responseId,
            SegmentId = segmentId,
            IsFinalSegment = true,
            MediaType = "audio/pcm",
            PayloadKind = OutputAudioPayloadKind.DecodedPcmFrame
        });

        await adapter.InterruptAsync(firstFlow);
        var events = new List<OutputPlaybackEvent>();
        await foreach (var playbackEvent in adapter.ReadPlaybackEventsAsync(firstFlow))
            events.Add(playbackEvent);
        Assert.Contains(events, item => item is OutputPlaybackInterruptedEvent);

        var nextFlow = new HPD.Agent.Audio.OutputFlowId("flow-next");
        var started = await adapter.StartAsync(new OutputAudioStream
        {
            OutputFlowId = nextFlow,
            ResponseId = new HPD.Agent.Audio.ResponseId("response-next"),
            SegmentId = new HPD.Agent.Audio.OutputSegmentId("segment-next"),
            IsFinalSegment = true,
            MediaType = "audio/pcm",
            PayloadKind = OutputAudioPayloadKind.DecodedPcmFrame
        });
        Assert.Equal(OutputSinkStartDisposition.Accepted, started.Disposition);
        await adapter.WriteAsync(new OutputAudioChunk
        {
            OutputFlowId = nextFlow,
            ResponseId = started.ResponseId,
            SegmentId = started.SegmentId,
            SegmentIndex = 0,
            Sequence = 0,
            Payload = new DecodedOutputAudioFrame { Frame = Frame([1, 2, 3, 4]) }
        });
        Assert.Equal(AudioSinkState.Open, endpoint.State);
    }

    [Fact]
    public async Task Interrupted_flow_drops_late_chunks_before_the_retained_transport()
    {
        var inner = new RecordingOutputSink();
        var gated = new LiveKitManagedAudioSession.GatedAudioOutputSink(inner, static () => true);
        var flowId = new HPD.Agent.Audio.OutputFlowId("stale-flow");
        var responseId = new HPD.Agent.Audio.ResponseId("stale-response");
        var segmentId = new HPD.Agent.Audio.OutputSegmentId("stale-segment");
        await gated.StartAsync(new OutputAudioStream
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            IsFinalSegment = true,
            MediaType = "audio/pcm",
            PayloadKind = OutputAudioPayloadKind.DecodedPcmFrame
        });

        Assert.True(await gated.InterruptActiveAsync(CancellationToken.None));
        await gated.WriteAsync(new OutputAudioChunk
        {
            OutputFlowId = flowId,
            ResponseId = responseId,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Sequence = 0,
            Payload = new DecodedOutputAudioFrame { Frame = Frame([1, 2, 3, 4]) }
        });

        Assert.Equal(1, inner.Interrupts);
        Assert.Equal(0, inner.Writes);
    }

    private static AudioFormat Format() => new() { SampleRate = 48_000, ChannelCount = 1, SampleFormat = AudioSampleFormat.Pcm16 };
    private static AudioFrame Frame(byte[] bytes) => new() { Data = bytes, Format = Format(), SamplesPerChannel = bytes.Length / 2 };
    private static byte[] Event(LiveKitFfiEventCase eventCase, ulong asyncId) => [checked((byte)eventCase), .. BitConverter.GetBytes(asyncId)];

    private sealed unsafe class ScriptedNative : ILiveKitFfiNativeApi
    {
        private delegate* unmanaged[Cdecl]<nint, nuint, void> _callback;
        internal readonly TaskCompletionSource Issued = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<ulong> Dropped = [];
        internal Action? BeforeResponse;
        internal bool ReleaseSucceeds = true;

        public void Initialize(delegate* unmanaged[Cdecl]<nint, nuint, void> callback) => _callback = callback;
        public LiveKitFfiNativeResponse Request(ReadOnlySpan<byte> request)
        {
            Issued.TrySetResult();
            BeforeResponse?.Invoke();
            return new(101, [checked((byte)LiveKitFfiResponseCase.Connect), .. BitConverter.GetBytes(7UL)]);
        }
        public bool DropHandle(ulong handle) { Dropped.Add(handle); return ReleaseSucceeds; }
        public void Dispose() { }
        internal void Emit(byte[] bytes)
        {
            fixed (byte* pointer = bytes) _callback((nint)pointer, checked((nuint)bytes.Length));
        }
    }

    private sealed class ByteWireDecoder : ILiveKitFfiWireDecoder
    {
        public LiveKitFfiIssuedResponse DecodeResponse(ReadOnlySpan<byte> bytes) =>
            new((LiveKitFfiResponseCase)bytes[0], BitConverter.ToUInt64(bytes[1..]), bytes.ToArray());
        public LiveKitFfiDecodedEvent DecodeEvent(ReadOnlySpan<byte> bytes) =>
            new((LiveKitFfiEventCase)bytes[0], BitConverter.ToUInt64(bytes[1..]), bytes.ToArray());
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!predicate()) await Task.Delay(1, timeout.Token);
    }

    private sealed class BlockingDecoder : ILiveKitFfiWireDecoder
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new(false);
        public LiveKitFfiIssuedResponse DecodeResponse(ReadOnlySpan<byte> bytes) => throw new NotSupportedException();
        public LiveKitFfiDecodedEvent DecodeEvent(ReadOnlySpan<byte> bytes)
        {
            Entered.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(5));
            return new(LiveKitFfiEventCase.RoomEvent, 0, bytes.ToArray());
        }
    }

    private sealed class ScriptedCapture : ILiveKitAudioCapturePort
    {
        private int _concurrent;
        internal int Captures;
        internal int Clears;
        internal int MaximumConcurrent;
        public async ValueTask CaptureAsync(AudioFrame frame, CancellationToken cancellationToken)
        {
            var concurrent = Interlocked.Increment(ref _concurrent);
            MaximumConcurrent = Math.Max(MaximumConcurrent, concurrent);
            try { await Task.Yield(); Captures++; }
            finally { Interlocked.Decrement(ref _concurrent); }
        }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { Clears++; return ValueTask.CompletedTask; }
    }

    private sealed class BlockingCapture : ILiveKitAudioCapturePort
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CaptureCancelled;
        internal int Clears;
        internal int CompletedCaptures;

        public async ValueTask CaptureAsync(AudioFrame frame, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (CaptureCancelled)
            {
                CompletedCaptures++;
                return;
            }
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CaptureCancelled = true;
                throw new InvalidDataException("LiveKit audio capture outcome is unknown.");
            }
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            Clears++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingOutputSink : IAudioOutputSink
    {
        internal int Writes;
        internal int Interrupts;

        public ValueTask<OutputSinkStartResult> StartAsync(OutputAudioStream stream, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OutputSinkStartResult
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Accepted
            });

        public ValueTask WriteAsync(OutputAudioChunk chunk, CancellationToken cancellationToken = default)
        {
            Writes++;
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(OutputAudioStreamCompletion completion, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            HPD.Agent.Audio.OutputFlowId outputFlowId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            HPD.Agent.Audio.OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            Interrupts++;
            return ValueTask.FromResult(new OutputPlaybackBoundary
            {
                OutputFlowId = outputFlowId,
                ResponseId = new HPD.Agent.Audio.ResponseId("interrupted"),
                PlayedTextLength = 0
            });
        }

        public ValueTask FlushAsync(HPD.Agent.Audio.OutputFlowId outputFlowId, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class ScriptedSessionControl : ILiveKitSessionControlPort
    {
        internal bool DisconnectFails;
        internal bool Reconciled;
        public ValueTask CaptureAsync(AudioFrame frame, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask UnpublishAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisconnectAsync(CancellationToken cancellationToken) => DisconnectFails
            ? ValueTask.FromException(new IOException("lost acknowledgement"))
            : ValueTask.CompletedTask;
        public ValueTask<bool> ReconcileStopAsync(string operationId, CancellationToken cancellationToken) => ValueTask.FromResult(Reconciled);
    }
}

[CollectionDefinition("LiveKit process host", DisableParallelization = true)]
public sealed class LiveKitProcessHostCollection;
