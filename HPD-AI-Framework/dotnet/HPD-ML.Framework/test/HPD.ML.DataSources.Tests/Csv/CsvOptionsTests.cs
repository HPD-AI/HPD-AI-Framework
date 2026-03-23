namespace HPD.ML.DataSources.Tests;

public class CsvOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new CsvOptions();

        Assert.Equal(',', options.Separator);
        Assert.True(options.HasHeader);
        Assert.Equal('"', options.Quote);
        Assert.Null(options.CommentPrefix);
        Assert.Equal(System.Text.Encoding.UTF8, options.Encoding);
        Assert.Null(options.TypeHints);
        Assert.Equal(100, options.InferenceScanRows);
        Assert.Equal(MissingValuePolicy.DefaultValue, options.MissingValuePolicy);
        Assert.Equal(0, options.SkipRows);
        Assert.Null(options.MaxRows);
    }

    [Fact]
    public void Record_Equality()
    {
        var a = new CsvOptions { Separator = '\t' };
        var b = new CsvOptions { Separator = '\t' };
        Assert.Equal(a, b);
    }

    [Fact]
    public void With_Overrides()
    {
        var original = new CsvOptions();
        var modified = original with { Separator = '\t' };
        Assert.Equal('\t', modified.Separator);
        Assert.Equal(',', original.Separator);
    }
}
