namespace Rhodium.Primitives;

/// <summary>
/// Replay settlement lifecycle status.
/// </summary>
public enum SettlementStatus : byte
{
    Scheduled = 1,
    Pending = 2,
    Released = 3
}
