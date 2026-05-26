using Rhodium.Platform.Attributes;
using Rhodium.Platform.Patterns;

namespace Rhodium.Platform.Tests;

public sealed class ParameterGridTests
{
    [Fact]
    public void ParameterGrid_ExpandsCartesianProduct()
    {
        var grid = ParameterGrid.Create()
            .Add(nameof(ParamStrategy.RsiPeriod), 10, 14)
            .Add(nameof(ParamStrategy.UseStops), false, true);

        Assert.Equal(4, grid.Count);
        Assert.Equal(10, grid.GetParametersForVariant(0).Get<int>(nameof(ParamStrategy.RsiPeriod)));
        Assert.False(grid.GetParametersForVariant(0).Get<bool>(nameof(ParamStrategy.UseStops)));
        Assert.Equal(14, grid.GetParametersForVariant(3).Get<int>(nameof(ParamStrategy.RsiPeriod)));
        Assert.True(grid.GetParametersForVariant(3).Get<bool>(nameof(ParamStrategy.UseStops)));
    }

    [Fact]
    public void ParameterGrid_FromParameterSets_PreservesExactRows()
    {
        var grid = ParameterGrid.FromParameterSets(
        [
            new ParameterSet(new Dictionary<string, object>
            {
                [nameof(ParamStrategy.RsiPeriod)] = 10,
                [nameof(ParamStrategy.UseStops)] = false
            }),
            new ParameterSet(new Dictionary<string, object>
            {
                [nameof(ParamStrategy.RsiPeriod)] = 14,
                [nameof(ParamStrategy.UseStops)] = false
            }),
            new ParameterSet(new Dictionary<string, object>
            {
                [nameof(ParamStrategy.RsiPeriod)] = 10,
                [nameof(ParamStrategy.UseStops)] = true
            })
        ]);

        Assert.Equal(3, grid.Count);
        Assert.Equal([nameof(ParamStrategy.RsiPeriod), nameof(ParamStrategy.UseStops)], grid.ParameterNames);
        Assert.Equal(10, grid.GetParametersForVariant(0).Get<int>(nameof(ParamStrategy.RsiPeriod)));
        Assert.False(grid.GetParametersForVariant(0).Get<bool>(nameof(ParamStrategy.UseStops)));
        Assert.Equal(14, grid.GetParametersForVariant(1).Get<int>(nameof(ParamStrategy.RsiPeriod)));
        Assert.False(grid.GetParametersForVariant(1).Get<bool>(nameof(ParamStrategy.UseStops)));
        Assert.Equal(10, grid.GetParametersForVariant(2).Get<int>(nameof(ParamStrategy.RsiPeriod)));
        Assert.True(grid.GetParametersForVariant(2).Get<bool>(nameof(ParamStrategy.UseStops)));
    }

    [Fact]
    public void ParameterGrid_FromParameterSets_CannotBeExtendedAsCartesianGrid()
    {
        var grid = ParameterGrid.FromParameterSets(
        [
            new ParameterSet(new Dictionary<string, object>
            {
                [nameof(ParamStrategy.RsiPeriod)] = 10
            })
        ]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            grid.Add(nameof(ParamStrategy.UseStops), true));

        Assert.Contains("Exact-row", ex.Message);
    }

    [Fact]
    public void StrategyGrid_AssignsColdPathParamsBeforeRegistration()
    {
        var grid = ParameterGrid.Create()
            .Add(nameof(ParamStrategy.RsiPeriod), 10, 14, 21)
            .Add(nameof(ParamStrategy.UseStops), false);
        var strategyGrid = new StrategyGrid<ParamStrategy>(grid);
        var tree = new StrategyTree();

        var ids = strategyGrid.RegisterAll(tree, depth: 0);

        Assert.Equal(3, ids.Count);
        Assert.Equal(3, strategyGrid.Variants.Count);
        var periods = new int[tree.NodeCount];
        for (var i = 0; i < periods.Length; i++)
            periods[i] = ((ParamStrategy)tree.GetNode(i).Strategy).RsiPeriod;

        Assert.Equal([10, 14, 21], periods);
        Assert.Equal(ids[2], strategyGrid.Variants[2].StrategyId);
        Assert.Equal(21, strategyGrid.Variants[2].Parameters.Get<int>(nameof(ParamStrategy.RsiPeriod)));
    }

    [Fact]
    public void StrategyGrid_RegistersOneThousandVariants()
    {
        var grid = ParameterGrid.Create()
            .Add(nameof(LargeParamStrategy.Fast), Enumerable.Range(1, 10).ToArray())
            .Add(nameof(LargeParamStrategy.Slow), Enumerable.Range(11, 10).ToArray())
            .Add(nameof(LargeParamStrategy.Signal), Enumerable.Range(21, 10).ToArray());
        var strategyGrid = new StrategyGrid<LargeParamStrategy>(grid);
        var tree = new StrategyTree();

        var ids = strategyGrid.RegisterAll(tree, depth: 0);

        Assert.Equal(1_000, ids.Count);
        Assert.Equal(1_000, tree.NodeCount);
        Assert.Equal(1_000, strategyGrid.Variants.Count);
        var last = (LargeParamStrategy)tree.GetNode(tree.NodeCount - 1).Strategy;
        Assert.Equal(10, last.Fast);
        Assert.Equal(20, last.Slow);
        Assert.Equal(30, last.Signal);
    }

    [Fact]
    public void StrategyGrid_ThrowsWhenGridMissesParam()
    {
        var grid = ParameterGrid.Create()
            .Add("Other", 1);
        var strategyGrid = new StrategyGrid<ParamStrategy>(grid);
        var tree = new StrategyTree();

        var ex = Assert.Throws<InvalidOperationException>(() => strategyGrid.RegisterAll(tree, depth: 0));

        Assert.Contains(nameof(ParamStrategy.RsiPeriod), ex.Message);
    }

    [Fact]
    public void StrategyGrid_ThrowsWhenGridValueTypeIsIncompatible()
    {
        var grid = ParameterGrid.Create()
            .Add<object>(nameof(ParamStrategy.RsiPeriod), "not-an-int")
            .Add(nameof(ParamStrategy.UseStops), true);
        var strategyGrid = new StrategyGrid<ParamStrategy>(grid);
        var tree = new StrategyTree();

        var ex = Assert.Throws<InvalidOperationException>(() => strategyGrid.RegisterAll(tree, depth: 0));

        Assert.Contains(nameof(ParamStrategy.RsiPeriod), ex.Message);
        Assert.Contains("cannot be assigned", ex.Message);
    }

    [Fact]
    public void StrategyGrid_UsesGeneratedVariantFactory()
    {
        var parameters = new ParameterSet(new Dictionary<string, object>
        {
            [nameof(ParamStrategy.RsiPeriod)] = 34,
            [nameof(ParamStrategy.UseStops)] = true
        });

        var strategy = ParamStrategy.CreateVariant(parameters);

        Assert.Equal(34, strategy.RsiPeriod);
        Assert.True(strategy.UseStops);
    }
}

public sealed partial class ParamStrategy : Strategy
{
    [Param] public int RsiPeriod { get; init; }
    [Param] public bool UseStops { get; init; }
}

public sealed partial class LargeParamStrategy : Strategy
{
    [Param] public int Fast { get; init; }
    [Param] public int Slow { get; init; }
    [Param] public int Signal { get; init; }
}
