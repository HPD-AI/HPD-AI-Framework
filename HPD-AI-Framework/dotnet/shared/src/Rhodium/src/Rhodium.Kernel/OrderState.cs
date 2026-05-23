using System.Runtime.InteropServices;
using Rhodium.Primitives;

namespace Rhodium.Kernel;

/// <summary>
/// Unmanaged hot-path order state for WorldState pages.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct OrderState
{
    public long OrderIdValue;
    public Side Side;
    public decimal Quantity;
    public decimal LimitPrice;
    public byte Type;
    public byte Status;
}
