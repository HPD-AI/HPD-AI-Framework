using Rhodium.Kernel;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public sealed class StrategyGrid<TStrategy>
    where TStrategy : Strategy, IStrategyParameterFactory<TStrategy>
{
    private readonly ParameterGrid _grid;
    private readonly List<VariantDescriptor> _variants = [];

    public StrategyGrid(ParameterGrid grid)
    {
        _grid = grid;
    }

    public IReadOnlyList<VariantDescriptor> Variants => _variants;

    public IReadOnlyList<StrategyId> RegisterAll(
        StrategyTree tree,
        int depth,
        IReadOnlyList<StrategyId>? children = null)
    {
        var ids = new List<StrategyId>(_grid.Count);
        _variants.Clear();

        for (var variantIndex = 0; variantIndex < _grid.Count; variantIndex++)
        {
            var parameters = _grid.GetParametersForVariant(variantIndex);
            var strategy = TStrategy.CreateVariant(parameters);
            var id = tree.Register(strategy, depth, children);
            ids.Add(id);
            _variants.Add(new VariantDescriptor(id, variantIndex, parameters));
        }

        return ids;
    }
}
