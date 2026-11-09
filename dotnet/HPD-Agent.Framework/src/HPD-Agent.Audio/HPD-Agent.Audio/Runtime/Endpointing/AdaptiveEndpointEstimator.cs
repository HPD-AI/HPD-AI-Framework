using System.Collections.ObjectModel;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Endpointing;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal readonly record struct OutcomeLabelId
{
    private OutcomeLabelId(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static OutcomeLabelId Create() => new(StableId128.CreateRandom());
}

internal readonly record struct OutcomeLabelRevision
{
    internal OutcomeLabelRevision(uint value)
    { if (value == 0) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
    internal uint Value { get; }
}

internal sealed record AdaptiveEstimatorKey
{
    internal AdaptiveEstimatorKey(TranscriptSourceId sourceId, BoundedAscii model,
        BoundedAscii language, BoundedAscii networkClass, Hash256 calibration)
    {
        Span<byte> bytes = stackalloc byte[32];
        if (!sourceId.IsValid || !model.IsValid || !language.IsValid || !networkClass.IsValid ||
            !calibration.TryWriteBytes(bytes))
            throw new ArgumentException("Estimator key requires exact source/model/language/network/calibration identity.");
        SourceId = sourceId;
        Model = model;
        Language = language;
        NetworkClass = networkClass;
        Calibration = calibration;
    }
    internal TranscriptSourceId SourceId { get; }
    internal BoundedAscii Model { get; }
    internal BoundedAscii Language { get; }
    internal BoundedAscii NetworkClass { get; }
    internal Hash256 Calibration { get; }
}

internal sealed record AdaptiveOutcomeLabel
{
    internal AdaptiveOutcomeLabel(OutcomeLabelId id, OutcomeLabelRevision revision,
        AdaptiveEstimatorKey key, ulong durationNanoseconds, bool rightCensored, bool retracted)
    {
        if (!id.IsValid || key is null || (!retracted && durationNanoseconds == 0))
            throw new ArgumentException("Outcome label is invalid.");
        Id = id;
        Revision = revision;
        Key = key;
        DurationNanoseconds = durationNanoseconds;
        RightCensored = rightCensored;
        Retracted = retracted;
    }
    internal OutcomeLabelId Id { get; }
    internal OutcomeLabelRevision Revision { get; }
    internal AdaptiveEstimatorKey Key { get; }
    internal ulong DurationNanoseconds { get; }
    internal bool RightCensored { get; }
    internal bool Retracted { get; }
}

internal sealed record AdaptiveEstimatorCell(uint ObservedCount, uint RightCensoredCount,
    decimal SumNanoseconds, decimal SumSquaresNanoseconds, bool Invalidated)
{
    internal decimal? MeanNanoseconds => Invalidated || ObservedCount == 0 ? null : SumNanoseconds / ObservedCount;
}

internal sealed class AdaptiveEndpointEstimatorState
{
    private readonly ReadOnlyDictionary<AdaptiveEstimatorKey, AdaptiveEstimatorCell> _cells;
    private readonly ReadOnlyDictionary<OutcomeLabelId, AdaptiveOutcomeLabel> _labels;
    internal AdaptiveEndpointEstimatorState(
        IDictionary<AdaptiveEstimatorKey, AdaptiveEstimatorCell>? cells = null,
        IDictionary<OutcomeLabelId, AdaptiveOutcomeLabel>? labels = null)
    {
        _cells = new(cells is null ? new Dictionary<AdaptiveEstimatorKey, AdaptiveEstimatorCell>() : new(cells));
        _labels = new(labels is null ? new Dictionary<OutcomeLabelId, AdaptiveOutcomeLabel>() : new(labels));
    }
    internal IReadOnlyDictionary<AdaptiveEstimatorKey, AdaptiveEstimatorCell> Cells => _cells;
    internal IReadOnlyDictionary<OutcomeLabelId, AdaptiveOutcomeLabel> Labels => _labels;
}

internal abstract record AdaptiveEstimatorResult
{
    private AdaptiveEstimatorResult() { }
    internal sealed record Applied(AdaptiveEndpointEstimatorState State, AdaptiveEstimatorCell Cell) : AdaptiveEstimatorResult;
    internal sealed record Duplicate(AdaptiveEndpointEstimatorState State, AdaptiveOutcomeLabel Label) : AdaptiveEstimatorResult;
    internal sealed record Rejected(AdaptiveEndpointEstimatorState State, BoundedAscii SafeCode) : AdaptiveEstimatorResult;
    internal sealed record CellInvalidated(AdaptiveEndpointEstimatorState State, AdaptiveEstimatorKey Key,
        BoundedAscii SafeCode) : AdaptiveEstimatorResult;
}

internal static class AdaptiveEndpointEstimator
{
    internal static AdaptiveEndpointEstimatorState Create() => new();

    internal static AdaptiveEstimatorResult Apply(AdaptiveEndpointEstimatorState state,
        AdaptiveOutcomeLabel label, ushort maximumCells, uint maximumLabels)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(label);
        if (maximumCells == 0 || maximumLabels == 0) throw new ArgumentOutOfRangeException(nameof(maximumCells));
        if (state.Labels.TryGetValue(label.Id, out var prior))
        {
            if (label.Revision.Value == prior.Revision.Value)
                return label == prior ? new AdaptiveEstimatorResult.Duplicate(state, prior)
                    : new AdaptiveEstimatorResult.Rejected(state, new BoundedAscii("outcome-label-contradiction"));
            if (label.Revision.Value != checked(prior.Revision.Value + 1))
                return new AdaptiveEstimatorResult.Rejected(state, new BoundedAscii("outcome-label-revision-conflict"));
            if (label.Key != prior.Key)
                return new AdaptiveEstimatorResult.Rejected(state, new BoundedAscii("outcome-label-stratum-conflict"));
        }
        else
        {
            if (label.Revision.Value != 1)
                return new AdaptiveEstimatorResult.Rejected(state, new BoundedAscii("outcome-label-first-revision-invalid"));
            if (state.Labels.Count >= maximumLabels)
                return new AdaptiveEstimatorResult.Rejected(state, new BoundedAscii("outcome-label-capacity-refused"));
            if (!state.Cells.ContainsKey(label.Key) && state.Cells.Count >= maximumCells)
                return new AdaptiveEstimatorResult.Rejected(state, new BoundedAscii("estimator-cell-capacity-refused"));
        }

        var cells = Copy(state.Cells);
        var labels = Copy(state.Labels);
        cells.TryGetValue(label.Key, out var cell);
        cell ??= new AdaptiveEstimatorCell(0, 0, 0, 0, false);
        if (cell.Invalidated)
            return new AdaptiveEstimatorResult.CellInvalidated(state, label.Key, new BoundedAscii("estimator-cell-invalid"));
        try
        {
            if (prior is not null && !prior.Retracted)
                cell = Remove(cell, prior);
            if (!label.Retracted)
                cell = Add(cell, label);
        }
        catch (OverflowException)
        {
            cells[label.Key] = cell with { Invalidated = true };
            var invalid = new AdaptiveEndpointEstimatorState(cells, labels);
            return new AdaptiveEstimatorResult.CellInvalidated(invalid, label.Key, new BoundedAscii("estimator-arithmetic-overflow"));
        }
        cells[label.Key] = cell;
        labels[label.Id] = label;
        return new AdaptiveEstimatorResult.Applied(new AdaptiveEndpointEstimatorState(cells, labels), cell);
    }

    private static AdaptiveEstimatorCell Add(AdaptiveEstimatorCell cell, AdaptiveOutcomeLabel label)
    {
        if (label.RightCensored)
            return cell with { RightCensoredCount = checked(cell.RightCensoredCount + 1) };
        var value = (decimal)label.DurationNanoseconds;
        return cell with
        {
            ObservedCount = checked(cell.ObservedCount + 1),
            SumNanoseconds = checked(cell.SumNanoseconds + value),
            SumSquaresNanoseconds = checked(cell.SumSquaresNanoseconds + value * value),
        };
    }

    private static AdaptiveEstimatorCell Remove(AdaptiveEstimatorCell cell, AdaptiveOutcomeLabel label)
    {
        if (label.RightCensored)
        {
            if (cell.RightCensoredCount == 0) throw new OverflowException();
            return cell with { RightCensoredCount = cell.RightCensoredCount - 1 };
        }
        if (cell.ObservedCount == 0) throw new OverflowException();
        var value = (decimal)label.DurationNanoseconds;
        return cell with
        {
            ObservedCount = cell.ObservedCount - 1,
            SumNanoseconds = checked(cell.SumNanoseconds - value),
            SumSquaresNanoseconds = checked(cell.SumSquaresNanoseconds - value * value),
        };
    }

    private static Dictionary<TKey, TValue> Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source) where TKey : notnull =>
        source.ToDictionary(static entry => entry.Key, static entry => entry.Value);
}
