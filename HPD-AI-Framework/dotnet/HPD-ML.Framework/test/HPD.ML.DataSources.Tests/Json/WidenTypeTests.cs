namespace HPD.ML.DataSources.Tests;

public class WidenTypeTests
{
    [Fact]
    public void Int_Long_ReturnsLong()
        => Assert.Equal(typeof(long), JsonDataHandle.WidenType(typeof(int), typeof(long)));

    [Fact]
    public void Int_Double_ReturnsDouble()
        => Assert.Equal(typeof(double), JsonDataHandle.WidenType(typeof(int), typeof(double)));

    [Fact]
    public void Int_String_ReturnsString()
        => Assert.Equal(typeof(string), JsonDataHandle.WidenType(typeof(int), typeof(string)));

    [Fact]
    public void Long_Double_ReturnsDouble()
        => Assert.Equal(typeof(double), JsonDataHandle.WidenType(typeof(long), typeof(double)));

    [Fact]
    public void Same_ReturnsSame()
        => Assert.Equal(typeof(int), JsonDataHandle.WidenType(typeof(int), typeof(int)));
}
