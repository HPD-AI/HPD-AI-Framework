using System.Runtime.CompilerServices;
using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Simulation.Diagnostics;

namespace Rhodium.Simulation.Data;

/// <summary>
/// Finance-level deterministic data iterator for a simulation run.
/// </summary>
public sealed class SimulationDataIterator
{
    /// <summary>Create an iterator from a simulation data plan.</summary>
    public SimulationDataIterator(SimulationDataPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
    }

    /// <summary>Data plan used by this iterator.</summary>
    public SimulationDataPlan Plan { get; }

    /// <summary>Provenance for the plan using its configured read options.</summary>
    public IReadOnlyList<SimulationDataProvenance> Provenance
        => GetProvenance();

    /// <summary>Create provenance records for the effective read options.</summary>
    public IReadOnlyList<SimulationDataProvenance> GetProvenance(ReplayReadOptions? readOptions = null)
    {
        var (effectiveOptions, _) = CombineReadOptions(Plan.ReadOptions, readOptions ?? ReplayReadOptions.All);
        var provenance = new SimulationDataProvenance[Plan.SourceCount];
        for (var ordinal = 0; ordinal < provenance.Length; ordinal++)
        {
            var source = Plan.GetSource(ordinal);
            provenance[ordinal] = new SimulationDataProvenance(
                source.SourceId,
                source.Priority,
                ordinal,
                source.SourceKind,
                effectiveOptions.From,
                effectiveOptions.To,
                effectiveOptions.EventFlowId,
                effectiveOptions.Limit);
        }

        return provenance;
    }

    /// <summary>Read all events using the plan read options.</summary>
    public IAsyncEnumerable<FinanceEvent> ReadAsync(CancellationToken ct = default)
        => ReadAsync(ReplayReadOptions.All, ct);

    /// <summary>Read events using plan read options intersected with run read options.</summary>
    public IAsyncEnumerable<FinanceEvent> ReadAsync(
        ReplayReadOptions readOptions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(readOptions);
        var (effectiveOptions, isEmpty) = CombineReadOptions(Plan.ReadOptions, readOptions);
        if (isEmpty)
            return EmptyAsync(ct);

        var timeline = ReplayTimeline<FinanceEvent>
            .Create()
            .WithOrdering(FinanceReplayOrderingPolicy.Default);

        for (var i = 0; i < Plan.SourceCount; i++)
        {
            var source = Plan.GetSource(i);
            timeline.AddSource(source.SourceId, source.Source, source.Priority);
        }

        return ReadMergedAsync(timeline, effectiveOptions, ct);
    }

    private static async IAsyncEnumerable<FinanceEvent> ReadMergedAsync(
        ReplayTimeline<FinanceEvent> timeline,
        ReplayReadOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sourceOptions = options with { From = null, To = null, Limit = null };
        var emitted = 0;
        await foreach (var evt in timeline.ReadAsync(sourceOptions, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!MatchesFinanceReadOptions(evt, options))
                continue;

            yield return evt;
            emitted++;
            if (options.Limit is { } limit && emitted >= limit)
                yield break;
        }
    }

    private static (ReplayReadOptions Options, bool IsEmpty) CombineReadOptions(
        ReplayReadOptions plan,
        ReplayReadOptions run)
    {
        var from = Max(plan.From, run.From);
        var to = Min(plan.To, run.To);
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
            return (new ReplayReadOptions(from, to, plan.EventFlowId ?? run.EventFlowId, 0), true);

        string? eventFlowId;
        if (plan.EventFlowId is null)
        {
            eventFlowId = run.EventFlowId;
        }
        else if (run.EventFlowId is null || string.Equals(plan.EventFlowId, run.EventFlowId, StringComparison.Ordinal))
        {
            eventFlowId = plan.EventFlowId;
        }
        else
        {
            return (new ReplayReadOptions(from, to, plan.EventFlowId, 0), true);
        }

        var limit = (plan.Limit, run.Limit) switch
        {
            (null, null) => (int?)null,
            ({ } planLimit, null) => planLimit,
            (null, { } runLimit) => runLimit,
            ({ } planLimit, { } runLimit) => Math.Min(planLimit, runLimit)
        };

        return (new ReplayReadOptions(from, to, eventFlowId, limit), false);
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
        => (left, right) switch
        {
            (null, null) => null,
            ({ } value, null) => value,
            (null, { } value) => value,
            ({ } leftValue, { } rightValue) => leftValue >= rightValue ? leftValue : rightValue
        };

    private static DateTimeOffset? Min(DateTimeOffset? left, DateTimeOffset? right)
        => (left, right) switch
        {
            (null, null) => null,
            ({ } value, null) => value,
            (null, { } value) => value,
            ({ } leftValue, { } rightValue) => leftValue <= rightValue ? leftValue : rightValue
        };

    private static bool MatchesFinanceReadOptions(FinanceEvent evt, ReplayReadOptions options)
    {
        if (options.EventFlowId is not null && evt.EventFlowId != options.EventFlowId)
            return false;

        var timestamp = GetFinanceEventTime(evt).ToDateTimeOffset();
        if (options.From is { } from && timestamp < from)
            return false;

        if (options.To is { } to && timestamp >= to)
            return false;

        return true;
    }

    private static Instant GetFinanceEventTime(FinanceEvent evt)
        => evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookDepthSnapshotReceived snapshot => snapshot.Time,
            BookDepth10Received snapshot => snapshot.Time,
            _ => evt.Time
        };

    private static async IAsyncEnumerable<FinanceEvent> EmptyAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
}
