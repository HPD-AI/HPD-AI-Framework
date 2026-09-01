using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Runtime.Scenarios;
using HPD.Agent.Audio.Turns;
using HPD.Agent.Providers.Audio.Meai;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable EXTEXP0001

public sealed class MeaiBatchSpeechToTextInteractionSessionTests
{
    [Fact]
    public async Task SendAsync_InputMedia_EmitsFinalTranscript()
    {
        var content = TestInputContent.Audio("meai.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("hello from meai");
        var resolver = FakeInputContentSourceResolver.Opened(content, [1, 2, 3]);
        await using var session = CreateSession(client, resolver);

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateEnvelope(content)));

        var updates = await ReadUpdatesAsync(session);
        var attempt = Assert.Single(updates.OfType<ProviderAttemptTerminalUpdate>());
        Assert.Equal(ProviderOperationOutcome.Succeeded, attempt.Outcome);
        var update = Assert.Single(updates.OfType<TranscriptUpdate>());
        var transcript = Assert.IsType<TranscriptUpdate>(update);
        Assert.Equal(TranscriptProjectionStageV1.Final, transcript.Stage);
        Assert.Equal("hello from meai", transcript.Text);
        Assert.Equal(content.Id, transcript.InputContentId);
    }

    [Fact]
    public async Task SendAsync_PassesOptionsAndResolverStreamBytes_ToSpeechToTextClient()
    {
        var content = TestInputContent.Audio("options.ogg", "audio/ogg");
        var bytes = new byte[] { 9, 8, 7, 6 };
        var client = new FakeSpeechToTextClient("options transcript");
        var resolver = FakeInputContentSourceResolver.Opened(content, bytes);
        await using var session = CreateSession(
            client,
            resolver,
            new MeaiBatchSpeechToTextInteractionSessionOptions
            {
                ModelId = "stt-model",
                SpeechLanguage = "en-US",
                SpeechSampleRate = 24_000
            });

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateEnvelope(content, sampleRateHz: 16_000)));

        Assert.Equal(bytes, client.LastAudioBytes);
        Assert.NotNull(client.LastOptions);
        Assert.Equal("stt-model", client.LastOptions.ModelId);
        Assert.Equal("en-US", client.LastOptions.SpeechLanguage);
        Assert.Equal(24_000, client.LastOptions.SpeechSampleRate);
    }

    [Fact]
    public async Task SendAsync_UsesEnvelopeSampleRate_WhenOptionSampleRateIsUnset()
    {
        var content = TestInputContent.Audio("sample-rate.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("sample transcript");
        var resolver = FakeInputContentSourceResolver.Opened(content, [4, 5, 6]);
        await using var session = CreateSession(
            client,
            resolver,
            new MeaiBatchSpeechToTextInteractionSessionOptions
            {
                ModelId = "stt-model",
                SpeechLanguage = "fr"
            });

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateEnvelope(content, sampleRateHz: 48_000)));

        Assert.NotNull(client.LastOptions);
        Assert.Equal(48_000, client.LastOptions.SpeechSampleRate);
    }

    [Fact]
    public async Task SendAsync_ResolverFailure_EmitsErrorWithoutTranscript()
    {
        var content = TestInputContent.Audio("missing.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("should not be used");
        var resolver = FakeInputContentSourceResolver.NotResolved(
            content,
            InputContentSourceOpenStatus.NotFound,
            "source missing");
        await using var session = CreateSession(client, resolver);

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateEnvelope(content)));

        var update = Assert.Single(await ReadUpdatesAsync(session));
        var error = Assert.IsType<ProviderErrorUpdate>(update);
        Assert.Equal("meai-stt.resolver-notfound", error.Error.Code);
        Assert.Equal(0, client.GetTextCount);
    }

    [Fact]
    public async Task SendAsync_MetadataOnlyPayload_EmitsUnreadableSourceError()
    {
        var content = TestInputContent.Audio("metadata.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("should not be used");
        var resolver = FakeInputContentSourceResolver.Opened(content, [1]);
        await using var session = CreateSession(client, resolver);

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateMetadataOnlyEnvelope(content)));

        var update = Assert.Single(await ReadUpdatesAsync(session));
        var error = Assert.IsType<ProviderErrorUpdate>(update);
        Assert.Equal("meai-stt.unreadable-source", error.Error.Code);
        Assert.Equal(0, client.GetTextCount);
    }

    [Fact]
    public async Task SendAsync_MeaiException_EmitsErrorWithoutTranscript()
    {
        var content = TestInputContent.Audio("throws.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("should not be used")
        {
            Exception = new InvalidOperationException("provider failed")
        };
        var resolver = FakeInputContentSourceResolver.Opened(content, [1, 2, 3]);
        await using var session = CreateSession(client, resolver);

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateEnvelope(content)));

        var updates = await ReadUpdatesAsync(session);
        var attempt = Assert.Single(updates.OfType<ProviderAttemptTerminalUpdate>());
        Assert.Equal(ProviderOperationOutcome.Failed, attempt.Outcome);
        var update = Assert.Single(updates.OfType<ProviderErrorUpdate>());
        var error = Assert.IsType<ProviderErrorUpdate>(update);
        Assert.Equal("meai-stt.transcription-exception", error.Error.Code);
        Assert.Contains("provider failed", error.Error.Message);
    }

    [Fact]
    public async Task SendAsync_EmptyTranscript_EmitsErrorByDefault()
    {
        var content = TestInputContent.Audio("empty.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("");
        var resolver = FakeInputContentSourceResolver.Opened(content, [1, 2, 3]);
        await using var session = CreateSession(client, resolver);

        await session.OpenAsync(CreatePlan());
        await session.SendAsync(new InteractionInputMedia(CreateEnvelope(content)));

        var updates = await ReadUpdatesAsync(session);
        var attempt = Assert.Single(updates.OfType<ProviderAttemptTerminalUpdate>());
        Assert.Equal(ProviderOperationOutcome.Succeeded, attempt.Outcome);
        var update = Assert.Single(updates.OfType<ProviderErrorUpdate>());
        var error = Assert.IsType<ProviderErrorUpdate>(update);
        Assert.Equal("meai-stt.empty-transcript", error.Error.Code);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeCallerOwnedClient_ByDefault()
    {
        var content = TestInputContent.Audio("owned.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("owned transcript");
        var resolver = FakeInputContentSourceResolver.Opened(content, [1]);
        var session = CreateSession(client, resolver);

        await session.DisposeAsync();

        Assert.False(client.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesClient_WhenConfigured()
    {
        var content = TestInputContent.Audio("dispose.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("dispose transcript");
        var resolver = FakeInputContentSourceResolver.Opened(content, [1]);
        var session = CreateSession(
            client,
            resolver,
            new MeaiBatchSpeechToTextInteractionSessionOptions
            {
                DisposeClient = true
            });

        await session.DisposeAsync();

        Assert.True(client.IsDisposed);
    }

    [Fact]
    public async Task AudioInteractionRuntime_WithMeaiFactory_CommitsTranscriptThroughRunner()
    {
        var content = TestInputContent.Audio("runtime.wav", "audio/wav");
        var client = new FakeSpeechToTextClient("runtime transcript");
        var resolver = FakeInputContentSourceResolver.Opened(content, [3, 2, 1]);
        var runner = new AudioInteractionRuntimeRunner();

        var result = await runner.RunAsync(new AudioInteractionRuntimeRequest
        {
            SessionId = new AudioSessionId("meai-runtime-session"),
            Inputs = [],
            InputContentRefs = [content],
            ThreadRef = new ThreadRef("audio-test-agent", "meai-runtime-session", "main"),
            InteractionSessionFactory = new MeaiBatchSpeechToTextInteractionSessionFactory(
                client,
                resolver)
        });

        Assert.Contains(result.LedgerRecords.ToArray().OfType<TranscriptLedgerRecord>(), r =>
            r.InputContentId == content.Id &&
            r.Text == "runtime transcript");
        Assert.Contains(result.LedgerRecords.ToArray().OfType<UserTurnLedgerRecord>(), r =>
            r.Text == "runtime transcript" &&
            r.CommitReason == EndpointCommitProjectionReasonV1.InputMediaTranscript);
        Assert.Equal([3, 2, 1], client.LastAudioBytes);
    }

    private static MeaiBatchSpeechToTextInteractionSession CreateSession(
        FakeSpeechToTextClient client,
        IInputContentSourceResolver resolver,
        MeaiBatchSpeechToTextInteractionSessionOptions? options = null)
        => new(
            new InteractionSessionId("meai-test-session"),
            client,
            resolver,
            options);

    private static InteractionExecutionPlan CreatePlan()
        => new()
        {
            Topology = AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech,
            RouteEpoch = new ProviderRouteEpoch
            {
                Id = new ProviderRouteEpochId("meai-test-route-epoch"),
                ProviderKey = "meai-stt",
                StartedAt = DateTimeOffset.UtcNow
            },
            Capabilities = new ProviderCapabilityProfile
            {
                ProviderKey = "meai-stt",
                Declared = new ProviderDeclaredCapabilities
                {
                    Flags = ProviderCapabilityFlag.SpeechToText
                }
            }
        };

    private static CanonicalMediaEnvelope CreateEnvelope(
        InputContentRef content,
        int? sampleRateHz = null)
        => new()
        {
            SessionId = new AudioSessionId("meai-test-audio-session"),
            Kind = MediaKind.Audio,
            Direction = MediaDirection.Inbound,
            Payload = new MediaPayloadRef.InputContent(content),
            Format = new MediaFormatDescriptor
            {
                MediaType = content.MediaType ?? "audio/wav",
                SampleRateHz = sampleRateHz
            }
        };

    private static CanonicalMediaEnvelope CreateMetadataOnlyEnvelope(
        InputContentRef content)
        => new()
        {
            SessionId = new AudioSessionId("meai-test-audio-session"),
            Kind = MediaKind.Audio,
            Direction = MediaDirection.Inbound,
            Payload = new MediaPayloadRef.MetadataOnly(content.Sha256, "metadata-only"),
            Format = new MediaFormatDescriptor
            {
                MediaType = content.MediaType ?? "audio/wav"
            }
        };

    private static async Task<IReadOnlyList<AudioInteractionUpdate>> ReadUpdatesAsync(
        IAudioInteractionSession session)
    {
        var updates = new List<AudioInteractionUpdate>();
        await foreach (var update in session.Updates)
        {
            updates.Add(update);
        }

        return updates;
    }

    private sealed class FakeInputContentSourceResolver : IInputContentSourceResolver
    {
        private readonly InputContentSourceOpenResult _result;

        private FakeInputContentSourceResolver(InputContentSourceOpenResult result)
        {
            _result = result;
        }

        public static FakeInputContentSourceResolver Opened(
            InputContentRef content,
            byte[] bytes)
            => new(InputContentSourceOpenResult.Opened(new InputContentSource
            {
                InputContentId = content.Id,
                MediaType = content.MediaType ?? "audio/wav",
                Name = content.Name,
                SizeBytes = bytes.LongLength,
                Sha256 = content.Sha256,
                OpenStreamAsync = cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
                }
            }));

        public static FakeInputContentSourceResolver NotResolved(
            InputContentRef content,
            InputContentSourceOpenStatus status,
            string reason)
            => new(InputContentSourceOpenResult.NotResolved(content.Id, status, reason));

        public ValueTask<InputContentSourceOpenResult> OpenAsync(
            InputContentRef inputContent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class FakeSpeechToTextClient : ISpeechToTextClient
    {
        private readonly string _transcript;

        public FakeSpeechToTextClient(string transcript)
        {
            _transcript = transcript;
        }

        public byte[]? LastAudioBytes { get; private set; }

        public SpeechToTextOptions? LastOptions { get; private set; }

        public int GetTextCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public Exception? Exception { get; init; }

        public async Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            GetTextCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            LastOptions = options;
            await using var copy = new MemoryStream();
            await audioSpeechStream.CopyToAsync(copy, cancellationToken);
            LastAudioBytes = copy.ToArray();
            return new SpeechToTextResponse(_transcript);
        }

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return (await GetTextAsync(audioSpeechStream, options, cancellationToken))
                .ToSpeechToTextResponseUpdates()
                .Single();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : null;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}

#pragma warning restore EXTEXP0001
