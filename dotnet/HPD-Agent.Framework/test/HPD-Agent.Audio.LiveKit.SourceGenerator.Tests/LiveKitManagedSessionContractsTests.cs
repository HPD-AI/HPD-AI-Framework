using System.Text;
using System.Text.Json;
using HPD.Audio.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Audio.LiveKit.SourceGenerator.Tests;

public sealed class LiveKitManagedSessionContractsTests
{
    [Fact]
    public void Bindings_UsesReviewedSchemaIdentity_AndOwnsPayload()
    {
        var binding = new LiveKitAudioSessionBinding
        {
            RoomName = "room-1",
            ParticipantIdentity = "agent-1",
            RemoteParticipantIdentity = "browser-1"
        };

        var bindings = LiveKitAudioTransport.Bindings(binding);
        var encoded = Assert.Single(bindings.Bindings);
        var decoded = LiveKitAudioTransport.Decode(bindings);

        Assert.Equal("livekit", encoded.ComponentInstance);
        Assert.Equal("hpd.provider.livekit.audiotransport.sessionbinding", encoded.Schema);
        Assert.Equal(1u, encoded.Version);
        Assert.Equal(binding, decoded);
    }

    [Fact]
    public void ManagedCredential_MintsRoomAndIdentityBoundParticipantToken()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var credential = "LK1\napi-key\napi-secret".ToCharArray();
        var binding = new LiveKitAudioSessionBinding
        {
            RoomName = "room-1",
            ParticipantIdentity = "agent-1"
        };

        var token = LiveKitParticipantToken.Resolve(
            credential, new LiveKitTransportProviderConfig { ParticipantTokenTtlSeconds = 120 }, binding, now);
        var pieces = new string(token).Split('.');
        var payload = JsonDocument.Parse(Decode(pieces[1])).RootElement;

        Assert.Equal("api-key", payload.GetProperty("iss").GetString());
        Assert.Equal("agent-1", payload.GetProperty("sub").GetString());
        Assert.Equal("room-1", payload.GetProperty("video").GetProperty("room").GetString());
        Assert.Equal(now.AddSeconds(120).ToUnixTimeSeconds(), payload.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void SuppliedCredential_RejectsMismatchedParticipantIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var header = Encode("{\"alg\":\"none\"}");
        var payload = Encode($"{{\"sub\":\"someone-else\",\"exp\":{now.AddMinutes(5).ToUnixTimeSeconds()}}}");
        var token = $"{header}.{payload}.signature".ToCharArray();

        Assert.Throws<InvalidDataException>(() => LiveKitParticipantToken.Resolve(
            token,
            new LiveKitTransportProviderConfig(),
            new LiveKitAudioSessionBinding { RoomName = "room-1", ParticipantIdentity = "agent-1" },
            now));
    }

    [Fact]
    public async Task DirectBuilder_InstallsManagedAuthorityWithoutOpeningTransport()
    {
        await using var agent = await AgentBuilder.Create()
            .WithManagedLiveKitAudio(Options())
            .BuildAsync();

        await agent.StartAsync();

        Assert.Single(agent.Middlewares.OfType<HPD.Agent.Audio.AgentIntegration.Middleware.AudioRuntimeAttachment>());
    }

    [Fact]
    public async Task ServiceCollection_RegistersSameManagedAuthorityGraph()
    {
        await using var services = new ServiceCollection()
            .AddManagedLiveKitAudio(Options())
            .BuildServiceProvider();

        Assert.Same(
            services.GetRequiredService<ManagedAudioSessionAuthorityV1>(),
            services.GetRequiredService<IAudioSessionControlAuthorityV1>());
    }

    [Fact]
    public void ManagedBackend_DefaultsToTheRetainedPcmSpeechRate()
    {
        Assert.Equal(16_000, Options().AudioSampleRateHz);
    }

    private static LiveKitManagedAudioSessionBackendOptions Options() => new()
    {
        Endpoint = "ws://127.0.0.1:7880",
        CredentialResolver = static (_, _) => ValueTask.FromResult("token.value.signature".ToCharArray()),
        TranscriptSource = new EmptyTranscriptSource(),
        VerifyNativeArtifact = false
    };

    private sealed class EmptyTranscriptSource : IManagedAudioTranscriptSourceV1
    {
        public async IAsyncEnumerable<ManagedAudioTranscriptCandidateV1> RunAsync(
            IAudioSource source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - text.Length % 4) % 4), '='));
    }
}
