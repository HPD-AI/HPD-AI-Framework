using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Providers.Audio.ElevenLabs;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class ElevenLabsRealtimeSpeechToTextParticipantTests
{
    [Fact]
    public async Task Connect_WaitsForSessionStartedAndFreezesEffectiveFormat()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);

        var ready = await participant.ConnectAsync(Request());

        Assert.Equal(StreamingSpeechToTextParticipantState.Ready, participant.State);
        Assert.Equal<ulong>(1, ready.ProviderSessionEpoch);
        Assert.Equal("sess-1", ready.ProviderSessionId);
        Assert.Equal(16000, ready.EffectiveAudioFormat.SampleRateHz);
        Assert.Equal("secret", socket.ApiKey);
        Assert.Contains("model_id=scribe_v2_realtime", socket.Uri!.Query);
        Assert.Contains("audio_format=pcm_16000", socket.Uri.Query);
        Assert.Contains("commit_strategy=manual", socket.Uri.Query);
    }

    [Fact]
    public async Task Connect_RejectsAnythingBeforeSessionStarted()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "partial_transcript", text = "too early" });
        await using var participant = CreateParticipant(socket);

        await Assert.ThrowsAsync<InvalidDataException>(() => participant.ConnectAsync(Request()).AsTask());
        Assert.Equal(StreamingSpeechToTextParticipantState.Faulted, participant.State);
    }

    [Fact]
    public async Task AudioAndMultipleCommits_ReuseOneSocketAndSendContextOnlyOnce()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request() with { PreviousText = "prior context" });

        await participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(1, [1, 2, 3, 4]));
        var firstCommit = await participant.CommitAsync(new() { OperationId = "commit-1" });
        await participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(2, [5, 6, 7, 8]));
        var secondCommit = await participant.CommitAsync(new() { OperationId = "commit-2" });

        Assert.Equal(1, socket.ConnectCount);
        Assert.Equal<ulong>(1, firstCommit.DispatchSequence);
        Assert.Equal<ulong>(2, secondCommit.DispatchSequence);
        Assert.Equal(StreamingSpeechToTextCommitDispatchOutcome.DispatchedOutcomeUnknown, firstCommit.Outcome);
        Assert.Equal(4, socket.Sent.Count);

        using var audio1 = JsonDocument.Parse(socket.Sent[0]);
        using var commit1 = JsonDocument.Parse(socket.Sent[1]);
        using var audio2 = JsonDocument.Parse(socket.Sent[2]);
        using var commit2 = JsonDocument.Parse(socket.Sent[3]);
        Assert.Equal("prior context", audio1.RootElement.GetProperty("previous_text").GetString());
        Assert.False(audio2.RootElement.TryGetProperty("previous_text", out _));
        Assert.False(audio1.RootElement.GetProperty("commit").GetBoolean());
        Assert.True(commit1.RootElement.GetProperty("commit").GetBoolean());
        Assert.True(commit2.RootElement.GetProperty("commit").GetBoolean());
        Assert.Equal(string.Empty, commit1.RootElement.GetProperty("audio_base_64").GetString());
    }

    [Fact]
    public async Task Observations_PreserveEveryProviderFinalityClassAndArrivalOrder()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        socket.EnqueueJson(new { message_type = "partial_transcript", text = "hel" });
        socket.EnqueueJson(new { message_type = "final_transcript", text = "hello" });
        socket.EnqueueJson(new
        {
            message_type = "final_transcript_with_timestamps",
            text = "hello",
            language_code = "en",
            words = new[] { new { text = "hello", start = 0.1, end = 0.5 } }
        });
        socket.EnqueueJson(new { message_type = "committed_transcript", text = "hello" });
        socket.EnqueueJson(new { message_type = "committed_transcript_with_timestamps", text = "hello!" });
        socket.EnqueueJson(new { message_type = "rate_limited", error = "slow down" });
        socket.EnqueueJson(new { message_type = "future_event", value = 42 });
        socket.EnqueueClose();
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        var observations = new List<StreamingSpeechToTextObservation>();
        await foreach (var observation in participant.ReadObservationsAsync())
            observations.Add(observation);

        Assert.Equal(
            [
                StreamingSpeechToTextObservationKind.PartialTranscript,
                StreamingSpeechToTextObservationKind.FinalTranscript,
                StreamingSpeechToTextObservationKind.FinalTranscriptWithTimestamps,
                StreamingSpeechToTextObservationKind.CommittedTranscript,
                StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps,
                StreamingSpeechToTextObservationKind.Error,
                StreamingSpeechToTextObservationKind.Unknown,
                StreamingSpeechToTextObservationKind.SessionClosed
            ],
            observations.Select(static value => value.Kind));
        Assert.Equal(Enumerable.Range(1, 8).Select(static value => (ulong)value), observations.Select(static value => value.Sequence));
        Assert.All(observations, static value => Assert.Equal<ulong>(1, value.ProviderSessionEpoch));
        Assert.Equal("en", observations[2].LanguageCode);
        Assert.Equal(TimeSpan.FromSeconds(0.1), observations[2].WordTimings[0].Start);
        Assert.Equal("hello!", observations[4].Text);
        Assert.Equal("rate_limited", observations[5].SafeCode);
        Assert.Equal("future_event", observations[6].ProviderEventType);
    }

    [Fact]
    public async Task ObservationStream_RejectsASecondReader()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());
        await using var first = participant.ReadObservationsAsync().GetAsyncEnumerator();
        var firstMove = first.MoveNextAsync().AsTask();

        await using var second = participant.ReadObservationsAsync().GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.MoveNextAsync().AsTask());

        socket.EnqueueClose();
        Assert.True(await firstMove);
    }

    [Fact]
    public async Task OneHundredTurns_StayOnOneProviderSession()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-soak" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        for (var turn = 1; turn <= 100; turn++)
        {
            await participant.WriteAudioAsync(
                new StreamingSpeechToTextAudioChunk((ulong)turn, [(byte)(turn % 255), 0]));
            var receipt = await participant.CommitAsync(new() { OperationId = $"commit-{turn}" });
            Assert.Equal((ulong)turn, receipt.DispatchSequence);
        }

        Assert.Equal(1, socket.ConnectCount);
        Assert.Equal(200, socket.Sent.Count);
    }

    [Fact]
    public async Task ProviderVad_RejectsExplicitCommitAndRequiresReconnectForUpdates()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-vad" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request() with
        {
            CommitStrategy = StreamingSpeechToTextCommitStrategy.ProviderVoiceActivityDetection
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            participant.CommitAsync(new() { OperationId = "not-allowed" }).AsTask());
        Assert.Equal(
            StreamingSpeechToTextUpdateDisposition.ReconnectRequired,
            await participant.UpdateAsync(new() { LanguageCode = "fr" }));
    }

    [Fact]
    public async Task AudioSequenceGap_IsRejectedBeforeWireSend()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(2, [1, 2])).AsTask());
        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task AudioBound_AcceptsOneSecondAndRejectsOneByteMore()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        await participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(1, new byte[32_000]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(2, new byte[32_001])).AsTask());
        Assert.Single(socket.Sent);
    }

    [Fact]
    public async Task OutboundAdmission_RejectsConcurrentWaitersInsteadOfBufferingThem()
    {
        var socket = new ScriptedSocket { PauseSends = true };
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        var first = participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(1, [1, 2])).AsTask();
        await socket.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(2, [3, 4])).AsTask());
        socket.ReleaseSend.TrySetResult();
        await first;
    }

    [Fact]
    public async Task Stop_CancelsTheSoleReaderAndSettlesAsSessionClosed()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());
        await using var reader = participant.ReadObservationsAsync().GetAsyncEnumerator();
        var pending = reader.MoveNextAsync().AsTask();

        await participant.StopAsync();

        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(StreamingSpeechToTextObservationKind.SessionClosed, reader.Current.Kind);
        Assert.Equal(StreamingSpeechToTextParticipantState.Stopped, participant.State);
    }

    [Fact]
    public async Task MalformedAndUnknownMessages_ProduceBoundedDiagnosticEvidence()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        socket.EnqueueRaw("{not-json");
        socket.EnqueueJson(new { message_type = "future_event", secret = new string('x', 10_000) });
        socket.EnqueueClose();
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        var observations = new List<StreamingSpeechToTextObservation>();
        await foreach (var observation in participant.ReadObservationsAsync())
            observations.Add(observation);

        Assert.Equal("malformed-provider-message", observations[0].SafeCode);
        Assert.Equal(64, observations[0].EvidenceSha256!.Length);
        Assert.Equal(StreamingSpeechToTextObservationKind.Unknown, observations[1].Kind);
        Assert.Equal(64, observations[1].EvidenceSha256!.Length);
        Assert.Null(observations[1].Detail);
    }

    [Fact]
    public async Task ResolvedProviderSettings_AreLoweredByTheSharedProtocolBuilder()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-vad" });
        await using var participant = new ElevenLabsRealtimeSpeechToTextParticipant(
            "secret",
            new ElevenLabsSttRuntimeSettings
            {
                WebSocketBaseUrl = "wss://example.test/v1",
                NoVerbatim = true,
                VadThreshold = 0.42,
                MinSpeechDurationMilliseconds = 120,
                EnableLogging = false
            },
            () => socket);

        await participant.ConnectAsync(Request() with
        {
            CommitStrategy = StreamingSpeechToTextCommitStrategy.ProviderVoiceActivityDetection
        });

        Assert.Contains("no_verbatim=true", socket.Uri!.Query);
        Assert.Contains("vad_threshold=0.42", socket.Uri.Query);
        Assert.Contains("min_speech_duration_ms=120", socket.Uri.Query);
        Assert.Contains("enable_logging=false", socket.Uri.Query);
    }

    [Fact]
    public async Task OversizedProviderMessage_IsTypedWithoutParsingOrRetention()
    {
        var socket = new ScriptedSocket();
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        socket.EnqueueCapacityExceeded();
        socket.EnqueueClose();
        await using var participant = CreateParticipant(socket);
        await participant.ConnectAsync(Request());

        await using var reader = participant.ReadObservationsAsync().GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("provider-message-capacity-exceeded", reader.Current.SafeCode);
        Assert.Equal(64, reader.Current.EvidenceSha256!.Length);
    }

    [Fact]
    public async Task ConnectCancellation_FaultsWithoutFabricatingReadiness()
    {
        var socket = new ScriptedSocket();
        await using var participant = CreateParticipant(socket);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            participant.ConnectAsync(Request(), cancellation.Token).AsTask());
        Assert.Equal(StreamingSpeechToTextParticipantState.Faulted, participant.State);
    }

    [Fact]
    public async Task StopDuringConnect_CannotResurrectTheParticipant()
    {
        var socket = new ScriptedSocket { PauseConnect = true };
        socket.EnqueueJson(new { message_type = "session_started", session_id = "too-late" });
        await using var participant = CreateParticipant(socket, TimeSpan.FromMilliseconds(250));
        var connect = participant.ConnectAsync(Request()).AsTask();
        await socket.ConnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await participant.StopAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.Equal(StreamingSpeechToTextParticipantState.Stopped, participant.State);
    }

    [Fact]
    public async Task CancellationHostileSend_CannotHangShutdownPastItsBudget()
    {
        var socket = new ScriptedSocket { PauseSends = true, IgnoreSendCancellation = true };
        socket.EnqueueJson(new { message_type = "session_started", session_id = "sess-1" });
        var participant = CreateParticipant(socket, TimeSpan.FromMilliseconds(50));
        await participant.ConnectAsync(Request());
        var send = participant.WriteAudioAsync(new StreamingSpeechToTextAudioChunk(1, [1, 2])).AsTask();
        await socket.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutException>(() => participant.StopAsync().AsTask());
        Assert.Equal(StreamingSpeechToTextParticipantState.Faulted, participant.State);

        socket.ReleaseSend.TrySetResult();
        await send;
        await participant.DisposeAsync();
    }

    private static ElevenLabsRealtimeSpeechToTextParticipant CreateParticipant(
        ScriptedSocket socket,
        TimeSpan? shutdownTimeout = null) =>
        new("secret", new ElevenLabsSttRuntimeSettings
        {
            WebSocketBaseUrl = "wss://example.test/v1"
        }, () => socket, shutdownTimeout);

    private static StreamingSpeechToTextConnectRequest Request() => new()
    {
        ModelId = "scribe_v2_realtime",
        AudioFormat = new StreamingSpeechToTextAudioFormat
        {
            SampleRateHz = 16000,
            ChannelCount = 1,
            BitsPerSample = 16
        },
        CommitStrategy = StreamingSpeechToTextCommitStrategy.Manual
    };

    private sealed class ScriptedSocket : IElevenLabsRealtimeSttSocket
    {
        private readonly Channel<ElevenLabsRealtimeSttSocketMessage> _incoming =
            Channel.CreateUnbounded<ElevenLabsRealtimeSttSocketMessage>();

        internal List<byte[]> Sent { get; } = [];
        internal Uri? Uri { get; private set; }
        internal string? ApiKey { get; private set; }
        internal int ConnectCount { get; private set; }
        internal bool PauseSends { get; init; }
        internal bool IgnoreSendCancellation { get; init; }
        internal bool PauseConnect { get; init; }
        internal TaskCompletionSource ConnectEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseConnect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SendEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsOpen { get; private set; }

        public async ValueTask ConnectAsync(Uri uri, string apiKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri = uri;
            ApiKey = apiKey;
            ConnectCount++;
            ConnectEntered.TrySetResult();
            if (PauseConnect)
                await ReleaseConnect.Task.WaitAsync(cancellationToken);
            IsOpen = true;
        }

        public async ValueTask SendTextAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOpen)
                throw new InvalidOperationException("Socket is closed.");
            SendEntered.TrySetResult();
            if (PauseSends)
            {
                if (IgnoreSendCancellation)
                    await ReleaseSend.Task;
                else
                    await ReleaseSend.Task.WaitAsync(cancellationToken);
            }
            Sent.Add(payload.ToArray());
        }

        public ValueTask<ElevenLabsRealtimeSttSocketMessage> ReceiveAsync(CancellationToken cancellationToken) =>
            _incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        internal void EnqueueJson<T>(T value) =>
            _incoming.Writer.TryWrite(new(false, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

        internal void EnqueueClose() =>
            _incoming.Writer.TryWrite(new(true, ReadOnlyMemory<byte>.Empty));

        internal void EnqueueRaw(string value) =>
            _incoming.Writer.TryWrite(new(false, Encoding.UTF8.GetBytes(value)));

        internal void EnqueueCapacityExceeded() =>
            _incoming.Writer.TryWrite(new(false, ReadOnlyMemory<byte>.Empty, true, new string('a', 64)));
    }
}
