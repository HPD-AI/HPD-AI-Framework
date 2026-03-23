namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MappedCursorTests
{
    [Fact]
    public void MappedCursor_TransformsRows()
    {
        var data = TestHelper.Data(("V", new int[] { 1, 2, 3 }));
        using var inner = data.GetCursor(["V"]);
        using var mapped = new MappedCursor(inner, row => row); // identity
        var values = new List<int>();
        while (mapped.MoveNext())
            values.Add(mapped.Current.GetValue<int>("V"));
        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public void MappedCursor_Current_BeforeMove_Throws()
    {
        var data = TestHelper.Data(("V", new int[] { 1 }));
        using var inner = data.GetCursor(["V"]);
        using var mapped = new MappedCursor(inner, row => row);
        Assert.Throws<InvalidOperationException>(() => mapped.Current);
    }

    [Fact]
    public void MappedCursor_Dispose_NoThrow()
    {
        var data = TestHelper.Data(("V", new int[] { 1 }));
        var inner = data.GetCursor(["V"]);
        var mapped = new MappedCursor(inner, row => row);
        // Dispose should not throw
        mapped.Dispose();
        // Double-dispose should also be safe
        mapped.Dispose();
    }
}
