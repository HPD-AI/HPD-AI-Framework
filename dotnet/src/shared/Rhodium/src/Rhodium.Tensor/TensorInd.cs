namespace Rhodium.Tensor;

/// <summary>
/// Minimal technical indicator helpers for tensor operations.
/// These are reference implementations that can be extended for production use.
/// </summary>
public static class TensorInd
{
    /// <summary>
    /// Tensorized RSI over the current close vector.
    /// Assumes input contains the most recent closes (one per virtual index).
    /// For full historical RSI, callers should supply a rolling window per index.
    /// </summary>
    public static void RSI(ReadOnlySpan<double> closes, Span<double> output, int period = 14)
    {
        if (closes.Length != output.Length)
            throw new ArgumentException("Length mismatch");

        // Minimal stub: requires rolling history to be accurate.
        // Implementations can keep per-index ring buffers and compute true RSI.
        output.Clear();
    }
}
