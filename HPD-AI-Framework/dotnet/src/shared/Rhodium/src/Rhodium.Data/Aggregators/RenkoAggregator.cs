using System;
using Rhodium.Primitives;

namespace Rhodium.Data.Aggregators;

/// <summary>
/// Aggregates trades into Renko bricks (price-based, ignores time).
/// </summary>
public sealed class RenkoAggregator : IAggregator<Trade, Bar>
{
    private readonly decimal _brickSize;
    private decimal _lastClose;
    private Instant _lastTime;
    private bool _initialized;

    public RenkoAggregator(decimal brickSize)
    {
        if (brickSize <= 0)
            throw new ArgumentException("Brick size must be positive", nameof(brickSize));
        _brickSize = brickSize;
    }

    public bool TryAggregate(Trade input, out Bar aggregate)
    {
        aggregate = default;
        var price = input.Price.Value;
        _lastTime = input.Time.ExchangeTime;
        var eventTime = input.Time.ExchangeTime;

        if (!_initialized)
        {
            _lastClose = price;
            _initialized = true;
            return false;
        }

        var diff = price - _lastClose;
        if (Math.Abs(diff) >= _brickSize)
        {
            var direction = diff > 0 ? 1 : -1;
            var brickClose = _lastClose + direction * _brickSize;

            aggregate = new Bar(
                Open: new Price(_lastClose, input.Price.Currency),
                High: new Price(Math.Max(_lastClose, brickClose), input.Price.Currency),
                Low: new Price(Math.Min(_lastClose, brickClose), input.Price.Currency),
                Close: new Price(brickClose, input.Price.Currency),
                Volume: input.Size,
                Time: eventTime,
                Period: Duration.Zero // Renko has no fixed period
            );

            _lastClose = brickClose;
            return true;
        }

        return false;
    }

    public Bar? Flush() => null; // Renko doesn't flush partial bricks

    public void Reset()
    {
        _initialized = false;
        _lastClose = 0;
    }
}
