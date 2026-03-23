using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class AnnotationSetTests
{
    [Fact]
    public void Empty_HasNoKeys()
    {
        Assert.Empty(AnnotationSet.Empty.Keys);
    }

    [Fact]
    public void TryGetValue_ExistingKey_ReturnsTrue()
    {
        var set = AnnotationSet.Empty.With("role", "Label");

        Assert.True(set.TryGetValue<string>("role", out var value));
        Assert.Equal("Label", value);
    }

    [Fact]
    public void TryGetValue_MissingKey_ReturnsFalse()
    {
        Assert.False(AnnotationSet.Empty.TryGetValue<string>("nope", out _));
    }

    [Fact]
    public void TryGetValue_WrongType_ReturnsFalse()
    {
        var set = AnnotationSet.Empty.With("count", 42);

        Assert.False(set.TryGetValue<string>("count", out _));
    }

    [Fact]
    public void With_ReturnsNewInstance_OriginalUnchanged()
    {
        var original = AnnotationSet.Empty;
        var modified = original.With("k", "v");

        Assert.Empty(original.Keys);
        Assert.Single(modified.Keys);
    }

    [Fact]
    public void With_OverwritesExistingKey()
    {
        var set = AnnotationSet.Empty.With("k", 1).With("k", 2);

        Assert.True(set.TryGetValue<int>("k", out var value));
        Assert.Equal(2, value);
    }
}
