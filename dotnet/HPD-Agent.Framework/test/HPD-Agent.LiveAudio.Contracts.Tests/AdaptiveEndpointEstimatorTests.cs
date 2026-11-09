using HPD.Agent.Audio.Endpointing;
using HPD.Agent.Audio.Runtime.Endpointing;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class AdaptiveEndpointEstimatorTests
{
    [Fact]
    public void Observed_and_censored_outcomes_remain_distinct_in_one_exact_stratum()
    {
        var f = new Fixture();
        var first = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            AdaptiveEndpointEstimator.Create(), f.Label(f.First, 1, 100), 4, 8));
        var censored = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            first.State, f.Label(f.Second, 1, 150, censored: true), 4, 8));

        Assert.Equal(1u, censored.Cell.ObservedCount);
        Assert.Equal(1u, censored.Cell.RightCensoredCount);
        Assert.Equal(100m, censored.Cell.MeanNanoseconds);
    }

    [Fact]
    public void Label_revision_repairs_prior_sufficient_statistics_exactly()
    {
        var f = new Fixture();
        var first = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            AdaptiveEndpointEstimator.Create(), f.Label(f.First, 1, 100), 4, 8));
        var corrected = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            first.State, f.Label(f.First, 2, 250), 4, 8));
        var retracted = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            corrected.State, f.Label(f.First, 3, 0, retracted: true), 4, 8));

        Assert.Equal(250m, corrected.Cell.MeanNanoseconds);
        Assert.Equal(62_500m, corrected.Cell.SumSquaresNanoseconds);
        Assert.Equal(0u, retracted.Cell.ObservedCount);
        Assert.Null(retracted.Cell.MeanNanoseconds);
    }

    [Fact]
    public void Exact_retry_is_duplicate_and_same_revision_difference_is_conflict()
    {
        var f = new Fixture();
        var label = f.Label(f.First, 1, 100);
        var applied = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            AdaptiveEndpointEstimator.Create(), label, 4, 8));
        Assert.IsType<AdaptiveEstimatorResult.Duplicate>(AdaptiveEndpointEstimator.Apply(applied.State, label, 4, 8));
        Assert.Equal("outcome-label-contradiction", Assert.IsType<AdaptiveEstimatorResult.Rejected>(
            AdaptiveEndpointEstimator.Apply(applied.State, f.Label(f.First, 1, 101), 4, 8)).SafeCode.ToString());
        Assert.Equal("outcome-label-revision-conflict", Assert.IsType<AdaptiveEstimatorResult.Rejected>(
            AdaptiveEndpointEstimator.Apply(applied.State, f.Label(f.First, 3, 101), 4, 8)).SafeCode.ToString());
    }

    [Fact]
    public void Source_model_language_network_and_calibration_are_independent_strata()
    {
        var f = new Fixture();
        var first = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            AdaptiveEndpointEstimator.Create(), f.Label(f.First, 1, 100), 4, 8));
        var otherKey = new AdaptiveEstimatorKey(TranscriptSourceId.Create(), new BoundedAscii("model"),
            new BoundedAscii("en"), new BoundedAscii("wifi"), Hash256.Compute([1]));
        var second = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            first.State, new AdaptiveOutcomeLabel(f.Second, new(1), otherKey, 200, false, false), 4, 8));

        Assert.Equal(2, second.State.Cells.Count);
        Assert.Equal(100m, second.State.Cells[f.Key].MeanNanoseconds);
        Assert.Equal(200m, second.State.Cells[otherKey].MeanNanoseconds);
    }

    [Fact]
    public void Stratum_change_and_cell_or_label_capacity_fail_closed()
    {
        var f = new Fixture();
        var first = Assert.IsType<AdaptiveEstimatorResult.Applied>(AdaptiveEndpointEstimator.Apply(
            AdaptiveEndpointEstimator.Create(), f.Label(f.First, 1, 100), 1, 1));
        var changedKey = new AdaptiveEstimatorKey(TranscriptSourceId.Create(), new BoundedAscii("model"),
            new BoundedAscii("en"), new BoundedAscii("wifi"), Hash256.Compute([1]));
        Assert.Equal("outcome-label-stratum-conflict", Assert.IsType<AdaptiveEstimatorResult.Rejected>(
            AdaptiveEndpointEstimator.Apply(first.State,
                new AdaptiveOutcomeLabel(f.First, new(2), changedKey, 200, false, false), 1, 1)).SafeCode.ToString());
        Assert.Equal("outcome-label-capacity-refused", Assert.IsType<AdaptiveEstimatorResult.Rejected>(
            AdaptiveEndpointEstimator.Apply(first.State, f.Label(f.Second, 1, 200), 1, 1)).SafeCode.ToString());
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            First = OutcomeLabelId.Create();
            Second = OutcomeLabelId.Create();
            Key = new AdaptiveEstimatorKey(TranscriptSourceId.Create(), new BoundedAscii("model"),
                new BoundedAscii("en"), new BoundedAscii("wifi"), Hash256.Compute([1]));
        }
        internal OutcomeLabelId First { get; }
        internal OutcomeLabelId Second { get; }
        internal AdaptiveEstimatorKey Key { get; }
        internal AdaptiveOutcomeLabel Label(OutcomeLabelId id, uint revision, ulong duration,
            bool censored = false, bool retracted = false) => new(id, new(revision), Key, duration, censored, retracted);
    }
}
