using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Patterns;

public struct StrategyContext
{
    public Strategy Strategy;
    public StrategyNode Node;
    public PortfolioSnapshot[] ChildSnapshots;
    public int[] Counters;
    public OrderIntent[] OrderIntents;
}
