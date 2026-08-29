using System.Runtime.CompilerServices;
using HPD.Agent.Audio.Providers;
using HPD.Audio.Primitives;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio;

#pragma warning disable MEAI001

public sealed record ManagedStreamingSpeechToTextOptionsV1
{
    public required string ModelId { get; init; }
    public string? LanguageCode { get; init; }
    public IReadOnlyList<string> Keyterms { get; init; } = [];
    public bool IncludeTimestamps { get; init; } = true;
    public bool IncludeLanguageDetection { get; init; } = true;
    public bool UseProviderVoiceActivityDetection { get; init; } = true;
}

/// <summary>
/// Bridges a provider's retained normalized STT participant to managed live
/// Audio without exposing provider-native transport contracts.
/// </summary>
public sealed class ManagedStreamingSpeechToTextSourceV1 : IManagedAudioTranscriptSourceV1
{
    private readonly Func<IStreamingSpeechToTextParticipantFactory> _factory;
    private readonly ManagedStreamingSpeechToTextOptionsV1 _options;

    public ManagedStreamingSpeechToTextSourceV1(
        ISpeechToTextClient client,
        ManagedStreamingSpeechToTextOptionsV1 options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);
        var factory = ResolveFactory(client);
        _factory = () => factory;
    }

    private ManagedStreamingSpeechToTextSourceV1(
        Func<IStreamingSpeechToTextParticipantFactory> factory,
        ManagedStreamingSpeechToTextOptionsV1 options)
    {
        _factory = factory;
        _options = options;
    }

    /// <summary>
    /// Captures the speech-to-text client resolved by this AgentBuilder. This
    /// preserves the normal provider, authentication, middleware, and ownership
    /// path while making its retained streaming participant available to the
    /// managed live-session backend.
    /// </summary>
    public static ManagedStreamingSpeechToTextSourceV1 CaptureFrom(
        AgentBuilder builder,
        ManagedStreamingSpeechToTextOptionsV1 options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelId);
        IStreamingSpeechToTextParticipantFactory? captured = null;
        builder.UseSpeechToTextClientMiddleware((client, _) =>
        {
            Interlocked.CompareExchange(ref captured, ResolveFactory(client), null);
            return client;
        });
        return new ManagedStreamingSpeechToTextSourceV1(
            () => Volatile.Read(ref captured) ?? throw new InvalidOperationException(
                "The Agent's speech-to-text client has not been resolved. Build and start the Agent before opening an Audio session."),
            options);
    }

    public async IAsyncEnumerable<ManagedAudioTranscriptCandidateV1> RunAsync(
        IAudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Format.SampleFormat != AudioSampleFormat.Pcm16)
            throw new NotSupportedException("Managed retained STT currently requires decoded PCM16 input.");

        await using var participant = await _factory().CreateAsync(cancellationToken).ConfigureAwait(false);
        await participant.ConnectAsync(new StreamingSpeechToTextConnectRequest
        {
            ModelId = _options.ModelId,
            AudioFormat = new StreamingSpeechToTextAudioFormat
            {
                SampleRateHz = source.Format.SampleRate,
                ChannelCount = source.Format.ChannelCount,
                BitsPerSample = 16,
                Encoding = "pcm"
            },
            CommitStrategy = _options.UseProviderVoiceActivityDetection
                ? StreamingSpeechToTextCommitStrategy.ProviderVoiceActivityDetection
                : StreamingSpeechToTextCommitStrategy.Manual,
            LanguageCode = _options.LanguageCode,
            Keyterms = _options.Keyterms,
            IncludeTimestamps = _options.IncludeTimestamps,
            IncludeLanguageDetection = _options.IncludeLanguageDetection
        }, cancellationToken).ConfigureAwait(false);

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writer = PumpAudioAsync(participant, source, lifetime.Token);
        try
        {
            await foreach (var observation in participant.ReadObservationsAsync(lifetime.Token)
                .ConfigureAwait(false))
            {
                if (observation.Kind is not (
                    StreamingSpeechToTextObservationKind.CommittedTranscript or
                    StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps))
                    continue;
                if (string.IsNullOrWhiteSpace(observation.Text)) continue;
                yield return new ManagedAudioTranscriptCandidateV1
                {
                    CandidateId = $"stt:{observation.ProviderSessionEpoch}:{observation.Sequence}",
                    Text = observation.Text,
                    CommitAutomatically = true
                };
            }
        }
        finally
        {
            lifetime.Cancel();
            try { await writer.ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            await participant.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IStreamingSpeechToTextParticipantFactory ResolveFactory(ISpeechToTextClient client) =>
        client.GetService(typeof(IStreamingSpeechToTextParticipantFactory))
            as IStreamingSpeechToTextParticipantFactory
            ?? throw new NotSupportedException(
                "The selected speech-to-text client does not expose a retained streaming participant.");

    private static async Task PumpAudioAsync(
        IStreamingSpeechToTextParticipant participant,
        IAudioSource source,
        CancellationToken cancellationToken)
    {
        ulong sequence = 0;
        while (true)
        {
            var read = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!read.HasFrame) return;
            var frame = read.Frame;
            if (frame.Data.IsEmpty) continue;
            await participant.WriteAudioAsync(
                new StreamingSpeechToTextAudioChunk(checked(++sequence), frame.Data.Span),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
