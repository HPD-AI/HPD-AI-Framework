using System;
using Rhodium.Primitives;

namespace Rhodium.Data.Aggregators;

/// <summary>
/// Aggregates a fixed number of trades into bars.
/// </summary>
public sealed class TickBarAggregator : IAggregator<Trade, Bar>
{
    private readonly int _tickCount;
    private Bar? _current;
    private int _count;

    public TickBarAggregator(int tickCount)
    {
        if (tickCount <= 0)
            throw new ArgumentException("Tick count must be positive", nameof(tickCount));
        _tickCount = tickCount;
    }

    public bool TryAggregate(Trade input, out Bar aggregate)
    {
        aggregate = default;
        var eventTime = input.Time.ExchangeTime;

        if (_current is null)
        {
            _current = Bar.Create(input.Price, input.Size, eventTime, Duration.Zero);
            _count = 1;
            return false;
        }

        _current = _current.Value.Update(input.Price, input.Size, eventTime);
        _count++;

        if (_count >= _tickCount)
        {
            aggregate = _current.Value;
            _current = null;
            _count = 0;
            return true;
        }

        return false;
    }

    public Bar? Flush()
    {
        var result = _current;
        _current = null;
        _count = 0;
        return result;
    }

    public void Reset()
    {
        _current = null;
        _count = 0;
    }
}
