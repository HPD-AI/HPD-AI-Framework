using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Authority;
using System.Reflection;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TranscriptObservationV1Tests
{
    [Fact]
    public void Closed_union_contains_every_normative_observation_kind()
    {
        var names = typeof(TranscriptObservationV1).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static type => type.IsSealed && typeof(TranscriptObservationV1).IsAssignableFrom(type))
            .Select(static type => type.Name).Order().ToArray();
        Assert.Equal(new[]
        {
            "BoundaryObserved", "DiscontinuityObserved", "FinalityAsserted", "GapObserved",
            "HypothesisOpened", "LanguageObserved", "NoSpeechObserved", "OpaqueHypothesis",
            "ProviderItemCompleted", "RangeCorrected", "RangeRetracted", "SourceCompleted",
            "SourceHealthChanged", "SpeakerObserved", "StablePrefixAdvanced", "TextAppended",
            "TextReplaced", "TurnResumed",
        }.Order(), names);
    }

    [Fact]
    public void Text_is_normalized_owned_and_bound_to_expected_revision()
    {
        var fixture = Create();
        var value = new TranscriptObservationV1.TextAppended(
            fixture.Observation, fixture.Source, fixture.Hypothesis, 2, new TranscriptRevisionV1(1),
            fixture.Authority, fixture.Digest, "e\u0301");

        Assert.Equal("é", value.Text);
        Assert.Equal(new byte[] { 0xc3, 0xa9 }, value.Utf8Bytes.ToArray());
        Assert.Equal(1u, value.ExpectedBaseRevision!.Value.Value);
    }

    [Fact]
    public void Observation_identity_sequence_provenance_and_cross_axis_values_fail_closed()
    {
        var fixture = Create();
        Assert.Throws<ArgumentException>(() => new TranscriptObservationV1.HypothesisOpened(
            default, fixture.Source, fixture.Hypothesis, 1, fixture.Authority, fixture.Digest));
        Assert.Throws<ArgumentException>(() => new TranscriptObservationV1.HypothesisOpened(
            fixture.Observation, fixture.Source, fixture.Hypothesis, 0, fixture.Authority, fixture.Digest));
        Assert.Throws<ArgumentException>(() => new TranscriptObservationV1.BoundaryObserved(
            fixture.Observation, fixture.Source, fixture.Hypothesis, 2, new TranscriptRevisionV1(1),
            fixture.Authority, fixture.Digest, TranscriptBoundaryEvidenceV1.None));
        Assert.Throws<ArgumentException>(() => new TranscriptObservationV1.SourceHealthChanged(
            fixture.Observation, fixture.Source, fixture.Hypothesis, 2, new TranscriptRevisionV1(1),
            fixture.Authority, fixture.Digest, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptRevisionV1(0));
    }

    [Fact]
    public void Equal_text_does_not_collapse_distinct_observations_or_sources()
    {
        var fixture = Create();
        var first = new TranscriptObservationV1.TextAppended(
            fixture.Observation, fixture.Source, fixture.Hypothesis, 2, new TranscriptRevisionV1(1),
            fixture.Authority, fixture.Digest, "same");
        var second = new TranscriptObservationV1.TextAppended(
            ProviderObservationIdV1.Create(), TranscriptSourceIdV1.Create(), fixture.Hypothesis, 2,
            new TranscriptRevisionV1(1), fixture.Authority, fixture.Digest, "same");

        Assert.NotEqual(first, second);
    }

    private static (ProviderObservationIdV1 Observation, TranscriptSourceIdV1 Source,
        TranscriptHypothesisIdV1 Hypothesis, ExpectedAuthorityVectorV1 Authority, Hash256 Digest) Create()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        return (ProviderObservationIdV1.Create(), TranscriptSourceIdV1.Create(), TranscriptHypothesisIdV1.Create(),
            ExpectedAuthorityVectorV1.Create(session, []), Hash256.Compute([1, 2, 3]));
    }
}
