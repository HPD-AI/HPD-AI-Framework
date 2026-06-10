namespace Rhodium.Primitives;

/// <summary>
/// Replay account-transfer lifecycle status.
/// </summary>
public enum AccountTransferStatus : byte
{
    Requested = 1,
    Completed = 2,
    Canceled = 3,
    Failed = 4
}
