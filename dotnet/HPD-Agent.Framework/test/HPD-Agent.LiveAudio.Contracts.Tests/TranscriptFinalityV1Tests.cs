using HPD.Agent.Audio.Endpointing;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TranscriptFinalityV1Tests
{
    [Fact]
    public void Product_retains_all_independent_finality_axes()
    {
        var range = new TranscriptTextRangeV1(3, 8);
        var value = new TranscriptFinalityV1(
            TranscriptMutabilityV1.StablePrefix,
            range,
            TranscriptFinalizedScopeV1.Range,
            TranscriptBoundaryEvidenceV1.ActivityClose | TranscriptBoundaryEvidenceV1.ProviderPause,
            TranscriptContinuityV1.Gapped,
            TranscriptObservabilityV1.DerivedByDeclaredAdapterRule,
            TranscriptCorrectionStateV1.Correctable,
            TranscriptAssemblyClosureV1.Open);

        Assert.Equal(range, value.StablePrefix);
        Assert.Equal(11u, range.EndUtf8ByteExclusive);
        Assert.Equal(TranscriptFinalizedScopeV1.Range, value.FinalizedScope);
        Assert.Equal(TranscriptContinuityV1.Gapped, value.Continuity);
        Assert.False(value.IsProviderFinal);
    }

    [Fact]
    public void Provider_finality_requires_scope_and_source_guaranteed_immutability()
    {
        var immutable = new TranscriptFinalityV1(
            TranscriptMutabilityV1.ImmutableUnderSourceGuarantee,
            null,
            TranscriptFinalizedScopeV1.ProviderItem,
            TranscriptBoundaryEvidenceV1.ProviderEndpoint,
            TranscriptContinuityV1.Complete,
            TranscriptObservabilityV1.Observed,
            TranscriptCorrectionStateV1.CorrectionWindowClosed,
            TranscriptAssemblyClosureV1.ClosedForAssembly);
        var scopedButMutable = new TranscriptFinalityV1(
            TranscriptMutabilityV1.Volatile,
            null,
            TranscriptFinalizedScopeV1.ProviderItem,
            TranscriptBoundaryEvidenceV1.ProviderEndpoint,
            TranscriptContinuityV1.Complete,
            TranscriptObservabilityV1.Observed,
            TranscriptCorrectionStateV1.Correctable,
            TranscriptAssemblyClosureV1.Open);

        Assert.True(immutable.IsProviderFinal);
        Assert.False(scopedButMutable.IsProviderFinal);
    }

    [Fact]
    public void Invalid_cross_axis_states_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new TranscriptFinalityV1(
            TranscriptMutabilityV1.StablePrefix, null, TranscriptFinalizedScopeV1.Range,
            TranscriptBoundaryEvidenceV1.None, TranscriptContinuityV1.Complete,
            TranscriptObservabilityV1.Observed, TranscriptCorrectionStateV1.Correctable,
            TranscriptAssemblyClosureV1.Open));
        Assert.Throws<ArgumentException>(() => new TranscriptFinalityV1(
            TranscriptMutabilityV1.Volatile, new TranscriptTextRangeV1(0, 1), TranscriptFinalizedScopeV1.None,
            TranscriptBoundaryEvidenceV1.None, TranscriptContinuityV1.Unknown,
            TranscriptObservabilityV1.Opaque, TranscriptCorrectionStateV1.Correctable,
            TranscriptAssemblyClosureV1.Open));
        Assert.Throws<ArgumentException>(() => new TranscriptFinalityV1(
            TranscriptMutabilityV1.Unknown, null, TranscriptFinalizedScopeV1.None,
            (TranscriptBoundaryEvidenceV1)(1 << 14), TranscriptContinuityV1.Unknown,
            TranscriptObservabilityV1.Opaque, TranscriptCorrectionStateV1.Correctable,
            TranscriptAssemblyClosureV1.Open));
        Assert.Throws<ArgumentException>(() => new TranscriptFinalityV1(
            TranscriptMutabilityV1.Unknown, null, TranscriptFinalizedScopeV1.None,
            TranscriptBoundaryEvidenceV1.None, TranscriptContinuityV1.Unknown,
            TranscriptObservabilityV1.Opaque, TranscriptCorrectionStateV1.Retracted,
            TranscriptAssemblyClosureV1.Open));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptTextRangeV1(0, 0));
        Assert.Throws<OverflowException>(() => new TranscriptTextRangeV1(uint.MaxValue, 1));
    }

    [Fact]
    public void Closed_registries_have_no_zero_or_alias_values()
    {
        AssertClosed<TranscriptMutabilityV1>();
        AssertClosed<TranscriptFinalizedScopeV1>();
        AssertClosed<TranscriptContinuityV1>();
        AssertClosed<TranscriptObservabilityV1>();
        AssertClosed<TranscriptCorrectionStateV1>();
        AssertClosed<TranscriptAssemblyClosureV1>();
    }

    private static void AssertClosed<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>().Select(static value => Convert.ToUInt16(value)).ToArray();
        Assert.DoesNotContain((ushort)0, values);
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
