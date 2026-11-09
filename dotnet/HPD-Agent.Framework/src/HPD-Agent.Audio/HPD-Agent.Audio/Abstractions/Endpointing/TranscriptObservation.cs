using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Endpointing;

internal readonly record struct TranscriptSourceId
{
    private TranscriptSourceId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static TranscriptSourceId Create() => new(StableId128.CreateRandom());
    internal static TranscriptSourceId FromValue(StableId128 value) =>
        value.Equals(default) ? throw new ArgumentException("A source identity is required.", nameof(value)) : new(value);
}

internal readonly record struct TranscriptHypothesisId
{
    private TranscriptHypothesisId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static TranscriptHypothesisId Create() => new(StableId128.CreateRandom());
    internal static TranscriptHypothesisId FromValue(StableId128 value) =>
        value.Equals(default) ? throw new ArgumentException("A hypothesis identity is required.", nameof(value)) : new(value);
}

internal readonly record struct ProviderObservationId
{
    private ProviderObservationId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static ProviderObservationId Create() => new(StableId128.CreateRandom());
    internal static ProviderObservationId FromValue(StableId128 value) =>
        value.Equals(default) ? throw new ArgumentException("An observation identity is required.", nameof(value)) : new(value);
}

internal readonly record struct TranscriptRevision
{
    internal TranscriptRevision(uint value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
    internal uint Value { get; }
}

internal enum TranscriptSourceHealth : ushort
{
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3,
    Quarantined = 4,
}

internal abstract record TranscriptObservation
{
    private protected TranscriptObservation(
        ProviderObservationId observationId,
        TranscriptSourceId sourceId,
        TranscriptHypothesisId hypothesisId,
        ulong sourceSequence,
        TranscriptRevision? expectedBaseRevision,
        ExpectedAuthorityVectorV1 authority,
        Hash256 provenanceDigest)
    {
        Span<byte> digest = stackalloc byte[32];
        if (!observationId.IsValid || !sourceId.IsValid || !hypothesisId.IsValid || sourceSequence == 0 ||
            !provenanceDigest.TryWriteBytes(digest))
            throw new ArgumentException("Transcript observation identity, sequence, and provenance are required.");
        ArgumentNullException.ThrowIfNull(authority);
        ObservationId = observationId;
        SourceId = sourceId;
        HypothesisId = hypothesisId;
        SourceSequence = sourceSequence;
        ExpectedBaseRevision = expectedBaseRevision;
        Authority = authority;
        ProvenanceDigest = provenanceDigest;
    }

    internal ProviderObservationId ObservationId { get; }
    internal TranscriptSourceId SourceId { get; }
    internal TranscriptHypothesisId HypothesisId { get; }
    internal ulong SourceSequence { get; }
    internal TranscriptRevision? ExpectedBaseRevision { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal Hash256 ProvenanceDigest { get; }

    internal sealed record HypothesisOpened : TranscriptObservation
    {
        internal HypothesisOpened(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h,
            ulong q, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, null, a, p) { }
    }

    internal abstract record TextMutation : TranscriptObservation
    {
        private readonly byte[] _utf8;
        private protected TextMutation(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h,
            ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, string text)
            : base(o, s, h, q, b, a, p)
        {
            ArgumentNullException.ThrowIfNull(text);
            var normalized = text.Normalize(NormalizationForm.FormC);
            _utf8 = Encoding.UTF8.GetBytes(normalized);
            if (_utf8.Length is 0 or > 65_536) throw new ArgumentOutOfRangeException(nameof(text));
            Text = normalized;
        }
        internal string Text { get; }
        internal ReadOnlySpan<byte> Utf8Bytes => _utf8;
    }

    internal sealed record TextAppended : TextMutation
    { internal TextAppended(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, string t) : base(o, s, h, q, b, a, p, t) { } }
    internal sealed record TextReplaced : TextMutation
    { internal TextReplaced(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, string t) : base(o, s, h, q, b, a, p, t) { } }

    internal abstract record RangeMutation : TranscriptObservation
    {
        private protected RangeMutation(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h,
            ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRange range)
            : base(o, s, h, q, b, a, p) => Range = range;
        internal TranscriptTextRange Range { get; }
    }

    internal sealed record RangeCorrected : RangeMutation
    {
        private readonly byte[] _replacementUtf8;
        internal RangeCorrected(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h,
            ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRange r, string replacement)
            : base(o, s, h, q, b, a, p, r)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            Replacement = replacement.Normalize(NormalizationForm.FormC);
            _replacementUtf8 = Encoding.UTF8.GetBytes(Replacement);
            if (_replacementUtf8.Length > 65_536) throw new ArgumentOutOfRangeException(nameof(replacement));
        }
        internal string Replacement { get; }
        internal ReadOnlySpan<byte> ReplacementUtf8Bytes => _replacementUtf8;
    }
    internal sealed record RangeRetracted : RangeMutation
    { internal RangeRetracted(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRange r) : base(o, s, h, q, b, a, p, r) { } }
    internal sealed record StablePrefixAdvanced : RangeMutation
    { internal StablePrefixAdvanced(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRange r) : base(o, s, h, q, b, a, p, r) { } }

    internal sealed record FinalityAsserted : TranscriptObservation
    { internal FinalityAsserted(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptFinality f) : base(o, s, h, q, b, a, p) => Finality = f ?? throw new ArgumentNullException(nameof(f)); internal TranscriptFinality Finality { get; } }
    internal sealed record ProviderItemCompleted : TranscriptObservation
    { internal ProviderItemCompleted(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record BoundaryObserved : TranscriptObservation
    { internal BoundaryObserved(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptBoundaryEvidence e) : base(o, s, h, q, b, a, p) { if (e == TranscriptBoundaryEvidence.None) throw new ArgumentException("Boundary evidence is required.", nameof(e)); Evidence = e; } internal TranscriptBoundaryEvidence Evidence { get; } }
    internal sealed record TurnResumed : TranscriptObservation
    { internal TurnResumed(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record NoSpeechObserved : TranscriptObservation
    { internal NoSpeechObserved(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record GapObserved : RangeMutation
    { internal GapObserved(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRange r) : base(o, s, h, q, b, a, p, r) { } }
    internal sealed record DiscontinuityObserved : TranscriptObservation
    { internal DiscontinuityObserved(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record LanguageObserved : TranscriptObservation
    { internal LanguageObserved(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, BoundedAscii language) : base(o, s, h, q, b, a, p) { if (!language.IsValid) throw new ArgumentException("Language is required.", nameof(language)); Language = language; } internal BoundedAscii Language { get; } }
    internal sealed record SpeakerObserved : TranscriptObservation
    { internal SpeakerObserved(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, ParticipantId speaker) : base(o, s, h, q, b, a, p) { if (!speaker.IsValid) throw new ArgumentException("Speaker is required.", nameof(speaker)); Speaker = speaker; } internal ParticipantId Speaker { get; } }
    internal sealed record SourceHealthChanged : TranscriptObservation
    { internal SourceHealthChanged(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptSourceHealth health) : base(o, s, h, q, b, a, p) { if (!Enum.IsDefined(health)) throw new ArgumentException("Source health is outside the closed registry.", nameof(health)); Health = health; } internal TranscriptSourceHealth Health { get; } }
    internal sealed record SourceCompleted : TranscriptObservation
    { internal SourceCompleted(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record OpaqueHypothesis : TranscriptObservation
    { internal OpaqueHypothesis(ProviderObservationId o, TranscriptSourceId s, TranscriptHypothesisId h, ulong q, TranscriptRevision? b, ExpectedAuthorityVectorV1 a, Hash256 p, BoundedAscii reason) : base(o, s, h, q, b, a, p) { if (!reason.IsValid) throw new ArgumentException("Opaque reason is required.", nameof(reason)); Reason = reason; } internal BoundedAscii Reason { get; } }
}
