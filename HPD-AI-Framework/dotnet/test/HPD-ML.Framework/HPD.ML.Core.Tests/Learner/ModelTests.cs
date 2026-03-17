using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ModelTests
{
    private sealed record FakeParams : ILearnedParameters;

    [Fact]
    public void Constructor_SetsTransformAndParameters()
    {
        var transform = ColumnSelectTransform.Keep("A");
        var parameters = new FakeParams();
        var model = new Model(transform, parameters);

        Assert.Same(transform, model.Transform);
        Assert.Same(parameters, model.Parameters);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var transform = ColumnSelectTransform.Keep("A");
        var parameters = new FakeParams();
        var a = new Model(transform, parameters);
        var b = new Model(transform, parameters);

        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentTransform_NotEqual()
    {
        var parameters = new FakeParams();
        var a = new Model(ColumnSelectTransform.Keep("A"), parameters);
        var b = new Model(ColumnSelectTransform.Keep("B"), parameters);

        Assert.NotEqual(a, b);
    }
}
