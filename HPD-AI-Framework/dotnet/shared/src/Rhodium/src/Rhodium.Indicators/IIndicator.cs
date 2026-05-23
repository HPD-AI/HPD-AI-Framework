using Rhodium.Primitives;

namespace Rhodium.Indicators;

/// <summary>
/// Base interface for all streaming indicators.
/// O(1) update complexity for HFT performance.
/// </summary>
public interface IIndicator<T>
{
    /// <summary>
    /// Current indicator value.
    /// </summary>
    T Value { get; }

    /// <summary>
    /// Whether the indicator has received enough data to produce valid output.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Number of samples processed.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Reset the indicator to initial state.
    /// </summary>
    void Reset();
}

/// <summary>
/// Streaming indicator that accepts price values.
/// </summary>
public interface IPriceIndicator : IIndicator<decimal>
{
    /// <summary>
    /// Update indicator with new price. O(1) operation.
    /// </summary>
    void Update(decimal price);
}

/// <summary>
/// Streaming indicator that accepts OHLCV bars.
/// </summary>
public interface IBarIndicator : IIndicator<decimal>
{
    /// <summary>
    /// Update indicator with new bar. O(1) operation.
    /// </summary>
    void Update(Bar bar);
}

/// <summary>
/// Streaming indicator that accepts tick/depth frames.
/// </summary>
public interface ITickIndicator : IIndicator<decimal>
{
    /// <summary>
    /// Update indicator with the current tick frame. O(1), allocation-free.
    /// </summary>
    void Update(in TickFrame tick);
}

/// <summary>
/// Base class for price indicators with common functionality.
/// </summary>
public abstract class PriceIndicatorBase : IPriceIndicator
{
    protected int _count;
    protected decimal _value;

    public decimal Value => _value;
    public abstract bool IsReady { get; }
    public int Count => _count;

    public abstract void Update(decimal price);

    public virtual void Reset()
    {
        _count = 0;
        _value = 0m;
    }
}

/// <summary>
/// Base class for bar indicators with common functionality.
/// </summary>
public abstract class BarIndicatorBase : IBarIndicator
{
    protected int _count;
    protected decimal _value;

    public decimal Value => _value;
    public abstract bool IsReady { get; }
    public int Count => _count;

    public abstract void Update(Bar bar);

    public virtual void Reset()
    {
        _count = 0;
        _value = 0m;
    }
}

/// <summary>
/// Base class for tick indicators with common functionality.
/// </summary>
public abstract class TickIndicatorBase : ITickIndicator
{
    protected int _count;
    protected decimal _value;

    public decimal Value => _value;
    public abstract bool IsReady { get; }
    public int Count => _count;

    public abstract void Update(in TickFrame tick);

    public virtual void Reset()
    {
        _count = 0;
        _value = 0m;
    }
}
