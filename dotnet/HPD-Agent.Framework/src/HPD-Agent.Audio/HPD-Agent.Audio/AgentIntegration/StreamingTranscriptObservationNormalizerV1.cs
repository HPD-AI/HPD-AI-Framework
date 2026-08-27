using System.Text;
using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Audio;

/// <summary>Conservatively lowers one retained-provider epoch into the accepted transcript union.</summary>
internal sealed class StreamingTranscriptObservationNormalizerV1
{
    private readonly ExpectedAuthorityVectorV1 _authority;
    private readonly bool _timestampsRequired;
    private readonly TranscriptSourceIdV1 _source = TranscriptSourceIdV1.Create();
    private readonly TranscriptHypothesisIdV1 _hypothesis = TranscriptHypothesisIdV1.Create();
    private ulong _sourceSequence;
    private uint _revision;
    private ulong _providerEpoch;
    private ulong _providerSequence;
    private bool _opened;
    private bool _terminal;

    internal StreamingTranscriptObservationNormalizerV1(
        ExpectedAuthorityVectorV1 authority, bool timestampsRequired = false)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _timestampsRequired = timestampsRequired;
    }

    internal IReadOnlyList<TranscriptObservationV1> Normalize(StreamingSpeechToTextObservation provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_terminal) throw new InvalidDataException("Transcript source is terminal.");
        ValidateProviderOrder(provider);
        var result = new List<TranscriptObservationV1>(4);
        EnsureOpened(result, provider);
        switch (provider.Kind)
        {
            case StreamingSpeechToTextObservationKind.PartialTranscript:
                AddText(result, provider, final: false);
                break;
            case StreamingSpeechToTextObservationKind.FinalTranscript:
            case StreamingSpeechToTextObservationKind.FinalTranscriptWithTimestamps:
                AddText(result, provider, final: true);
                break;
            case StreamingSpeechToTextObservationKind.CommittedTranscript:
                AddText(result, provider, final: !_timestampsRequired);
                break;
            case StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps:
                AddText(result, provider, final: true);
                Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.ProviderItemCompleted(
                    o, _source, _hypothesis, q, b, _authority, p));
                break;
            case StreamingSpeechToTextObservationKind.Error:
                Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.SourceHealthChanged(
                    o, _source, _hypothesis, q, b, _authority, p, TranscriptSourceHealthV1.Unavailable));
                Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.DiscontinuityObserved(
                    o, _source, _hypothesis, q, b, _authority, p));
                _terminal = true;
                break;
            case StreamingSpeechToTextObservationKind.SessionClosed:
                Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.SourceCompleted(
                    o, _source, _hypothesis, q, b, _authority, p));
                _terminal = true;
                break;
            case StreamingSpeechToTextObservationKind.Unknown:
                Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.OpaqueHypothesis(
                    o, _source, _hypothesis, q, b, _authority, p, new BoundedAscii("unknown-provider-observation")));
                break;
            default:
                throw new InvalidDataException("Provider emitted an unknown transcript observation kind.");
        }
        if (!string.IsNullOrWhiteSpace(provider.LanguageCode))
            Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.LanguageObserved(
                o, _source, _hypothesis, q, b, _authority, p,
                new BoundedAscii(BoundAscii(provider.LanguageCode!, 32))));
        return result;
    }

    private void ValidateProviderOrder(StreamingSpeechToTextObservation provider)
    {
        if (provider.ProviderSessionEpoch == 0 || provider.Sequence == 0)
            throw new InvalidDataException("Provider transcript identity is incomplete.");
        if (_providerEpoch == 0) _providerEpoch = provider.ProviderSessionEpoch;
        if (provider.ProviderSessionEpoch != _providerEpoch)
            throw new InvalidDataException("Provider epoch changed without supervised replacement.");
        if (provider.Sequence != checked(_providerSequence + 1))
            throw new InvalidDataException("Provider transcript sequence is discontinuous.");
        _providerSequence = provider.Sequence;
    }

    private void EnsureOpened(List<TranscriptObservationV1> result, StreamingSpeechToTextObservation provider)
    {
        if (_opened) return;
        var digest = Provenance(provider, "open");
        result.Add(new TranscriptObservationV1.HypothesisOpened(
            ProviderObservationIdV1.Create(), _source, _hypothesis, checked(++_sourceSequence), _authority, digest));
        _opened = true;
        _revision = 1;
    }

    private void AddText(List<TranscriptObservationV1> result, StreamingSpeechToTextObservation provider, bool final)
    {
        if (string.IsNullOrWhiteSpace(provider.Text))
        {
            Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.NoSpeechObserved(
                o, _source, _hypothesis, q, b, _authority, p));
            return;
        }
        Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.TextReplaced(
            o, _source, _hypothesis, q, b, _authority, p, provider.Text));
        if (!final) return;
        Add(result, provider, (o, q, b, p) => new TranscriptObservationV1.FinalityAsserted(
            o, _source, _hypothesis, q, b, _authority, p,
            new TranscriptFinalityV1(TranscriptMutabilityV1.ImmutableUnderSourceGuarantee, null,
                TranscriptFinalizedScopeV1.ProviderItem, TranscriptBoundaryEvidenceV1.ProviderEndpoint,
                TranscriptContinuityV1.Complete, TranscriptObservabilityV1.Observed,
                TranscriptCorrectionStateV1.CorrectionWindowClosed,
                TranscriptAssemblyClosureV1.ClosedForAssembly)));
    }

    private void Add(List<TranscriptObservationV1> result, StreamingSpeechToTextObservation provider,
        Func<ProviderObservationIdV1, ulong, TranscriptRevisionV1, Hash256, TranscriptObservationV1> create)
    {
        var sequence = checked(++_sourceSequence);
        var revision = new TranscriptRevisionV1(_revision++);
        result.Add(create(ProviderObservationIdV1.Create(), sequence, revision, Provenance(provider, sequence.ToString())));
    }

    private static Hash256 Provenance(StreamingSpeechToTextObservation provider, string discriminator) =>
        Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('|', provider.ProviderSessionEpoch,
            provider.Sequence, provider.Kind, provider.ProviderEventType, provider.EvidenceSha256, discriminator)));

    private static string BoundAscii(string value, int maximum) => new string(value
        .Where(static c => c is >= ' ' and <= '~').Take(maximum).ToArray()) is { Length: > 0 } bounded
            ? bounded : "und";
}
