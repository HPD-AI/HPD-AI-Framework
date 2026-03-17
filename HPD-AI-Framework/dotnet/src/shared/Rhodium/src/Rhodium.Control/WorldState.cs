using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Control;

/// <summary>
/// Paged world state keyed by virtual index.
/// Mirrors the paged layout of ITensorStore.
/// </summary>
public sealed class WorldState
{
    private readonly List<Position[]> _positionPages = new();
    private readonly List<Order[]> _orderPages = new();

    /// <summary>
    /// Page size must match tensor store page size for alignment.
    /// </summary>
    private const int PageSize = 1024; // AlignedPage<PriceF64>.Capacity

    /// <summary>
    /// Allocate a new page when the tensor store grows.
    /// </summary>
    public void AllocatePage(int pageIndex, IBatchMap map)
    {
        if (pageIndex < _positionPages.Count) return;

        var posPage = new Position[PageSize];
        var ordPage = new Order[PageSize];

        // Initialize to avoid null checks in hot paths
        for (int i = 0; i < PageSize; i++)
        {
            var virtualIndex = pageIndex * PageSize + i;
            var (inst, _) = map.SafeGetContext(virtualIndex);
            posPage[i] = Position.Empty(inst);
            ordPage[i] = Order.Empty(inst);
        }

        _positionPages.Add(posPage);
        _orderPages.Add(ordPage);
    }

    /// <summary>
    /// Get a reference to the position at the given virtual index.
    /// </summary>
    public ref Position PositionAt(int virtualIndex) =>
        ref _positionPages[virtualIndex / PageSize][virtualIndex % PageSize];

    /// <summary>
    /// Get a reference to the order at the given virtual index.
    /// </summary>
    public ref Order OrderAt(int virtualIndex) =>
        ref _orderPages[virtualIndex / PageSize][virtualIndex % PageSize];
}
