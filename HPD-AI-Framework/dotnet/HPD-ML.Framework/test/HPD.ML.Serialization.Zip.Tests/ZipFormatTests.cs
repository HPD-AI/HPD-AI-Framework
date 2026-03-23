namespace HPD.ML.Serialization.Zip.Tests;

using HPD.ML.Abstractions;

public class ZipFormatTests
{
    [Fact]
    public void FormatId_ReturnsExpected()
    {
        var format = new ZipFormat();
        Assert.Equal("hpd-ml-zip-v1", format.FormatId);
    }

    [Theory]
    [InlineData(SaveContent.LearnedParameters)]
    [InlineData(SaveContent.PipelineTopology)]
    [InlineData(SaveContent.InferenceState)]
    [InlineData(SaveContent.All)]
    public void SupportsContent_AllFlags(SaveContent content)
    {
        var format = new ZipFormat();
        Assert.True(format.SupportsContent(content));
    }
}
