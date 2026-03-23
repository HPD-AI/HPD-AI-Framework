namespace Rhodium.Data;

/// <summary>
/// Linear-rewind buffer for contiguous history windows.
/// Provides allocation-free access to historical data for indicators and analysis.
/// </summary>
public sealed class ShadowBuffer
{
    private readonly double[] _memory;
    private int _head;
    private readonly int _lookback;
    private bool _isWarmedUp;

    /// <summary>
    /// Creates a new ShadowBuffer with the specified lookback period.
    /// </summary>
    /// <param name="lookback">Number of historical values to maintain</param>
    public ShadowBuffer(int lookback)
    {
        _lookback = lookback;
        _memory = new double[lookback * 2]; // Double buffer for linear rewind
    }

    /// <summary>
    /// Pushes a new value into the buffer.
    /// </summary>
    /// <param name="value">Value to add to the buffer</param>
    public void Push(double value)
    {
        _memory[_head++] = value;
        if (_head >= _lookback) _isWarmedUp = true;

        // Linear rewind: when we hit the end of the buffer, copy the last lookback values
        // to the beginning and reset the head pointer
        if (_head >= _memory.Length)
        {
            Array.Copy(_memory, _head - _lookback, _memory, 0, _lookback);
            _head = _lookback;
        }
    }

    /// <summary>
    /// Gets a contiguous window of historical values.
    /// Returns Span.Empty during warmup period.
    /// </summary>
    /// <returns>ReadOnlySpan containing the historical window, or empty during warmup</returns>
    public ReadOnlySpan<double> GetWindow()
    {
        if (!_isWarmedUp)
            return ReadOnlySpan<double>.Empty;

        return new ReadOnlySpan<double>(_memory, _head - _lookback, _lookback);
    }
}
