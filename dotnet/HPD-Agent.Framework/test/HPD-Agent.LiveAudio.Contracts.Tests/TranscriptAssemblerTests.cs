using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TranscriptAssemblerTests
{
    [Fact]
    public void Open_append_correct_retract_and_finality_are_revision_bound()
    {
        var f = new Fixture();
        var state = Applied(TranscriptAssembler.Apply(TranscriptAssembler.Create(), f.Open(1), f.Bounds)).State;
        state = Applied(TranscriptAssembler.Apply(state, f.Append(2, 1, "hello"), f.Bounds)).State;
        state = Applied(TranscriptAssembler.Apply(state, f.Correct(3, 2, new(1, 3), "i"), f.Bounds)).State;
        var retracted = Applied(TranscriptAssembler.Apply(state, f.Retract(4, 3, new(1, 1)), f.Bounds));

        Assert.Equal("ho", retracted.Track.Text);
        Assert.Equal(4u, retracted.Track.Revision.Value);
        Assert.Equal(TranscriptCorrectionState.Retracted, retracted.Track.Finality.CorrectionState);
        Assert.Equal(4, retracted.Track.Provenance.Count);
    }

    [Fact]
    public void Duplicate_is_idempotent_and_same_sequence_different_digest_fences_source()
    {
        var f = new Fixture();
        var opened = Applied(TranscriptAssembler.Apply(TranscriptAssembler.Create(), f.Open(1), f.Bounds));
        Assert.IsType<TranscriptAssemblerResult.Duplicate>(
            TranscriptAssembler.Apply(opened.State, f.Open(1), f.Bounds));
        var contradiction = f.Open(1, Hash256.Compute([9]));
        Assert.IsType<TranscriptAssemblerResult.AuthorityContinuityLost>(
            TranscriptAssembler.Apply(opened.State, contradiction, f.Bounds));
        Assert.IsType<TranscriptAssemblerResult.TerminalFault>(
            TranscriptAssembler.Apply(opened.State, contradiction, f.Bounds, false));
    }

    [Fact]
    public void Revision_source_and_utf8_range_conflicts_fail_closed_without_mutation()
    {
        var f = new Fixture();
        var opened = Applied(TranscriptAssembler.Apply(TranscriptAssembler.Create(), f.Open(1), f.Bounds));
        var appended = Applied(TranscriptAssembler.Apply(opened.State, f.Append(2, 1, "éx"), f.Bounds));
        var stale = TranscriptAssembler.Apply(appended.State, f.Append(3, 1, "bad"), f.Bounds);
        var splitUtf8 = TranscriptAssembler.Apply(appended.State, f.Correct(3, 2, new(1, 1), "z"), f.Bounds);
        var hole = TranscriptAssembler.Apply(appended.State, f.Append(4, 2, "bad"), f.Bounds);

        Assert.Equal("revision-conflict", Assert.IsType<TranscriptAssemblerResult.Rejected>(stale).SafeCode.ToString());
        Assert.Equal("range-invalid", Assert.IsType<TranscriptAssemblerResult.Rejected>(splitUtf8).SafeCode.ToString());
        Assert.Equal("source-sequence-gap", Assert.IsType<TranscriptAssemblerResult.AuthorityContinuityLost>(hole).SafeCode.ToString());
        Assert.Equal("éx", appended.Track.Text);

        var staleSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var staleAuthority = ExpectedAuthorityVectorV1.Create(staleSession, []);
        var staleAuthorityObservation = new TranscriptObservation.TextAppended(
            ProviderObservationId.Create(), f.Source, f.Hypothesis, 3, new(2), staleAuthority,
            Hash256.Compute([7]), "bad");
        Assert.Equal("authority-stale", Assert.IsType<TranscriptAssemblerResult.Rejected>(
            TranscriptAssembler.Apply(appended.State, staleAuthorityObservation, f.Bounds)).SafeCode.ToString());
    }

    [Fact]
    public void Bounds_reject_ordinary_growth_but_authoritative_loss_requires_barrier()
    {
        var f = new Fixture(new TranscriptAssemblerBounds(1, 1, 3, 2, 1));
        var opened = Applied(TranscriptAssembler.Apply(TranscriptAssembler.Create(), f.Open(1), f.Bounds));
        var tooLarge = TranscriptAssembler.Apply(opened.State, f.Append(2, 1, "four"), f.Bounds);
        Assert.Equal("text-capacity-refused", Assert.IsType<TranscriptAssemblerResult.Rejected>(tooLarge).SafeCode.ToString());

        var appended = Applied(TranscriptAssembler.Apply(opened.State, f.Append(2, 1, "abc"), f.Bounds));
        var finalized = TranscriptAssembler.Apply(appended.State, f.Final(3, 2), f.Bounds);
        Assert.IsType<TranscriptAssemblerResult.AuthorityContinuityLost>(finalized);
    }

    [Fact]
    public void Snapshots_own_text_provenance_and_gap_collections()
    {
        var f = new Fixture();
        var opened = Applied(TranscriptAssembler.Apply(TranscriptAssembler.Create(), f.Open(1), f.Bounds));
        var appended = Applied(TranscriptAssembler.Apply(opened.State, f.Append(2, 1, "abc"), f.Bounds));
        var gap = Applied(TranscriptAssembler.Apply(appended.State, f.Gap(3, 2, new(1, 1)), f.Bounds));

        Assert.Equal("abc", gap.Track.Text);
        Assert.Single(gap.Track.Gaps);
        Assert.Equal(3, gap.Track.Provenance.Count);
        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<Hash256>>(gap.Track.Provenance);
    }

    private static TranscriptAssemblerResult.Applied Applied(TranscriptAssemblerResult result) =>
        Assert.IsType<TranscriptAssemblerResult.Applied>(result);

    private sealed class Fixture
    {
        internal Fixture(TranscriptAssemblerBounds? bounds = null)
        {
            Bounds = bounds ?? new TranscriptAssemblerBounds(2, 4, 64, 16, 4);
            Source = TranscriptSourceId.Create();
            Hypothesis = TranscriptHypothesisId.Create();
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
        }
        internal TranscriptAssemblerBounds Bounds { get; }
        internal TranscriptSourceId Source { get; }
        internal TranscriptHypothesisId Hypothesis { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }

        internal TranscriptObservation.HypothesisOpened Open(ulong sequence, Hash256? digest = null) =>
            new(ProviderObservationId.Create(), Source, Hypothesis, sequence, Authority, digest ?? Digest(sequence));
        internal TranscriptObservation.TextAppended Append(ulong sequence, uint revision, string text) =>
            new(ProviderObservationId.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), text);
        internal TranscriptObservation.RangeCorrected Correct(ulong sequence, uint revision, TranscriptTextRange range, string text) =>
            new(ProviderObservationId.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), range, text);
        internal TranscriptObservation.RangeRetracted Retract(ulong sequence, uint revision, TranscriptTextRange range) =>
            new(ProviderObservationId.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), range);
        internal TranscriptObservation.FinalityAsserted Final(ulong sequence, uint revision) =>
            new(ProviderObservationId.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence),
                new TranscriptFinality(TranscriptMutability.ImmutableUnderSourceGuarantee, null,
                    TranscriptFinalizedScope.ProviderItem, TranscriptBoundaryEvidence.ProviderEndpoint,
                    TranscriptContinuity.Complete, TranscriptObservability.Observed,
                    TranscriptCorrectionState.CorrectionWindowClosed, TranscriptAssemblyClosure.ClosedForAssembly));
        internal TranscriptObservation.GapObserved Gap(ulong sequence, uint revision, TranscriptTextRange range) =>
            new(ProviderObservationId.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), range);
        private static Hash256 Digest(ulong sequence) => Hash256.Compute(BitConverter.GetBytes(sequence));
    }
}
