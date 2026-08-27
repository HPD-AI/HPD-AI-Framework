using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Endpointing;

internal readonly record struct TranscriptSourceIdV1
{
    private TranscriptSourceIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static TranscriptSourceIdV1 Create() => new(StableId128.CreateRandom());
    internal static TranscriptSourceIdV1 FromValue(StableId128 value) =>
        value.Equals(default) ? throw new ArgumentException("A source identity is required.", nameof(value)) : new(value);
}

internal readonly record struct TranscriptHypothesisIdV1
{
    private TranscriptHypothesisIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static TranscriptHypothesisIdV1 Create() => new(StableId128.CreateRandom());
    internal static TranscriptHypothesisIdV1 FromValue(StableId128 value) =>
        value.Equals(default) ? throw new ArgumentException("A hypothesis identity is required.", nameof(value)) : new(value);
}

internal readonly record struct ProviderObservationIdV1
{
    private ProviderObservationIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static ProviderObservationIdV1 Create() => new(StableId128.CreateRandom());
    internal static ProviderObservationIdV1 FromValue(StableId128 value) =>
        value.Equals(default) ? throw new ArgumentException("An observation identity is required.", nameof(value)) : new(value);
}

internal readonly record struct TranscriptRevisionV1
{
    internal TranscriptRevisionV1(uint value)
    {
        if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }
    internal uint Value { get; }
}

internal enum TranscriptSourceHealthV1 : ushort
{
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3,
    Quarantined = 4,
}

internal abstract record TranscriptObservationV1
{
    private protected TranscriptObservationV1(
        ProviderObservationIdV1 observationId,
        TranscriptSourceIdV1 sourceId,
        TranscriptHypothesisIdV1 hypothesisId,
        ulong sourceSequence,
        TranscriptRevisionV1? expectedBaseRevision,
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

    internal ProviderObservationIdV1 ObservationId { get; }
    internal TranscriptSourceIdV1 SourceId { get; }
    internal TranscriptHypothesisIdV1 HypothesisId { get; }
    internal ulong SourceSequence { get; }
    internal TranscriptRevisionV1? ExpectedBaseRevision { get; }
    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal Hash256 ProvenanceDigest { get; }

    internal sealed record HypothesisOpened : TranscriptObservationV1
    {
        internal HypothesisOpened(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h,
            ulong q, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, null, a, p) { }
    }

    internal abstract record TextMutation : TranscriptObservationV1
    {
        private readonly byte[] _utf8;
        private protected TextMutation(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h,
            ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, string text)
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
    { internal TextAppended(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, string t) : base(o, s, h, q, b, a, p, t) { } }
    internal sealed record TextReplaced : TextMutation
    { internal TextReplaced(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, string t) : base(o, s, h, q, b, a, p, t) { } }

    internal abstract record RangeMutation : TranscriptObservationV1
    {
        private protected RangeMutation(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h,
            ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRangeV1 range)
            : base(o, s, h, q, b, a, p) => Range = range;
        internal TranscriptTextRangeV1 Range { get; }
    }

    internal sealed record RangeCorrected : RangeMutation
    {
        private readonly byte[] _replacementUtf8;
        internal RangeCorrected(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h,
            ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRangeV1 r, string replacement)
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
    { internal RangeRetracted(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRangeV1 r) : base(o, s, h, q, b, a, p, r) { } }
    internal sealed record StablePrefixAdvanced : RangeMutation
    { internal StablePrefixAdvanced(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRangeV1 r) : base(o, s, h, q, b, a, p, r) { } }

    internal sealed record FinalityAsserted : TranscriptObservationV1
    { internal FinalityAsserted(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptFinalityV1 f) : base(o, s, h, q, b, a, p) => Finality = f ?? throw new ArgumentNullException(nameof(f)); internal TranscriptFinalityV1 Finality { get; } }
    internal sealed record ProviderItemCompleted : TranscriptObservationV1
    { internal ProviderItemCompleted(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record BoundaryObserved : TranscriptObservationV1
    { internal BoundaryObserved(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptBoundaryEvidenceV1 e) : base(o, s, h, q, b, a, p) { if (e == TranscriptBoundaryEvidenceV1.None) throw new ArgumentException("Boundary evidence is required.", nameof(e)); Evidence = e; } internal TranscriptBoundaryEvidenceV1 Evidence { get; } }
    internal sealed record TurnResumed : TranscriptObservationV1
    { internal TurnResumed(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record NoSpeechObserved : TranscriptObservationV1
    { internal NoSpeechObserved(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record GapObserved : RangeMutation
    { internal GapObserved(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptTextRangeV1 r) : base(o, s, h, q, b, a, p, r) { } }
    internal sealed record DiscontinuityObserved : TranscriptObservationV1
    { internal DiscontinuityObserved(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record LanguageObserved : TranscriptObservationV1
    { internal LanguageObserved(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, BoundedAscii language) : base(o, s, h, q, b, a, p) { if (!language.IsValid) throw new ArgumentException("Language is required.", nameof(language)); Language = language; } internal BoundedAscii Language { get; } }
    internal sealed record SpeakerObserved : TranscriptObservationV1
    { internal SpeakerObserved(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, ParticipantId speaker) : base(o, s, h, q, b, a, p) { if (!speaker.IsValid) throw new ArgumentException("Speaker is required.", nameof(speaker)); Speaker = speaker; } internal ParticipantId Speaker { get; } }
    internal sealed record SourceHealthChanged : TranscriptObservationV1
    { internal SourceHealthChanged(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p, TranscriptSourceHealthV1 health) : base(o, s, h, q, b, a, p) { if (!Enum.IsDefined(health)) throw new ArgumentException("Source health is outside the closed registry.", nameof(health)); Health = health; } internal TranscriptSourceHealthV1 Health { get; } }
    internal sealed record SourceCompleted : TranscriptObservationV1
    { internal SourceCompleted(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1 b, ExpectedAuthorityVectorV1 a, Hash256 p) : base(o, s, h, q, b, a, p) { } }
    internal sealed record OpaqueHypothesis : TranscriptObservationV1
    { internal OpaqueHypothesis(ProviderObservationIdV1 o, TranscriptSourceIdV1 s, TranscriptHypothesisIdV1 h, ulong q, TranscriptRevisionV1? b, ExpectedAuthorityVectorV1 a, Hash256 p, BoundedAscii reason) : base(o, s, h, q, b, a, p) { if (!reason.IsValid) throw new ArgumentException("Opaque reason is required.", nameof(reason)); Reason = reason; } internal BoundedAscii Reason { get; } }
}
