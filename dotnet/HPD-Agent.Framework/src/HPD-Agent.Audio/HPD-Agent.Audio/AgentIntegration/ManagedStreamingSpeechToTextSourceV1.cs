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

    public async IAsyncEnumerable<ManagedAudioInputObservationV1> RunAsync(
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
        var speechOpen = false;
        try
        {
            await foreach (var observation in participant.ReadObservationsAsync(lifetime.Token)
                .ConfigureAwait(false))
            {
                if (!speechOpen && IsSpeechEvidenceObservation(observation))
                {
                    speechOpen = true;
                    yield return new ManagedAudioSpeechStartedV1
                    {
                        ObservationId = $"stt:{observation.ProviderSessionEpoch}:speech:{observation.Sequence}"
                    };
                }
                if (!IsAutomaticCommitObservation(observation.Kind, _options.IncludeTimestamps))
                    continue;
                if (string.IsNullOrWhiteSpace(observation.Text)) continue;
                yield return new ManagedAudioTranscriptCandidateV1
                {
                    CandidateId = $"stt:{observation.ProviderSessionEpoch}:{observation.Sequence}",
                    Text = observation.Text,
                    CommitAutomatically = true
                };
                speechOpen = false;
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

    internal static bool IsAutomaticCommitObservation(
        StreamingSpeechToTextObservationKind kind,
        bool timestampsRequested) => timestampsRequested
            ? kind == StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps
            : kind == StreamingSpeechToTextObservationKind.CommittedTranscript;

    internal static bool IsSpeechEvidenceObservation(StreamingSpeechToTextObservation observation) =>
        !string.IsNullOrWhiteSpace(observation.Text) && observation.Kind is
            StreamingSpeechToTextObservationKind.PartialTranscript or
            StreamingSpeechToTextObservationKind.FinalTranscript or
            StreamingSpeechToTextObservationKind.FinalTranscriptWithTimestamps or
            StreamingSpeechToTextObservationKind.CommittedTranscript or
            StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps;

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
