namespace Rhodium.HFT;

/// <summary>
/// One tick-indexed depth level in book order.
/// </summary>
public readonly record struct DepthLevel(long PriceTick, decimal Quantity);

internal static class DepthLevelBuffer
{
    public static void InsertBid(Span<DepthLevel> destination, ref int count, long priceTick, decimal quantity)
        => Insert(destination, ref count, new DepthLevel(priceTick, quantity), descending: true);

    public static void InsertAsk(Span<DepthLevel> destination, ref int count, long priceTick, decimal quantity)
        => Insert(destination, ref count, new DepthLevel(priceTick, quantity), descending: false);

    private static void Insert(Span<DepthLevel> destination, ref int count, DepthLevel level, bool descending)
    {
        if (destination.IsEmpty || level.Quantity <= 0m)
            return;

        var insertAt = 0;
        while (insertAt < count)
        {
            var existing = destination[insertAt].PriceTick;
            if (descending ? level.PriceTick > existing : level.PriceTick < existing)
                break;

            insertAt++;
        }

        if (insertAt >= destination.Length)
            return;

        if (count < destination.Length)
            count++;

        for (var i = count - 1; i > insertAt; i--)
            destination[i] = destination[i - 1];

        destination[insertAt] = level;
    }
}
