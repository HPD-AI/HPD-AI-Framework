using System.Collections.ObjectModel;
using HPD.Agent.Authority;
using HPD.Agent.Audio.Endpointing;

namespace HPD.Agent.Audio.Runtime.Endpointing;

internal readonly record struct OutcomeLabelIdV1
{
    private OutcomeLabelIdV1(StableId128 value) => Value = value;
    internal StableId128 Value { get; }
    internal bool IsValid => !Value.Equals(default);
    internal static OutcomeLabelIdV1 Create() => new(StableId128.CreateRandom());
}

internal readonly record struct OutcomeLabelRevisionV1
{
    internal OutcomeLabelRevisionV1(uint value)
    { if (value == 0) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
    internal uint Value { get; }
}

internal sealed record AdaptiveEstimatorKeyV1
{
    internal AdaptiveEstimatorKeyV1(TranscriptSourceIdV1 sourceId, BoundedAscii model,
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
    internal TranscriptSourceIdV1 SourceId { get; }
    internal BoundedAscii Model { get; }
    internal BoundedAscii Language { get; }
    internal BoundedAscii NetworkClass { get; }
    internal Hash256 Calibration { get; }
}

internal sealed record AdaptiveOutcomeLabelV1
{
    internal AdaptiveOutcomeLabelV1(OutcomeLabelIdV1 id, OutcomeLabelRevisionV1 revision,
        AdaptiveEstimatorKeyV1 key, ulong durationNanoseconds, bool rightCensored, bool retracted)
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
    internal OutcomeLabelIdV1 Id { get; }
    internal OutcomeLabelRevisionV1 Revision { get; }
    internal AdaptiveEstimatorKeyV1 Key { get; }
    internal ulong DurationNanoseconds { get; }
    internal bool RightCensored { get; }
    internal bool Retracted { get; }
}

internal sealed record AdaptiveEstimatorCellV1(uint ObservedCount, uint RightCensoredCount,
    decimal SumNanoseconds, decimal SumSquaresNanoseconds, bool Invalidated)
{
    internal decimal? MeanNanoseconds => Invalidated || ObservedCount == 0 ? null : SumNanoseconds / ObservedCount;
}

internal sealed class AdaptiveEndpointEstimatorStateV1
{
    private readonly ReadOnlyDictionary<AdaptiveEstimatorKeyV1, AdaptiveEstimatorCellV1> _cells;
    private readonly ReadOnlyDictionary<OutcomeLabelIdV1, AdaptiveOutcomeLabelV1> _labels;
    internal AdaptiveEndpointEstimatorStateV1(
        IDictionary<AdaptiveEstimatorKeyV1, AdaptiveEstimatorCellV1>? cells = null,
        IDictionary<OutcomeLabelIdV1, AdaptiveOutcomeLabelV1>? labels = null)
    {
        _cells = new(cells is null ? new Dictionary<AdaptiveEstimatorKeyV1, AdaptiveEstimatorCellV1>() : new(cells));
        _labels = new(labels is null ? new Dictionary<OutcomeLabelIdV1, AdaptiveOutcomeLabelV1>() : new(labels));
    }
    internal IReadOnlyDictionary<AdaptiveEstimatorKeyV1, AdaptiveEstimatorCellV1> Cells => _cells;
    internal IReadOnlyDictionary<OutcomeLabelIdV1, AdaptiveOutcomeLabelV1> Labels => _labels;
}

internal abstract record AdaptiveEstimatorResultV1
{
    private AdaptiveEstimatorResultV1() { }
    internal sealed record Applied(AdaptiveEndpointEstimatorStateV1 State, AdaptiveEstimatorCellV1 Cell) : AdaptiveEstimatorResultV1;
    internal sealed record Duplicate(AdaptiveEndpointEstimatorStateV1 State, AdaptiveOutcomeLabelV1 Label) : AdaptiveEstimatorResultV1;
    internal sealed record Rejected(AdaptiveEndpointEstimatorStateV1 State, BoundedAscii SafeCode) : AdaptiveEstimatorResultV1;
    internal sealed record CellInvalidated(AdaptiveEndpointEstimatorStateV1 State, AdaptiveEstimatorKeyV1 Key,
        BoundedAscii SafeCode) : AdaptiveEstimatorResultV1;
}

internal static class AdaptiveEndpointEstimatorV1
{
    internal static AdaptiveEndpointEstimatorStateV1 Create() => new();

    internal static AdaptiveEstimatorResultV1 Apply(AdaptiveEndpointEstimatorStateV1 state,
        AdaptiveOutcomeLabelV1 label, ushort maximumCells, uint maximumLabels)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(label);
        if (maximumCells == 0 || maximumLabels == 0) throw new ArgumentOutOfRangeException(nameof(maximumCells));
        if (state.Labels.TryGetValue(label.Id, out var prior))
        {
            if (label.Revision.Value == prior.Revision.Value)
                return label == prior ? new AdaptiveEstimatorResultV1.Duplicate(state, prior)
                    : new AdaptiveEstimatorResultV1.Rejected(state, new BoundedAscii("outcome-label-contradiction"));
            if (label.Revision.Value != checked(prior.Revision.Value + 1))
                return new AdaptiveEstimatorResultV1.Rejected(state, new BoundedAscii("outcome-label-revision-conflict"));
            if (label.Key != prior.Key)
                return new AdaptiveEstimatorResultV1.Rejected(state, new BoundedAscii("outcome-label-stratum-conflict"));
        }
        else
        {
            if (label.Revision.Value != 1)
                return new AdaptiveEstimatorResultV1.Rejected(state, new BoundedAscii("outcome-label-first-revision-invalid"));
            if (state.Labels.Count >= maximumLabels)
                return new AdaptiveEstimatorResultV1.Rejected(state, new BoundedAscii("outcome-label-capacity-refused"));
            if (!state.Cells.ContainsKey(label.Key) && state.Cells.Count >= maximumCells)
                return new AdaptiveEstimatorResultV1.Rejected(state, new BoundedAscii("estimator-cell-capacity-refused"));
        }

        var cells = Copy(state.Cells);
        var labels = Copy(state.Labels);
        cells.TryGetValue(label.Key, out var cell);
        cell ??= new AdaptiveEstimatorCellV1(0, 0, 0, 0, false);
        if (cell.Invalidated)
            return new AdaptiveEstimatorResultV1.CellInvalidated(state, label.Key, new BoundedAscii("estimator-cell-invalid"));
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
            var invalid = new AdaptiveEndpointEstimatorStateV1(cells, labels);
            return new AdaptiveEstimatorResultV1.CellInvalidated(invalid, label.Key, new BoundedAscii("estimator-arithmetic-overflow"));
        }
        cells[label.Key] = cell;
        labels[label.Id] = label;
        return new AdaptiveEstimatorResultV1.Applied(new AdaptiveEndpointEstimatorStateV1(cells, labels), cell);
    }

    private static AdaptiveEstimatorCellV1 Add(AdaptiveEstimatorCellV1 cell, AdaptiveOutcomeLabelV1 label)
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

    private static AdaptiveEstimatorCellV1 Remove(AdaptiveEstimatorCellV1 cell, AdaptiveOutcomeLabelV1 label)
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
