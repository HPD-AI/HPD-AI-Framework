namespace HPD.ML.DataSources.Tests;

public class ParseLineTests
{
    [Fact]
    public void Simple_CommaSeparated()
    {
        var result = CsvDataHandle.ParseLine("a,b,c", ',', '"');
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void QuotedField_WithComma()
    {
        var result = CsvDataHandle.ParseLine("a,\"b,c\",d", ',', '"');
        Assert.Equal(["a", "b,c", "d"], result);
    }

    [Fact]
    public void QuotedField_EscapedQuote()
    {
        var result = CsvDataHandle.ParseLine("a,\"b\"\"c\",d", ',', '"');
        Assert.Equal(["a", "b\"c", "d"], result);
    }

    [Fact]
    public void EmptyFields()
    {
        var result = CsvDataHandle.ParseLine("a,,c", ',', '"');
        Assert.Equal(["a", "", "c"], result);
    }

    [Fact]
    public void TrailingComma()
    {
        var result = CsvDataHandle.ParseLine("a,b,", ',', '"');
        Assert.Equal(["a", "b", ""], result);
    }

    [Fact]
    public void CustomSeparator()
    {
        var result = CsvDataHandle.ParseLine("a\tb\tc", '\t', '"');
        Assert.Equal(["a", "b", "c"], result);
    }
}
