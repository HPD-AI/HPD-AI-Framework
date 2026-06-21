using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class RowTests
{
    private static Row CreateRow(int index = 1)
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("Id")
            .AddColumn<float>("Score")
            .Build();

        var columns = new Dictionary<string, Array>
        {
            ["Id"] = new int[] { 10, 20, 30 },
            ["Score"] = new float[] { 1.1f, 2.2f, 3.3f },
        };

        return new Row(schema, columns, index);
    }

    [Fact]
    public void GetValue_ReturnsCorrectValue()
    {
        var row = CreateRow(1);

        Assert.Equal(20, row.GetValue<int>("Id"));
        Assert.Equal(2.2f, row.GetValue<float>("Score"));
    }

    [Fact]
    public void GetValue_MissingColumn_ThrowsKeyNotFound()
    {
        var row = CreateRow();
        Assert.Throws<KeyNotFoundException>(() => row.GetValue<int>("Bogus"));
    }

    [Fact]
    public void TryGetValue_ExistingColumn_ReturnsTrue()
    {
        var row = CreateRow(0);

        Assert.True(row.TryGetValue<int>("Id", out var val));
        Assert.Equal(10, val);
    }

    [Fact]
    public void TryGetValue_MissingColumn_ReturnsFalse()
    {
        var row = CreateRow();
        Assert.False(row.TryGetValue<int>("Bogus", out _));
    }

    [Fact]
    public void GetValue_MultipleTypes()
    {
        var row = CreateRow(2);

        Assert.Equal(30, row.GetValue<int>("Id"));
        Assert.Equal(3.3f, row.GetValue<float>("Score"));
    }
}
