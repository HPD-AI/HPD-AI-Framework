namespace HPD.ML.Evaluation.Tests;

using HPD.ML.Core;

public class MetricHelpersTests
{
    private static HPD.ML.Abstractions.IRow MakeRow(string column, object value)
    {
        var schema = new SchemaBuilder()
            .AddColumn(column, new FieldType(value.GetType()))
            .Build();
        return new DictionaryRow(schema, new Dictionary<string, object> { [column] = value });
    }

    [Fact]
    public void ToBool_FromBool_ReturnsValue()
    {
        Assert.True(MetricHelpers.ToBool(MakeRow("x", true), "x"));
        Assert.False(MetricHelpers.ToBool(MakeRow("x", false), "x"));
    }

    [Fact]
    public void ToBool_FromFloat_UsesThreshold()
    {
        Assert.True(MetricHelpers.ToBool(MakeRow("x", 0.9f), "x"));
        Assert.False(MetricHelpers.ToBool(MakeRow("x", 0.1f), "x"));
    }

    [Fact]
    public void ToDouble_FromFloat_Converts()
    {
        double result = MetricHelpers.ToDouble(MakeRow("x", 3.14f), "x");
        Assert.Equal(3.14, result, 0.01);
    }

    [Fact]
    public void ToDouble_FromInt_Converts()
    {
        double result = MetricHelpers.ToDouble(MakeRow("x", 42), "x");
        Assert.Equal(42.0, result, 1e-10);
    }

    [Fact]
    public void ToInt_FromUint_Converts()
    {
        int result = MetricHelpers.ToInt(MakeRow("x", 7u), "x");
        Assert.Equal(7, result);
    }
}
