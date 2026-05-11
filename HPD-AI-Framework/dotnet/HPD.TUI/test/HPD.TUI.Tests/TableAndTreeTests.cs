using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.TUI.Tests;

public sealed class TableAndTreeTests
{
    [Fact]
    public void TableView_RendersGridWhenWide()
    {
        var model = new TableModel<Row>()
            .AddColumn("Name", row => row.Name)
            .AddColumn("Kind", row => row.Kind)
            .AddRow(new Row("alpha", "file"));
        var view = new TableView<Row>(model) { StackedBreakpoint = 10 };
        var context = new RenderContext(20, 2, Theme.Default);
        using var grid = new TerminalGrid(20, 2);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 20, ref writer);

        Assert.Equal("Name   Kind         ", ReadLine(grid, 0));
        Assert.Equal("alpha  file         ", ReadLine(grid, 1));
    }

    [Fact]
    public void TableView_RendersStackedWhenNarrow()
    {
        var model = new TableModel<Row>()
            .AddColumn("Name", row => row.Name)
            .AddColumn("Kind", row => row.Kind)
            .AddRow(new Row("alpha", "file"));
        var view = new TableView<Row>(model) { StackedBreakpoint = 20 };
        var context = new RenderContext(12, 2, Theme.Default);
        using var grid = new TerminalGrid(12, 2);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 12, ref writer);

        Assert.Equal("Name: alpha ", ReadLine(grid, 0));
        Assert.Equal("Kind: file  ", ReadLine(grid, 1));
    }

    [Fact]
    public void TableView_RendersTitleCaptionBorderAndSeparators()
    {
        var model = new TableModel<Row>
        {
            Title = "Files",
            Caption = "2 rows",
            Border = Layout.BorderSpec.Ascii,
            ShowRowSeparators = true
        }
            .AddColumn("Name", row => row.Name)
            .AddColumn("Kind", row => row.Kind)
            .AddRow(new Row("alpha", "file"))
            .AddRow(new Row("beta", "dir"));
        var view = new TableView<Row>(model) { StackedBreakpoint = 10 };

        var lines = Rendering.TuiCapture.RenderToLines(view, 20, 9);

        Assert.Equal("Files               ", lines[0]);
        Assert.Equal("+-----------+", lines[1][..13]);
        Assert.Equal("|Name   Kind|", lines[2][..13]);
        Assert.Equal("|-----------|", lines[3][..13]);
        Assert.Equal("|alpha  file|", lines[4][..13]);
        Assert.Equal("|-----------|", lines[5][..13]);
        Assert.Equal("|beta   dir |", lines[6][..13]);
        Assert.Equal("+-----------+", lines[7][..13]);
        Assert.Equal("2 rows              ", lines[8]);
    }

    [Fact]
    public void TableView_AppliesAlignmentAndEllipsis()
    {
        var model = new TableModel<Row>()
            .AddColumn(new TableColumn<Row>("Name", row => row.Name)
            {
                Width = Layout.SizePolicy.Fixed(4),
                Overflow = Layout.OverflowPolicy.Ellipsis
            })
            .AddColumn(new TableColumn<Row>("Kind", row => row.Kind)
            {
                Width = Layout.SizePolicy.Fixed(6),
                Alignment = Layout.Alignment.End
            })
            .AddRow(new Row("alphabet", "file"));
        var view = new TableView<Row>(model) { StackedBreakpoint = 10 };

        var lines = Rendering.TuiCapture.RenderToLines(view, 20, 2);

        Assert.Equal("Name    Kind        ", lines[0]);
        Assert.Equal("alp…    file        ", lines[1]);
    }

    [Fact]
    public void TableView_EllipsisUsesTerminalCellWidth()
    {
        var model = new TableModel<Row>()
            .AddColumn(new TableColumn<Row>("Name", row => row.Name)
            {
                Width = Layout.SizePolicy.Fixed(4),
                Overflow = Layout.OverflowPolicy.Ellipsis
            })
            .AddRow(new Row("你好abc", "file"));
        var view = new TableView<Row>(model) { StackedBreakpoint = 10 };

        var lines = Rendering.TuiCapture.RenderToLines(view, 10, 2);

        Assert.Equal("Name      ", lines[0]);
        Assert.Equal("你…       ", lines[1]);
    }

    [Fact]
    public void CollectionListView_MeasuresWideTextByTerminalCells()
    {
        var model = new CollectionModel<string>().Add("wide", "你好");
        var view = new CollectionListView<string>(model, new Controllers.CollectionNavigationController<string>(model));

        var measurement = view.Measure(new RenderContext(20, 1, Theme.Default), 20);

        Assert.Equal(6, measurement.MaxWidth);
    }

    [Fact]
    public void TableView_CollapsesLowPriorityColumnsBeforeStacking()
    {
        var model = new TableModel<Row>()
            .AddColumn("Name", row => row.Name, priority: 0)
            .AddColumn("Kind", row => row.Kind, priority: 10)
            .AddRow(new Row("alpha", "file"));
        var view = new TableView<Row>(model) { StackedBreakpoint = 8 };

        var lines = Rendering.TuiCapture.RenderToLines(view, 8, 2);

        Assert.Equal("Name    ", lines[0]);
        Assert.Equal("alpha   ", lines[1]);
    }

    [Fact]
    public void TableView_WiresOptionalGridNavigation()
    {
        var model = new TableModel<Row>()
            .AddColumn("Name", row => row.Name)
            .AddColumn("Kind", row => row.Kind)
            .AddRow(new Row("alpha", "file"))
            .AddRow(new Row("beta", "dir"));
        var view = new TableView<Row>(model) { EnableCellNavigation = true, StackedBreakpoint = 10 };

        _ = TuiCapture.RenderToLines(view, 20, 3);
        view.HandleInput(new KeyEvent(KeyCode.DownArrow));
        view.HandleInput(new KeyEvent(KeyCode.RightArrow));

        Assert.Equal(1, view.Navigation.Row);
        Assert.Equal(1, view.Navigation.Column);
    }

    [Fact]
    public void TreeView_RendersExpandedNodes()
    {
        var root = new TreeNode<string>("root", "root", "root")
            .Add(new TreeNode<string>("child", "child", "child"));
        var model = new TreeModel<string>().AddRoot(root);
        var view = new TreeView<string>(model);
        var context = new RenderContext(12, 2, Theme.Default);
        using var grid = new TerminalGrid(12, 2);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 12, ref writer);

        Assert.Equal("▾ root      ", ReadLine(grid, 0));
        Assert.Equal("  • child   ", ReadLine(grid, 1));
    }

    [Fact]
    public void TreeView_HidesCollapsedChildren()
    {
        var root = new TreeNode<string>("root", "root", "root")
            .Add(new TreeNode<string>("child", "child", "child"));
        var model = new TreeModel<string>().AddRoot(root);
        model.Collapse("root");
        var view = new TreeView<string>(model);
        var context = new RenderContext(12, 2, Theme.Default);
        using var grid = new TerminalGrid(12, 2);
        var writer = new SegmentWriter(grid);

        view.Render(in context, 12, ref writer);

        Assert.Equal("▸ root      ", ReadLine(grid, 0));
        Assert.Equal("            ", ReadLine(grid, 1));
    }

    [Fact]
    public void TreeController_NavigatesSelectsAndTogglesExpansion()
    {
        var root = new TreeNode<string>("root", "root", "root")
            .Add(new TreeNode<string>("child", "child", "child"));
        var model = new TreeModel<string>().AddRoot(root);
        var view = new TreeView<string>(model);
        TreeNode<string>? submitted = null;
        view.Controller.Submitted = node => submitted = node;

        Assert.Equal("root", model.SelectedKey);

        view.HandleInput(new KeyEvent(KeyCode.DownArrow));
        Assert.Equal("child", model.SelectedKey);

        view.HandleInput(new KeyEvent(KeyCode.Enter));
        Assert.Equal("child", submitted?.Key);

        view.HandleInput(new KeyEvent(KeyCode.UpArrow));
        view.HandleInput(new KeyEvent(KeyCode.LeftArrow));

        Assert.False(model.IsExpanded("root"));
    }

    [Fact]
    public void TreeController_LeafOnlySelectionSkipsGroupNodes()
    {
        var root = new TreeNode<string>("root", "root", "root")
            .Add(new TreeNode<string>("child", "child", "child"));
        var model = new TreeModel<string> { LeafOnlySelection = true }.AddRoot(root);
        var view = new TreeView<string>(model);

        Assert.Equal("child", model.SelectedKey);
    }

    [Fact]
    public void TreeView_RendersCompactAndBreadcrumbModes()
    {
        var root = new TreeNode<string>("root", "root", "root")
            .Add(new TreeNode<string>("child", "child", "child"));
        var model = new TreeModel<string>().AddRoot(root);
        model.SelectedKey = "child";

        var compact = new TreeView<string>(model) { Mode = TreeViewMode.Compact };
        var breadcrumb = new TreeView<string>(model) { Mode = TreeViewMode.Breadcrumb };

        Assert.Equal("root / child        ", TuiCapture.RenderToLines(compact, 20, 1)[0]);
        Assert.Equal("root > child        ", TuiCapture.RenderToLines(breadcrumb, 20, 1)[0]);
    }

    [Fact]
    public void TreeView_UsesViewportToKeepSelectionVisible()
    {
        var root = new TreeNode<string>("root", "root", "root")
            .Add(new TreeNode<string>("one", "one", "one"))
            .Add(new TreeNode<string>("two", "two", "two"))
            .Add(new TreeNode<string>("three", "three", "three"));
        var model = new TreeModel<string>().AddRoot(root);
        var view = new TreeView<string>(model);

        view.HandleInput(new KeyEvent(KeyCode.PageDown));
        var lines = TuiCapture.RenderToLines(view, 20, 2);

        Assert.Equal("  • two             ", lines[0]);
        Assert.Equal("  • three           ", lines[1]);
        Assert.Equal(2, model.Viewport.Offset);
    }

    private static string ReadLine(TerminalGrid grid, int y)
    {
        Span<char> buffer = stackalloc char[grid.Width];
        for (var x = 0; x < grid.Width; x++)
        {
            buffer[x] = (char)grid.GetCell(x, y).Rune.Value;
        }

        return new string(buffer);
    }

    private readonly record struct Row(string Name, string Kind);
}
