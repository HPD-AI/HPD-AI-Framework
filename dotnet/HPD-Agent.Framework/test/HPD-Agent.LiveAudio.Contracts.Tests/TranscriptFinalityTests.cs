using HPD.Agent.Audio.Endpointing;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TranscriptFinalityTests
{
    [Fact]
    public void Product_retains_all_independent_finality_axes()
    {
        var range = new TranscriptTextRange(3, 8);
        var value = new TranscriptFinality(
            TranscriptMutability.StablePrefix,
            range,
            TranscriptFinalizedScope.Range,
            TranscriptBoundaryEvidence.ActivityClose | TranscriptBoundaryEvidence.ProviderPause,
            TranscriptContinuity.Gapped,
            TranscriptObservability.DerivedByDeclaredAdapterRule,
            TranscriptCorrectionState.Correctable,
            TranscriptAssemblyClosure.Open);

        Assert.Equal(range, value.StablePrefix);
        Assert.Equal(11u, range.EndUtf8ByteExclusive);
        Assert.Equal(TranscriptFinalizedScope.Range, value.FinalizedScope);
        Assert.Equal(TranscriptContinuity.Gapped, value.Continuity);
        Assert.False(value.IsProviderFinal);
    }

    [Fact]
    public void Provider_finality_requires_scope_and_source_guaranteed_immutability()
    {
        var immutable = new TranscriptFinality(
            TranscriptMutability.ImmutableUnderSourceGuarantee,
            null,
            TranscriptFinalizedScope.ProviderItem,
            TranscriptBoundaryEvidence.ProviderEndpoint,
            TranscriptContinuity.Complete,
            TranscriptObservability.Observed,
            TranscriptCorrectionState.CorrectionWindowClosed,
            TranscriptAssemblyClosure.ClosedForAssembly);
        var scopedButMutable = new TranscriptFinality(
            TranscriptMutability.Volatile,
            null,
            TranscriptFinalizedScope.ProviderItem,
            TranscriptBoundaryEvidence.ProviderEndpoint,
            TranscriptContinuity.Complete,
            TranscriptObservability.Observed,
            TranscriptCorrectionState.Correctable,
            TranscriptAssemblyClosure.Open);

        Assert.True(immutable.IsProviderFinal);
        Assert.False(scopedButMutable.IsProviderFinal);
    }

    [Fact]
    public void Invalid_cross_axis_states_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new TranscriptFinality(
            TranscriptMutability.StablePrefix, null, TranscriptFinalizedScope.Range,
            TranscriptBoundaryEvidence.None, TranscriptContinuity.Complete,
            TranscriptObservability.Observed, TranscriptCorrectionState.Correctable,
            TranscriptAssemblyClosure.Open));
        Assert.Throws<ArgumentException>(() => new TranscriptFinality(
            TranscriptMutability.Volatile, new TranscriptTextRange(0, 1), TranscriptFinalizedScope.None,
            TranscriptBoundaryEvidence.None, TranscriptContinuity.Unknown,
            TranscriptObservability.Opaque, TranscriptCorrectionState.Correctable,
            TranscriptAssemblyClosure.Open));
        Assert.Throws<ArgumentException>(() => new TranscriptFinality(
            TranscriptMutability.Unknown, null, TranscriptFinalizedScope.None,
            (TranscriptBoundaryEvidence)(1 << 14), TranscriptContinuity.Unknown,
            TranscriptObservability.Opaque, TranscriptCorrectionState.Correctable,
            TranscriptAssemblyClosure.Open));
        Assert.Throws<ArgumentException>(() => new TranscriptFinality(
            TranscriptMutability.Unknown, null, TranscriptFinalizedScope.None,
            TranscriptBoundaryEvidence.None, TranscriptContinuity.Unknown,
            TranscriptObservability.Opaque, TranscriptCorrectionState.Retracted,
            TranscriptAssemblyClosure.Open));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptTextRange(0, 0));
        Assert.Throws<OverflowException>(() => new TranscriptTextRange(uint.MaxValue, 1));
    }

    [Fact]
    public void Closed_registries_have_no_zero_or_alias_values()
    {
        AssertClosed<TranscriptMutability>();
        AssertClosed<TranscriptFinalizedScope>();
        AssertClosed<TranscriptContinuity>();
        AssertClosed<TranscriptObservability>();
        AssertClosed<TranscriptCorrectionState>();
        AssertClosed<TranscriptAssemblyClosure>();
    }

    private static void AssertClosed<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>().Select(static value => Convert.ToUInt16(value)).ToArray();
        Assert.DoesNotContain((ushort)0, values);
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
