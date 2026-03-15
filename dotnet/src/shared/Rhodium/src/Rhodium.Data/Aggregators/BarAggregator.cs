using System;
using Rhodium.Primitives;

namespace Rhodium.Data.Aggregators;

/// <summary>
/// Aggregates trades into time-based OHLCV bars.
/// </summary>
public sealed class BarAggregator : IAggregator<Trade, Bar>
{
    private readonly Duration _period;
    private Bar? _current;
    private Instant _periodEnd;

    public BarAggregator(Duration period)
    {
        if (period.Nanos <= 0)
            throw new ArgumentException("Period must be positive", nameof(period));
        _period = period;
    }

    public bool TryAggregate(Trade input, out Bar aggregate)
    {
        aggregate = default;
        var eventTime = input.Time.ExchangeTime;

        if (_current is null)
        {
            _current = Bar.Create(input.Price, input.Size, eventTime, _period);
            _periodEnd = AlignToGrid(eventTime) + _period;
            return false;
        }

        if (eventTime >= _periodEnd)
        {
            aggregate = _current.Value;
            _current = Bar.Create(input.Price, input.Size, eventTime, _period);
            _periodEnd = AlignToGrid(eventTime) + _period;
            return true;
        }

        _current = _current.Value.Update(input.Price, input.Size, eventTime);
        return false;
    }

    public Bar? Flush()
    {
        var result = _current;
        _current = null;
        return result;
    }

    public void Reset()
    {
        _current = null;
        _periodEnd = default;
    }

    private Instant AlignToGrid(Instant time)
    {
        var nanos = time.Nanos;
        var periodNanos = _period.Nanos;
        return new Instant(nanos - (nanos % periodNanos));
    }

    // Factory methods for common periods
    public static BarAggregator Minutes(int minutes) => new(Duration.FromMinutes(minutes));
    public static BarAggregator Hours(int hours) => new(Duration.FromHours(hours));
    public static BarAggregator Daily() => new(Duration.FromDays(1));
}
