namespace HPD.ML.DataSources.Tests;

public class JsonOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new JsonOptions();

        Assert.Null(options.IsJsonLines);
        Assert.Null(options.PropertyMapping);
        Assert.Null(options.TypeHints);
        Assert.Equal(100, options.InferenceScanRows);
        Assert.Equal(1, options.MaxFlattenDepth);
        Assert.Equal(System.Text.Encoding.UTF8, options.Encoding);
    }

    [Fact]
    public void Record_Equality()
    {
        var a = new JsonOptions { MaxFlattenDepth = 2 };
        var b = new JsonOptions { MaxFlattenDepth = 2 };
        Assert.Equal(a, b);
    }
}
