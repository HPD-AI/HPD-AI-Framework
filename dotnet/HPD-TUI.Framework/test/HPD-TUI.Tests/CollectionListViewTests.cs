using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Rendering;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class CollectionListViewTests
{
    [Fact]
    public void Render_CanShowCategoryHeaders()
    {
        var model = new CollectionModel<int>()
            .Add(new CollectionItem<int>("open", 1, "Open", category: "Files"))
            .Add(new CollectionItem<int>("save", 2, "Save", category: "Files"));
        var navigation = new CollectionNavigationController<int>(model);
        var view = new CollectionListView<int>(model, navigation) { ShowCategories = true };

        var rendered = TuiCapture.RenderToString(view, 16, 3);

        Assert.Contains("Files", rendered);
        Assert.Contains("> Open", rendered);
        Assert.Contains("  Save", rendered);
    }

    [Fact]
    public void Render_UsesFilteredVisibleRows()
    {
        var model = new CollectionModel<int> { AllowFilter = true }
            .Add(1, "Alpha")
            .Add(2, "Beta")
            .Add(3, "Gamma");
        model.Query = "mm";
        var navigation = new CollectionNavigationController<int>(model);
        var view = new CollectionListView<int>(model, navigation);

        var rendered = TuiCapture.RenderToString(view, 16, 2);

        Assert.Contains("> Gamma", rendered);
        Assert.DoesNotContain("Alpha", rendered);
        Assert.DoesNotContain("Beta", rendered);
    }

    [Fact]
    public void Measure_ReportsRenderedVisibleRowCount()
    {
        var model = new CollectionModel<int>()
            .Add(1, "Alpha")
            .Add(2, "Beta")
            .Add(3, "Gamma");
        var navigation = new CollectionNavigationController<int>(model);
        var view = new CollectionListView<int>(model, navigation);
        var context = new RenderContext(16, 12, Theme.Default, ColorSystem.Legacy);

        var measurement = view.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(16, context.Height));

        Assert.Equal(3, measurement.Height);
    }

    [Fact]
    public void SelectionModel_DefaultsToBoundedVisibleWindow()
    {
        var model = new SelectionModel<int>();
        for (var i = 1; i <= 20; i++)
        {
            model.Add(i, $"Item {i:00}");
        }

        var navigation = new CollectionNavigationController<int>(model);
        var view = new CollectionListView<int>(model, navigation);
        var context = new RenderContext(16, 30, Theme.Default, ColorSystem.Legacy);

        var measurement = view.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(16, context.Height));

        Assert.Equal(SelectionModel<int>.DefaultMaxVisibleItems, measurement.Height);
        var rendered = TuiCapture.RenderToString(view, 16, 30);
        Assert.Contains("Item 12", rendered);
        Assert.DoesNotContain("Item 13", rendered);
    }

    [Fact]
    public void Render_ScrollsBoundedSelectionWindowAsActiveItemMoves()
    {
        var model = new SelectionModel<int>();
        for (var i = 1; i <= 20; i++)
        {
            model.Add(i, $"Item {i:00}");
        }

        var navigation = new CollectionNavigationController<int>(model);
        for (var i = 0; i < SelectionModel<int>.DefaultMaxVisibleItems; i++)
        {
            navigation.Move(1);
        }

        var view = new CollectionListView<int>(model, navigation);
        var rendered = TuiCapture.RenderToString(view, 16, 30);

        Assert.DoesNotContain("Item 01", rendered);
        Assert.Contains("> Item 13", rendered);
    }

    [Fact]
    public void FilterState_AllowsCustomPredicate()
    {
        var model = new CollectionModel<int> { AllowFilter = true }
            .Add(1, "one")
            .Add(2, "two");
        model.Filter.Predicate = static (item, query) => item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) == query;
        model.Query = "2";

        Assert.Equal(1, model.VisibleCount);
        Assert.Equal("two", model.Items[model.GetSourceIndexAtVisibleIndex(0)].Title);
    }
}
