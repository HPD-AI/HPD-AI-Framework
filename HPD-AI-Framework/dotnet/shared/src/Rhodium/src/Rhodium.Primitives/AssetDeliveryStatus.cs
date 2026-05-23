namespace Rhodium.Primitives;

/// <summary>
/// Replay asset-delivery lifecycle status.
/// </summary>
public enum AssetDeliveryStatus : byte
{
    Scheduled = 1,
    Delivered = 2,
    Canceled = 3
}
