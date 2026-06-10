using System.Runtime.InteropServices;
using Rhodium.Primitives;

namespace Rhodium.Kernel;

[StructLayout(LayoutKind.Sequential)]
public readonly struct AllocationCommand
{
    public StrategyId TargetStrategy { get; init; }
    public decimal AllocationWeight { get; init; }
    public bool HasAllocationWeight { get; init; }
    public decimal MaxCapitalAmount { get; init; }
    public bool HasMaxCapital { get; init; }
    public bool Pause { get; init; }
    public bool HasPause { get; init; }
}
