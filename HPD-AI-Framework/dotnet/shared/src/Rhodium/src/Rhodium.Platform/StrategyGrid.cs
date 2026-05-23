using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Platform.Attributes;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public sealed class StrategyGrid<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
    TStrategy>
    where TStrategy : Strategy, new()
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
            var strategy = new TStrategy();
            AssignParameters(strategy, parameters);
            var id = tree.Register(strategy, depth, children);
            ids.Add(id);
            _variants.Add(new VariantDescriptor(id, variantIndex, parameters));
        }

        return ids;
    }

    private static void AssignParameters(TStrategy strategy, ParameterSet parameters)
    {
        foreach (var property in typeof(TStrategy).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var attr = property.GetCustomAttribute<ParamAttribute>();
            if (attr is null)
                continue;

            if (!IsSupportedParameterType(property.PropertyType))
            {
                throw new InvalidOperationException(
                    $"Parameter property '{property.Name}' uses unsupported type '{property.PropertyType.Name}'. " +
                    "Supported parameter types are int, long, double, decimal, bool, string, and enum types.");
            }

            if (property.SetMethod is null)
                throw new InvalidOperationException($"Parameter property '{property.Name}' must be settable during cold-path variant construction.");
            if (!IsInitOnly(property))
                throw new InvalidOperationException($"Parameter property '{property.Name}' must be init-only.");

            var name = attr.Name ?? property.Name;
            if (!parameters.TryGet(name, out var value))
                throw new InvalidOperationException($"Parameter grid is missing value for strategy parameter '{name}'.");

            if (value is null || !property.PropertyType.IsInstanceOfType(value))
            {
                var actual = value?.GetType().Name ?? "<null>";
                throw new InvalidOperationException(
                    $"Parameter grid value for '{name}' has type '{actual}', which cannot be assigned to strategy parameter '{property.Name}' of type '{property.PropertyType.Name}'.");
            }

            property.SetValue(strategy, value);
        }
    }

    private static bool IsSupportedParameterType(Type type)
        => type == typeof(int)
            || type == typeof(long)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(bool)
            || type == typeof(string)
            || type.IsEnum;

    private static bool IsInitOnly(PropertyInfo property)
        => property.SetMethod?.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit)) == true;
}
