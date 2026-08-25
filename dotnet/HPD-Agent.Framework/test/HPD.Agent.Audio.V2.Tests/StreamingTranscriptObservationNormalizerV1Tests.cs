using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class StreamingTranscriptObservationNormalizerV1Tests
{
    [Theory]
    [InlineData((int)StreamingSpeechToTextObservationKind.PartialTranscript)]
    [InlineData((int)StreamingSpeechToTextObservationKind.FinalTranscript)]
    [InlineData((int)StreamingSpeechToTextObservationKind.FinalTranscriptWithTimestamps)]
    [InlineData((int)StreamingSpeechToTextObservationKind.CommittedTranscript)]
    [InlineData((int)StreamingSpeechToTextObservationKind.CommittedTranscriptWithTimestamps)]
    [InlineData((int)StreamingSpeechToTextObservationKind.Error)]
    [InlineData((int)StreamingSpeechToTextObservationKind.Unknown)]
    [InlineData((int)StreamingSpeechToTextObservationKind.SessionClosed)]
    public void EveryQualifiedProviderObservationEntersClosedTranscriptUnion(int kindValue)
    {
        var kind = (StreamingSpeechToTextObservationKind)kindValue;
        var normalized = new StreamingTranscriptObservationNormalizerV1(Authority()).Normalize(new()
        {
            ProviderSessionEpoch = 1,
            Sequence = 1,
            Kind = kind,
            Text = kind == StreamingSpeechToTextObservationKind.Error ? null : "hello",
            ProviderEventType = kind.ToString(),
            LanguageCode = "en"
        });

        Assert.NotEmpty(normalized);
        Assert.All(normalized, static item => Assert.IsAssignableFrom<TranscriptObservationV1>(item));
        Assert.Equal(Enumerable.Range(1, normalized.Count).Select(static value => (ulong)value),
            normalized.Select(static item => item.SourceSequence));
        Assert.All(normalized, static item => Assert.True(item.ProvenanceDigest != default));
    }

    [Fact]
    public void GapAndEpochTransitionFailClosed()
    {
        var gap = new StreamingTranscriptObservationNormalizerV1(Authority());
        _ = gap.Normalize(Observation(1, 1));
        Assert.Throws<InvalidDataException>(() => gap.Normalize(Observation(1, 3)));

        var epoch = new StreamingTranscriptObservationNormalizerV1(Authority());
        _ = epoch.Normalize(Observation(1, 1));
        Assert.Throws<InvalidDataException>(() => epoch.Normalize(Observation(2, 2)));
    }

    private static StreamingSpeechToTextObservation Observation(ulong epoch, ulong sequence) => new()
    {
        ProviderSessionEpoch = epoch, Sequence = sequence,
        Kind = StreamingSpeechToTextObservationKind.PartialTranscript, Text = "hello"
    };

    private static ExpectedAuthorityVectorV1 Authority() => ExpectedAuthorityVectorV1.Create(
        new(RuntimeGenerationId.Create(), LiveSessionId.Create()),
        [new AuthorityAxisValueV1.Graph(GraphGenerationId.Create()),
         new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create()),
         new AuthorityAxisValueV1.Provider(ProviderGenerationId.Create())]);
}
