namespace Rhodium.Primitives;

/// <summary>
/// Unique identifier for replay asset-delivery receivables.
/// </summary>
public readonly record struct AssetDeliveryId(long Value)
{
    private static long _next;

    public static AssetDeliveryId New() => new(Interlocked.Increment(ref _next));

    public static implicit operator AssetDeliveryId(long value) => new(value);

    public override string ToString() => Value.ToString();
}
