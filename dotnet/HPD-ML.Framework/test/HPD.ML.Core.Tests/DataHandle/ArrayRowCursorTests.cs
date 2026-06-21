using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ArrayRowCursorTests
{
    [Fact]
    public void Current_BeforeMoveNext_Throws()
    {
        var handle = TestHelpers.CreateSimpleHandle(3);
        using var cursor = handle.GetCursor(["Id"]);

        Assert.Throws<InvalidOperationException>(() => cursor.Current);
    }

    [Fact]
    public void MoveNext_AdvancesThroughAllRows()
    {
        var handle = TestHelpers.CreateSimpleHandle(3);
        using var cursor = handle.GetCursor(["Id"]);

        Assert.True(cursor.MoveNext());
        Assert.True(cursor.MoveNext());
        Assert.True(cursor.MoveNext());
        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void MoveNext_EmptyData_ReturnsFalseImmediately()
    {
        var handle = TestHelpers.CreateSimpleHandle(0);
        using var cursor = handle.GetCursor(["Id"]);

        Assert.False(cursor.MoveNext());
    }

    [Fact]
    public void Current_AfterMoveNext_ReturnsRow()
    {
        var handle = TestHelpers.CreateSimpleHandle(1);
        using var cursor = handle.GetCursor(["Id"]);

        Assert.True(cursor.MoveNext());
        Assert.Equal(0, cursor.Current.GetValue<int>("Id"));
    }
}
