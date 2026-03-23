namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class OneHotEncodeTests
{
    private static readonly Dictionary<string, int> Mapping = new()
    {
        ["A"] = 0, ["B"] = 1, ["C"] = 2
    };

    [Fact]
    public void OneHot_EncodesCorrectly()
    {
        var data = TestHelper.Data(("City", new string[] { "A", "B", "C" }));
        var transform = new OneHotEncodeTransform("City", Mapping);
        var result = transform.Apply(data);
        var vectors = TestHelper.CollectFloatArray(result, "City");
        Assert.Equal([1, 0, 0], vectors[0]);
        Assert.Equal([0, 1, 0], vectors[1]);
        Assert.Equal([0, 0, 1], vectors[2]);
    }

    [Fact]
    public void OneHot_UnknownValue_AllZeros()
    {
        var data = TestHelper.Data(("City", new string[] { "D" }));
        var transform = new OneHotEncodeTransform("City", Mapping);
        var result = transform.Apply(data);
        var vectors = TestHelper.CollectFloatArray(result, "City");
        Assert.Equal([0, 0, 0], vectors[0]);
    }

    [Fact]
    public void OneHot_OutputSchema_VectorColumn()
    {
        var schema = new SchemaBuilder().AddColumn("City", new FieldType(typeof(string))).Build();
        var transform = new OneHotEncodeTransform("City", Mapping);
        var outSchema = transform.GetOutputSchema(schema);
        var col = outSchema.FindByName("City")!;
        Assert.True(col.Type.IsVector);
        Assert.Equal(typeof(float), col.Type.ClrType);
    }

    [Fact]
    public void OneHot_KeyValuesAnnotation()
    {
        var mapping = new Dictionary<string, int> { ["A"] = 0, ["B"] = 1 };
        var schema = new SchemaBuilder().AddColumn("City", new FieldType(typeof(string))).Build();
        var transform = new OneHotEncodeTransform("City", mapping);
        var outSchema = transform.GetOutputSchema(schema);
        var col = outSchema.FindByName("City")!;
        Assert.True(col.Annotations.TryGetValue<string[]>("role:KeyValues", out var keys));
        Assert.Equal(["A", "B"], keys);
    }

    [Fact]
    public void OneHot_SeparateOutputColumn()
    {
        var data = TestHelper.Data(("City", new string[] { "A" }));
        var transform = new OneHotEncodeTransform("City", Mapping, outputColumnName: "City_OH");
        var result = transform.Apply(data);
        Assert.NotNull(result.Schema.FindByName("City"));
        Assert.NotNull(result.Schema.FindByName("City_OH"));
    }

    [Fact]
    public void OneHot_PreservesRowCount()
    {
        var transform = new OneHotEncodeTransform("City", Mapping);
        Assert.True(transform.Properties.PreservesRowCount);
    }
}

public class OneHotEncodeLearnerTests
{
    [Fact]
    public void OneHotLearner_BuildsMapping()
    {
        var data = TestHelper.Data(("City", new string[] { "A", "B", "A", "C" }));
        var learner = new OneHotEncodeLearner("City");
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<OneHotParameters>(model.Parameters);
        Assert.Equal(3, p.KeyMapping.Count);
        Assert.True(p.KeyMapping.ContainsKey("A"));
        Assert.True(p.KeyMapping.ContainsKey("B"));
        Assert.True(p.KeyMapping.ContainsKey("C"));
    }

    [Fact]
    public void OneHotLearner_MaxCategories_Caps()
    {
        var values = Enumerable.Range(0, 100).Select(i => $"Val{i}").ToArray();
        var data = TestHelper.Data(("V", values));
        var learner = new OneHotEncodeLearner("V", maxCategories: 5);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<OneHotParameters>(model.Parameters);
        Assert.Equal(5, p.KeyMapping.Count);
    }

    [Fact]
    public void OneHotLearner_FitTransform_EndToEnd()
    {
        var data = TestHelper.Data(("City", new string[] { "X", "Y", "X" }));
        var learner = new OneHotEncodeLearner("City");
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var vectors = TestHelper.CollectFloatArray(result, "City");
        Assert.Equal(3, vectors.Count);
        // First and third should be identical
        Assert.Equal(vectors[0], vectors[2]);
        // First and second should differ
        Assert.NotEqual(vectors[0], vectors[1]);
    }
}
