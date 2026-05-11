using HPD.TUI.Controllers;
using HPD.TUI.Models;

namespace HPD.TUI.Tests;

public sealed class CollectionSpineTests
{
    [Fact]
    public void Navigation_SkipsDisabledItems()
    {
        var model = new CollectionModel<int>()
            .Add(new CollectionItem<int>("one", 1, "One"))
            .Add(new CollectionItem<int>("two", 2, "Two", disabled: true))
            .Add(new CollectionItem<int>("three", 3, "Three"));
        var controller = new CollectionNavigationController<int>(model);

        controller.Move(1);

        Assert.Equal(2, controller.ActiveIndex);
        Assert.Equal("three", controller.ActiveItem?.Key);
    }

    [Fact]
    public void MultiSelection_TracksStableKeys()
    {
        var model = new MultiSelectionModel<int>()
            .Add(new CollectionItem<int>("alpha", 1, "Alpha"))
            .Add(new CollectionItem<int>("beta", 2, "Beta"));

        var toggled = model.Toggle("beta");

        Assert.True(toggled);
        Assert.True(model.IsSelected(1));
        Assert.Contains("beta", model.SelectedKeys);
        Assert.Equal([2], model.GetSelectedValues());
    }

    [Fact]
    public void SelectionState_ReturnsSelectedValueByStableKey()
    {
        var model = new SelectionModel<int>()
            .Add(new CollectionItem<int>("alpha", 1, "Alpha"))
            .Add(new CollectionItem<int>("beta", 2, "Beta"));
        model.Selection.SelectedKey = "beta";

        Assert.Equal(2, model.Selection.GetSelectedValue(model));
    }

    [Fact]
    public void Viewport_TracksActiveItem()
    {
        var model = new CollectionModel<int>()
            .Add(1, "One")
            .Add(2, "Two")
            .Add(3, "Three");
        var controller = new CollectionNavigationController<int>(model);
        controller.Viewport.SetWindowSize(2, model.Items.Count);

        controller.Move(2);

        Assert.Equal(2, controller.ActiveIndex);
        Assert.Equal(1, controller.Viewport.Offset);
    }

    [Fact]
    public void Filter_MapsVisibleRowsToSourceRows()
    {
        var model = new CollectionModel<int> { AllowFilter = true }
            .Add(1, "Alpha")
            .Add(2, "Beta")
            .Add(3, "Gamma");

        model.Query = "a";

        Assert.Equal(3, model.VisibleCount);
        Assert.Equal(1, model.GetVisibleIndex(1));
        Assert.Equal(2, model.GetSourceIndexAtVisibleIndex(2));

        model.Query = "mm";

        Assert.Equal(1, model.VisibleCount);
        Assert.Equal(2, model.GetSourceIndexAtVisibleIndex(0));
    }

    [Fact]
    public void Navigation_IgnoresFilteredOutItems()
    {
        var model = new CollectionModel<int> { AllowFilter = true }
            .Add(1, "Alpha")
            .Add(2, "Beta")
            .Add(3, "Gamma");
        model.Query = "mm";
        var controller = new CollectionNavigationController<int>(model);

        controller.MoveFirst();

        Assert.Equal(2, controller.ActiveIndex);
        Assert.Equal(0, controller.Viewport.Offset);
    }

    [Fact]
    public void VirtualCollectionSource_GeneratesItemsByIndex()
    {
        var source = new VirtualCollectionSource<int>(
            100_000,
            static index => new CollectionItem<int>(index.ToString(System.Globalization.CultureInfo.InvariantCulture), index, $"Item {index}"));
        var model = new CollectionModel<int>(source);

        Assert.Equal(100_000, model.Items.Count);
        Assert.Equal(42, model.Items[42].Value);
    }

    [Fact]
    public void ProjectedAndFilteredSources_Compose()
    {
        var source = new ListCollectionSource<int>()
            .Add(new CollectionItem<int>("one", 1, "One"))
            .Add(new CollectionItem<int>("two", 2, "Two"));
        var filtered = new FilteredCollectionSource<int>(source, static item => item.Value == 2);
        var projected = new ProjectedCollectionSource<int, string>(
            filtered,
            static item => new CollectionItem<string>(item.Key, item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), item.Title));

        Assert.Equal(1, projected.Count);
        Assert.Equal("2", projected.GetItem(0).Value);
    }

    [Fact]
    public async Task AsyncCollectionSource_RefreshesSnapshot()
    {
        var remote = new DelegateAsyncCollectionSource<int>(
            _ => ValueTask.FromResult(2),
            static (index, _) => ValueTask.FromResult(new CollectionItem<int>(index.ToString(System.Globalization.CultureInfo.InvariantCulture), index, $"Item {index}")));
        var source = new AsyncCollectionSource<int>(remote);

        await source.RefreshAsync();

        Assert.Equal(2, source.Count);
        Assert.Equal("Item 1", source.GetItem(1).Title);
    }
}
