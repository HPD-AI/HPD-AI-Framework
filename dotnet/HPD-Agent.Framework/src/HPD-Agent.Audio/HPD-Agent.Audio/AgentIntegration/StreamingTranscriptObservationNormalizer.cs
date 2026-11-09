using System.Text;
using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Audio;

/// <summary>Conservatively lowers one retained-provider epoch into the accepted transcript union.</summary>
internal sealed class StreamingTranscriptObservationNormalizer
{
    private readonly ExpectedAuthorityVectorV1 _authority;
    private readonly bool _timestampsRequired;
    private readonly TranscriptSourceId _source = TranscriptSourceId.Create();
    private readonly TranscriptHypothesisId _hypothesis = TranscriptHypothesisId.Create();
    private ulong _sourceSequence;
    private uint _revision;
    private ulong _providerEpoch;
    private ulong _providerSequence;
    private bool _opened;
    private bool _terminal;

    internal StreamingTranscriptObservationNormalizer(
        ExpectedAuthorityVectorV1 authority, bool timestampsRequired = false)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _timestampsRequired = timestampsRequired;
    }

    internal IReadOnlyList<TranscriptObservation> Normalize(StreamingSpeechToTextObservation provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_terminal) throw new InvalidDataException("Transcript source is terminal.");
        ValidateProviderOrder(provider);
        var result = new List<TranscriptObservation>(4);
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
                Add(result, provider, (o, q, b, p) => new TranscriptObservation.ProviderItemCompleted(
                    o, _source, _hypothesis, q, b, _authority, p));
                break;
            case StreamingSpeechToTextObservationKind.Error:
                Add(result, provider, (o, q, b, p) => new TranscriptObservation.SourceHealthChanged(
                    o, _source, _hypothesis, q, b, _authority, p, TranscriptSourceHealth.Unavailable));
                Add(result, provider, (o, q, b, p) => new TranscriptObservation.DiscontinuityObserved(
                    o, _source, _hypothesis, q, b, _authority, p));
                _terminal = true;
                break;
            case StreamingSpeechToTextObservationKind.SessionClosed:
                Add(result, provider, (o, q, b, p) => new TranscriptObservation.SourceCompleted(
                    o, _source, _hypothesis, q, b, _authority, p));
                _terminal = true;
                break;
            case StreamingSpeechToTextObservationKind.Unknown:
                Add(result, provider, (o, q, b, p) => new TranscriptObservation.OpaqueHypothesis(
                    o, _source, _hypothesis, q, b, _authority, p, new BoundedAscii("unknown-provider-observation")));
                break;
            default:
                throw new InvalidDataException("Provider emitted an unknown transcript observation kind.");
        }
        if (!string.IsNullOrWhiteSpace(provider.LanguageCode))
            Add(result, provider, (o, q, b, p) => new TranscriptObservation.LanguageObserved(
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

    private void EnsureOpened(List<TranscriptObservation> result, StreamingSpeechToTextObservation provider)
    {
        if (_opened) return;
        var digest = Provenance(provider, "open");
        result.Add(new TranscriptObservation.HypothesisOpened(
            ProviderObservationId.Create(), _source, _hypothesis, checked(++_sourceSequence), _authority, digest));
        _opened = true;
        _revision = 1;
    }

    private void AddText(List<TranscriptObservation> result, StreamingSpeechToTextObservation provider, bool final)
    {
        if (string.IsNullOrWhiteSpace(provider.Text))
        {
            Add(result, provider, (o, q, b, p) => new TranscriptObservation.NoSpeechObserved(
                o, _source, _hypothesis, q, b, _authority, p));
            return;
        }
        Add(result, provider, (o, q, b, p) => new TranscriptObservation.TextReplaced(
            o, _source, _hypothesis, q, b, _authority, p, provider.Text));
        if (!final) return;
        Add(result, provider, (o, q, b, p) => new TranscriptObservation.FinalityAsserted(
            o, _source, _hypothesis, q, b, _authority, p,
            new TranscriptFinality(TranscriptMutability.ImmutableUnderSourceGuarantee, null,
                TranscriptFinalizedScope.ProviderItem, TranscriptBoundaryEvidence.ProviderEndpoint,
                TranscriptContinuity.Complete, TranscriptObservability.Observed,
                TranscriptCorrectionState.CorrectionWindowClosed,
                TranscriptAssemblyClosure.ClosedForAssembly)));
    }

    private void Add(List<TranscriptObservation> result, StreamingSpeechToTextObservation provider,
        Func<ProviderObservationId, ulong, TranscriptRevision, Hash256, TranscriptObservation> create)
    {
        var sequence = checked(++_sourceSequence);
        var revision = new TranscriptRevision(_revision++);
        result.Add(create(ProviderObservationId.Create(), sequence, revision, Provenance(provider, sequence.ToString())));
    }

    private static Hash256 Provenance(StreamingSpeechToTextObservation provider, string discriminator) =>
        Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('|', provider.ProviderSessionEpoch,
            provider.Sequence, provider.Kind, provider.ProviderEventType, provider.EvidenceSha256, discriminator)));

    private static string BoundAscii(string value, int maximum) => new string(value
        .Where(static c => c is >= ' ' and <= '~').Take(maximum).ToArray()) is { Length: > 0 } bounded
            ? bounded : "und";
}
