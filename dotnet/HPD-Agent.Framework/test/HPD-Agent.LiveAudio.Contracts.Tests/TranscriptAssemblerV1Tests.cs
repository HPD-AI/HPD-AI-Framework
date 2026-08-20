using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TranscriptAssemblerV1Tests
{
    [Fact]
    public void Open_append_correct_retract_and_finality_are_revision_bound()
    {
        var f = new Fixture();
        var state = Applied(TranscriptAssemblerV1.Apply(TranscriptAssemblerV1.Create(), f.Open(1), f.Bounds)).State;
        state = Applied(TranscriptAssemblerV1.Apply(state, f.Append(2, 1, "hello"), f.Bounds)).State;
        state = Applied(TranscriptAssemblerV1.Apply(state, f.Correct(3, 2, new(1, 3), "i"), f.Bounds)).State;
        var retracted = Applied(TranscriptAssemblerV1.Apply(state, f.Retract(4, 3, new(1, 1)), f.Bounds));

        Assert.Equal("ho", retracted.Track.Text);
        Assert.Equal(4u, retracted.Track.Revision.Value);
        Assert.Equal(TranscriptCorrectionStateV1.Retracted, retracted.Track.Finality.CorrectionState);
        Assert.Equal(4, retracted.Track.Provenance.Count);
    }

    [Fact]
    public void Duplicate_is_idempotent_and_same_sequence_different_digest_fences_source()
    {
        var f = new Fixture();
        var opened = Applied(TranscriptAssemblerV1.Apply(TranscriptAssemblerV1.Create(), f.Open(1), f.Bounds));
        Assert.IsType<TranscriptAssemblerResultV1.Duplicate>(
            TranscriptAssemblerV1.Apply(opened.State, f.Open(1), f.Bounds));
        var contradiction = f.Open(1, Hash256.Compute([9]));
        Assert.IsType<TranscriptAssemblerResultV1.AuthorityContinuityLost>(
            TranscriptAssemblerV1.Apply(opened.State, contradiction, f.Bounds));
        Assert.IsType<TranscriptAssemblerResultV1.TerminalFault>(
            TranscriptAssemblerV1.Apply(opened.State, contradiction, f.Bounds, false));
    }

    [Fact]
    public void Revision_source_and_utf8_range_conflicts_fail_closed_without_mutation()
    {
        var f = new Fixture();
        var opened = Applied(TranscriptAssemblerV1.Apply(TranscriptAssemblerV1.Create(), f.Open(1), f.Bounds));
        var appended = Applied(TranscriptAssemblerV1.Apply(opened.State, f.Append(2, 1, "éx"), f.Bounds));
        var stale = TranscriptAssemblerV1.Apply(appended.State, f.Append(3, 1, "bad"), f.Bounds);
        var splitUtf8 = TranscriptAssemblerV1.Apply(appended.State, f.Correct(3, 2, new(1, 1), "z"), f.Bounds);
        var hole = TranscriptAssemblerV1.Apply(appended.State, f.Append(4, 2, "bad"), f.Bounds);

        Assert.Equal("revision-conflict", Assert.IsType<TranscriptAssemblerResultV1.Rejected>(stale).SafeCode.ToString());
        Assert.Equal("range-invalid", Assert.IsType<TranscriptAssemblerResultV1.Rejected>(splitUtf8).SafeCode.ToString());
        Assert.Equal("source-sequence-gap", Assert.IsType<TranscriptAssemblerResultV1.AuthorityContinuityLost>(hole).SafeCode.ToString());
        Assert.Equal("éx", appended.Track.Text);

        var staleSession = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
        var staleAuthority = ExpectedAuthorityVectorV1.Create(staleSession, []);
        var staleAuthorityObservation = new TranscriptObservationV1.TextAppended(
            ProviderObservationIdV1.Create(), f.Source, f.Hypothesis, 3, new(2), staleAuthority,
            Hash256.Compute([7]), "bad");
        Assert.Equal("authority-stale", Assert.IsType<TranscriptAssemblerResultV1.Rejected>(
            TranscriptAssemblerV1.Apply(appended.State, staleAuthorityObservation, f.Bounds)).SafeCode.ToString());
    }

    [Fact]
    public void Bounds_reject_ordinary_growth_but_authoritative_loss_requires_barrier()
    {
        var f = new Fixture(new TranscriptAssemblerBoundsV1(1, 1, 3, 2, 1));
        var opened = Applied(TranscriptAssemblerV1.Apply(TranscriptAssemblerV1.Create(), f.Open(1), f.Bounds));
        var tooLarge = TranscriptAssemblerV1.Apply(opened.State, f.Append(2, 1, "four"), f.Bounds);
        Assert.Equal("text-capacity-refused", Assert.IsType<TranscriptAssemblerResultV1.Rejected>(tooLarge).SafeCode.ToString());

        var appended = Applied(TranscriptAssemblerV1.Apply(opened.State, f.Append(2, 1, "abc"), f.Bounds));
        var finalized = TranscriptAssemblerV1.Apply(appended.State, f.Final(3, 2), f.Bounds);
        Assert.IsType<TranscriptAssemblerResultV1.AuthorityContinuityLost>(finalized);
    }

    [Fact]
    public void Snapshots_own_text_provenance_and_gap_collections()
    {
        var f = new Fixture();
        var opened = Applied(TranscriptAssemblerV1.Apply(TranscriptAssemblerV1.Create(), f.Open(1), f.Bounds));
        var appended = Applied(TranscriptAssemblerV1.Apply(opened.State, f.Append(2, 1, "abc"), f.Bounds));
        var gap = Applied(TranscriptAssemblerV1.Apply(appended.State, f.Gap(3, 2, new(1, 1)), f.Bounds));

        Assert.Equal("abc", gap.Track.Text);
        Assert.Single(gap.Track.Gaps);
        Assert.Equal(3, gap.Track.Provenance.Count);
        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<Hash256>>(gap.Track.Provenance);
    }

    private static TranscriptAssemblerResultV1.Applied Applied(TranscriptAssemblerResultV1 result) =>
        Assert.IsType<TranscriptAssemblerResultV1.Applied>(result);

    private sealed class Fixture
    {
        internal Fixture(TranscriptAssemblerBoundsV1? bounds = null)
        {
            Bounds = bounds ?? new TranscriptAssemblerBoundsV1(2, 4, 64, 16, 4);
            Source = TranscriptSourceIdV1.Create();
            Hypothesis = TranscriptHypothesisIdV1.Create();
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            Authority = ExpectedAuthorityVectorV1.Create(session, []);
        }
        internal TranscriptAssemblerBoundsV1 Bounds { get; }
        internal TranscriptSourceIdV1 Source { get; }
        internal TranscriptHypothesisIdV1 Hypothesis { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }

        internal TranscriptObservationV1.HypothesisOpened Open(ulong sequence, Hash256? digest = null) =>
            new(ProviderObservationIdV1.Create(), Source, Hypothesis, sequence, Authority, digest ?? Digest(sequence));
        internal TranscriptObservationV1.TextAppended Append(ulong sequence, uint revision, string text) =>
            new(ProviderObservationIdV1.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), text);
        internal TranscriptObservationV1.RangeCorrected Correct(ulong sequence, uint revision, TranscriptTextRangeV1 range, string text) =>
            new(ProviderObservationIdV1.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), range, text);
        internal TranscriptObservationV1.RangeRetracted Retract(ulong sequence, uint revision, TranscriptTextRangeV1 range) =>
            new(ProviderObservationIdV1.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), range);
        internal TranscriptObservationV1.FinalityAsserted Final(ulong sequence, uint revision) =>
            new(ProviderObservationIdV1.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence),
                new TranscriptFinalityV1(TranscriptMutabilityV1.ImmutableUnderSourceGuarantee, null,
                    TranscriptFinalizedScopeV1.ProviderItem, TranscriptBoundaryEvidenceV1.ProviderEndpoint,
                    TranscriptContinuityV1.Complete, TranscriptObservabilityV1.Observed,
                    TranscriptCorrectionStateV1.CorrectionWindowClosed, TranscriptAssemblyClosureV1.ClosedForAssembly));
        internal TranscriptObservationV1.GapObserved Gap(ulong sequence, uint revision, TranscriptTextRangeV1 range) =>
            new(ProviderObservationIdV1.Create(), Source, Hypothesis, sequence, new(revision), Authority, Digest(sequence), range);
        private static Hash256 Digest(ulong sequence) => Hash256.Compute(BitConverter.GetBytes(sequence));
    }
}
