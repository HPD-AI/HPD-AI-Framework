using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ComposedTransformTests
{
    [Fact]
    public void GetOutputSchema_ChainsAllTransforms()
    {
        var select = ColumnSelectTransform.Keep("A", "B");
        var rename = new ColumnRenameTransform("A", "X");
        var composed = new ComposedTransform([select, rename]);

        var inputSchema = TestHelpers.CreateThreeColumnHandle().Schema;
        var outputSchema = composed.GetOutputSchema(inputSchema);

        Assert.Equal(2, outputSchema.Columns.Count);
        Assert.NotNull(outputSchema.FindByName("X"));
        Assert.NotNull(outputSchema.FindByName("B"));
        Assert.Null(outputSchema.FindByName("A"));
    }

    [Fact]
    public void Apply_ChainsAllTransforms()
    {
        var source = TestHelpers.CreateThreeColumnHandle(3);
        var select = ColumnSelectTransform.Keep("A");
        var rename = new ColumnRenameTransform("A", "X");
        var composed = new ComposedTransform([select, rename]);

        var result = composed.Apply(source);
        Assert.Single(result.Schema.Columns);
        Assert.Equal("X", result.Schema.Columns[0].Name);
    }

    [Fact]
    public void Properties_IsStateful_TrueIfAnyStateful()
    {
        var stateless = ColumnSelectTransform.Keep("A");
        var stateful = new FakeStatefulTransform();
        var composed = new ComposedTransform([stateless, stateful]);

        Assert.True(composed.Properties.IsStateful);
    }

    [Fact]
    public void Properties_PreservesRowCount_FalseIfAnyFalse()
    {
        var preserving = ColumnSelectTransform.Keep("A");
        var nonPreserving = new FakeNonPreservingTransform();
        var composed = new ComposedTransform([preserving, nonPreserving]);

        Assert.False(composed.Properties.PreservesRowCount);
    }

    [Fact]
    public void Properties_DevicePreference_LastNonNull()
    {
        var t1 = new FakeTransformWithDevice(new DevicePreference("gpu:0"));
        var t2 = new FakeTransformWithDevice(null);
        var composed = new ComposedTransform([t1, t2]);

        Assert.Equal("gpu:0", composed.Properties.DevicePreference?.DeviceId);
    }

    private sealed class FakeStatefulTransform : ITransform
    {
        public TransformProperties Properties => new() { IsStateful = true, PreservesRowCount = true };
        public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;
        public IDataHandle Apply(IDataHandle input) => input;
    }

    private sealed class FakeNonPreservingTransform : ITransform
    {
        public TransformProperties Properties => new() { PreservesRowCount = false };
        public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;
        public IDataHandle Apply(IDataHandle input) => input;
    }

    private sealed class FakeTransformWithDevice(DevicePreference? pref) : ITransform
    {
        public TransformProperties Properties => new() { PreservesRowCount = true, DevicePreference = pref };
        public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;
        public IDataHandle Apply(IDataHandle input) => input;
    }
}
