namespace Rhodium.Primitives;

/// <summary>
/// Replay asset-delivery lifecycle status.
/// </summary>
public enum AssetDeliveryStatus : byte
{
    Scheduled = 1,
    Pending = 2,
    Delivered = 3,
    Canceled = 4
}
