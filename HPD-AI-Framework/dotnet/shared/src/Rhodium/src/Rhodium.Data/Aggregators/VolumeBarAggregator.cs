using System;
using Rhodium.Primitives;

namespace Rhodium.Data.Aggregators;

/// <summary>
/// Aggregates trades into volume-based bars.
/// </summary>
public sealed class VolumeBarAggregator : IAggregator<Trade, Bar>
{
    private readonly decimal _volumeThreshold;
    private Bar? _current;
    private decimal _accumulatedVolume;

    public VolumeBarAggregator(Qty volumeThreshold)
    {
        if (volumeThreshold.Value <= 0)
            throw new ArgumentException("Volume threshold must be positive", nameof(volumeThreshold));
        _volumeThreshold = volumeThreshold.Value;
    }

    public bool TryAggregate(Trade input, out Bar aggregate)
    {
        aggregate = default;
        var eventTime = input.Time.ExchangeTime;

        if (_current is null)
        {
            _current = Bar.Create(input.Price, input.Size, eventTime, Duration.Zero);
            _accumulatedVolume = input.Size.Value;
            return false;
        }

        _current = _current.Value.Update(input.Price, input.Size, eventTime);
        _accumulatedVolume += input.Size.Value;

        if (_accumulatedVolume >= _volumeThreshold)
        {
            aggregate = _current.Value;
            _current = null;
            _accumulatedVolume = 0;
            return true;
        }

        return false;
    }

    public Bar? Flush()
    {
        var result = _current;
        _current = null;
        _accumulatedVolume = 0;
        return result;
    }

    public void Reset()
    {
        _current = null;
        _accumulatedVolume = 0;
    }
}
