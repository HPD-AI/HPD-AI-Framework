using HPD.TUI.Controllers;

namespace HPD.TUI.Tests;

public sealed class GridNavigationTests
{
    [Fact]
    public void MovesRowsAndColumnsWithinBounds()
    {
        var controller = new GridNavigationController(rowCount: 3, columnCount: 2);

        controller.MoveRows(10);
        controller.MoveColumns(10);

        Assert.Equal(2, controller.Row);
        Assert.Equal(1, controller.Column);
    }

    [Fact]
    public void ResizeClampsPosition()
    {
        var controller = new GridNavigationController(rowCount: 10, columnCount: 10);
        controller.MoveRows(9);
        controller.MoveColumns(9);

        controller.Resize(rowCount: 2, columnCount: 3);

        Assert.Equal(1, controller.Row);
        Assert.Equal(2, controller.Column);
    }
}
