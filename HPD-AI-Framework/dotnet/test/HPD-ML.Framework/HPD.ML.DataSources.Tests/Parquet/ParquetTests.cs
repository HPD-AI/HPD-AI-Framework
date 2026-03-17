namespace HPD.ML.DataSources.Tests;

public class ParquetTests
{
    [Fact]
    public void Create_ThrowsNotImplemented()
    {
        Assert.Throws<NotImplementedException>(() =>
            ParquetDataHandle.Create("nonexistent.parquet"));
    }

    [Fact]
    public void ParquetWriter_ThrowsNotImplemented()
    {
        var data = HPD.ML.Core.InMemoryDataHandle.FromColumns(
            ("V", new int[] { 1, 2, 3 }));

        Assert.Throws<NotImplementedException>(() =>
            ParquetWriter.Write(data, "output.parquet"));
    }

    [Fact]
    public void ParquetOptions_Defaults()
    {
        var options = new ParquetOptions();

        Assert.Null(options.Columns);
        Assert.Null(options.RowGroups);
        Assert.Equal(4096, options.BatchSize);
    }
}
